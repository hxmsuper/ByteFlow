using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;
using static ByteFlow.Localization;
namespace ByteFlow;
public sealed record AppRow(int Pid, string Name, string Path, double Down, double Up, long Bytes, bool Blocked) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsSystem { get; init; }
    private bool favorite;
    public bool IsFavorite { get => favorite; set { favorite = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("")); } }
    public string DisplayName => IsFavorite ? "★  " + Name : Name;
    private bool rateAvailable = true;
    public bool RateAvailable { get => rateAvailable; set { rateAvailable = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("")); } }
    public double Down { get; private set; } = Down;
    public double Up { get; private set; } = Up;
    public long Bytes { get; private set; } = Bytes;
    public bool Blocked { get; private set; } = Blocked;
    public void Update(AppRow value)
    {
        Down = value.Down; Up = value.Up; Bytes = value.Bytes;
        Blocked = value.Blocked; ProcessCount = value.ProcessCount;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
    }
    public int ProcessCount { get; set; } = 1;
    public bool Allowed => !Blocked;
    public bool CanControl => !string.IsNullOrEmpty(Path);
    public string Key => CanControl ? Path : "pid:" + Pid;
    public string PidLabel => Pid > 0 ? Pid.ToString() : "—";
    public string DownText => RateAvailable ? MainWindow.Format(Down) + "/s" : "—";
    public string UpText => RateAvailable ? MainWindow.Format(Up) + "/s" : "—";
    public string CompactDownText => "↓ " + DownText;
    public string CompactUpText => "↑ " + UpText;
    public string TotalText => MainWindow.Format(Bytes);
    public string State => Blocked ? "已断网" : "可联网";
    public string Action => Blocked ? "恢复联网" : "断开网络";
}
public partial class MainWindow : Window
{
    private readonly NetworkMonitor monitor = new();
    private readonly Firewall firewall = new();
    private readonly ProcessCatalog catalog = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly ObservableCollection<AppRow> displayed = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<int, (long Down, long Up)> previous = new();
    private List<AppRow> rows = new();
    private HashSet<string> blocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private double last;
    private bool failed;
    private string? selectedKey;
    private bool refreshing;
    private UsageStore usage = null!;
    private int saveTicks;
    private bool saving;
    private string? captureError;
    private readonly Dictionary<int, (long Down, long Up)> recorded = new();
    public MainWindow()
    {
        Localization.Load();
        Localization.Apply();
        InitializeComponent();
        try { usage = new UsageStore(); }
        catch (Exception e) { MessageBox.Show(T("统计数据加载失败：", "Unable to load usage data: ") + e.Message); usage = new UsageStore(System.IO.Path.Combine(UsageStore.DataDirectory, "recovery")); }
        ApplyLanguage();
        LoadViewOptions();
        Table.ItemsSource = displayed;
        Loaded += (_, _) =>
        {
            try { blocked = firewall.Blocked(); }
            catch (Exception e) { Status.Text = T("无法读取防火墙：", "Unable to read firewall: ") + e.Message; }
            _ = Task.Run(() => monitor.Run(message => Dispatcher.BeginInvoke(() =>
            {
                failed = true;
                captureError = message;
                Status.Text = T("速率采集失败：", "Rate capture failed: ") + message;
                Status.Foreground = System.Windows.Media.Brushes.Salmon;
                RateHint.Text = T("无法采集实时速率，请查看底部错误提示", "Capture unavailable. See the error below.");
            })));
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!stopping.IsCancellationRequested)
                    {
                        catalog.Scan();
                        await Task.Delay(TimeSpan.FromSeconds(5), stopping.Token);
                    }
                }
                catch (OperationCanceledException) { }
            });
            timer.Tick += (_, _) => Refresh(); timer.Start();
        };
        Closed += (_, _) => { timer.Stop(); stopping.Cancel(); monitor.Dispose(); try { usage.Save(); } catch (Exception e) { MessageBox.Show(T("保存统计失败：", "Failed to save usage: ") + e.Message); } };
    }
    private void Refresh()
    {
        double now = elapsed.Elapsed.TotalSeconds, seconds = Math.Max(0.1, now - last); last = now;
        var next = new List<AppRow>();
        double down = 0, up = 0; long total = 0;
        var processes = catalog.Snapshot;
        foreach (var item in monitor.Counters)
        {
            long received = Interlocked.Read(ref item.Value.Received), sent = Interlocked.Read(ref item.Value.Sent);
            previous.TryGetValue(item.Key, out var old);
            double d = Math.Max(0, received - old.Down) / seconds, u = Math.Max(0, sent - old.Up) / seconds;
            previous[item.Key] = (received, sent); down += d; up += u; total += received + sent;
            if (processes.TryGetValue(item.Key, out var info))
            {
                recorded.TryGetValue(item.Key, out var persisted);
                usage.Add(info.Path.Length > 0 ? info.Path : "pid:" + item.Key, info.Name, received - persisted.Down, sent - persisted.Up);
                recorded[item.Key] = (received, sent);
                next.Add(new(item.Key, info.Name, info.Path, d, u, received + sent, blocked.Contains(info.Path)) { IsSystem = info.IsSystem });
            }
        }
        var knownPids = next.Select(r => r.Pid).ToHashSet();
        foreach (var info in processes.Values)
        {
            if (!knownPids.Contains(info.Pid))
                next.Add(new(info.Pid, info.Name, info.Path, 0, 0, 0, blocked.Contains(info.Path)) { IsSystem = info.IsSystem });
        }
        next = next.GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var first = group.First();
            string name = first.Name;
            return new AppRow(first.Pid, name, first.Path, group.Sum(r => r.Down), group.Sum(r => r.Up), group.Sum(r => r.Bytes), first.Blocked) { ProcessCount = group.Count(), IsSystem = group.All(r => r.IsSystem) };
        }).ToList();
        foreach (var path in blocked)
            if (!next.Any(row => string.Equals(row.Path, path, StringComparison.OrdinalIgnoreCase))) next.Add(new(0, System.IO.Path.GetFileNameWithoutExtension(path), path, 0, 0, 0, true) { ProcessCount = 0 });
        rows = next.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        RecordHistory(now);
        Filter();
        Download.Text = monitor.IsRunning ? "↓ " + Format(down) + "/s" : "↓ —";
        Upload.Text = monitor.IsRunning ? "↑ " + Format(up) + "/s" : "↑ —";
        CompactRates.Text = Download.Text + "    " + Upload.Text;
        Total.Text = T("本次累计 ", "Session ") + Format(total);
        if (!failed)
        {
            bool waiting = monitor.EventCount == 0;
            RateHint.Text = !monitor.IsRunning ? T("正在启动采集…", "Starting capture…") : waiting ? T("采集已启动，等待网络数据；可打开网页或下载文件观察速率", "Capture started. Open a website or download a file to see rates.") : T($"实时速率 · 每秒更新 · {rows.Count(r => r.Down + r.Up > 0)} 个应用正在传输", $"Live rates · updated every second · {rows.Count(r => r.Down + r.Up > 0)} active apps");
            Status.Text = T($"● {(waiting ? "等待网络数据" : "采集中")} · {displayed.Count} 个应用 · {monitor.EventCount:N0} 条网络事件 · {blocked.Count} 个程序已禁止联网", $"● {(waiting ? "Waiting for traffic" : "Capturing")} · {displayed.Count} apps · {monitor.EventCount:N0} network events · {blocked.Count} blocked");
        }
        if (++saveTicks >= 15 && !saving)
        {
            saveTicks = 0; saving = true;
            _ = Task.Run(() => usage.Save()).ContinueWith(task => Dispatcher.BeginInvoke(() => { saving = false; if (task.IsFaulted) MessageBox.Show(this, T("保存统计失败：", "Failed to save usage: ") + task.Exception?.GetBaseException().Message); }));
        }
    }
    private void Filter()
    {
        if (Table == null) return;
        refreshing = true;
        var visible = rows.Where(r => (ShowSystem?.IsChecked == true || !r.IsSystem || r.Blocked || preferences.Favorites.Contains(r.Key)) && !preferences.Hidden.Contains(r.Key) && (OnlyFavorites?.IsChecked != true || preferences.Favorites.Contains(r.Key))).ToList();
        var keys = visible.Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = displayed.Count - 1; i >= 0; i--) if (!keys.Contains(displayed[i].Key)) displayed.RemoveAt(i);
        var existing = displayed.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var row in visible)
        {
            row.IsFavorite = preferences.Favorites.Contains(row.Key);
            row.RateAvailable = monitor.IsRunning && monitor.EventCount > 0;
            if (existing.TryGetValue(row.Key, out var match)) { match.IsFavorite = row.IsFavorite; match.RateAvailable = row.RateAvailable; match.Update(row); }
            else displayed.Add(row);
        }
        // WPF's default collection view does not re-sort changed item values automatically.
        // Refresh only when the user has chosen a sort, retaining the same row objects.
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(displayed);
        if (view.SortDescriptions.Count == 0) { view.SortDescriptions.Add(new SortDescription(nameof(AppRow.IsFavorite), ListSortDirection.Descending)); view.SortDescriptions.Add(new SortDescription(nameof(AppRow.Name), ListSortDirection.Ascending)); }
        view.Refresh();
        Table.SelectedItem = displayed.FirstOrDefault(r => string.Equals(r.Key, selectedKey, StringComparison.OrdinalIgnoreCase));
        refreshing = false;
        UpdateDetail(Table.SelectedItem as AppRow);
    }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing) return;
        var row = Table.SelectedItem as AppRow;
        selectedKey = row?.Key;
        UpdateDetail(row);
    }
    private void UpdateDetail(AppRow? row)
    {
        if (DetailName == null) return;
        DetailName.Text = row?.Name ?? T("选择一个应用", "Select an application");
        DetailPath.Text = row == null ? T("在左侧列表中选择应用，查看流量并控制联网。", "Select an app to inspect usage and control internet access.") : row.CanControl ? row.Path : T("无法读取程序路径，暂不支持控制。", "Executable path unavailable. Network control is disabled.");
        DetailRates.Text = row == null ? "—" : $"↓ {row.DownText}\n↑ {row.UpText}";
        DetailTotal.Text = row?.TotalText ?? "—";
        DetailSwitch.DataContext = row;
        DetailSwitch.IsChecked = row?.Allowed ?? false;
        DetailSwitch.IsEnabled = row?.CanControl ?? false;
        UpdateChart();
    }
    private void SystemFilterChanged(object sender, RoutedEventArgs e) => Filter();
    private void Toggle(object sender, RoutedEventArgs e)
    {
        if ((sender as ToggleButton)?.DataContext is not AppRow row) return;
        if (string.IsNullOrEmpty(row.Path)) { MessageBox.Show(this, T("无法读取此进程的程序路径，可能已退出或属于受保护的系统进程。", "Cannot read the executable path. The process may have exited or be protected."), T("无法控制", "Control unavailable")); return; }
        try { if (blocked.Contains(row.Path)) firewall.Restore(row.Path); else firewall.Block(row.Path); blocked = firewall.Blocked(); Refresh(); }
        catch (Exception error) { MessageBox.Show(this, error.Message, T("网络规则操作失败", "Firewall operation failed")); Filter(); }
    }
    private void RestoreAll(object sender, RoutedEventArgs e)
    {
        try { firewall.Restore(); blocked = firewall.Blocked(); Refresh(); }
        catch (Exception error) { MessageBox.Show(this, error.Message, T("恢复失败", "Restore failed")); }
    }
    public static string Format(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unit = 0; while (bytes >= 1024 && unit < units.Length - 1) { bytes /= 1024; unit++; }
        return $"{bytes:0.##} {units[unit]}";
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static ByteFlow.Localization;
namespace ByteFlow;
public partial class MainWindow
{
    private void ApplyLanguage()
    {
        Title = T("ByteFlow · 应用网络控制", "ByteFlow · Network Control");
        if (failed) { Status.Text = T("速率采集失败：", "Rate capture failed: ") + captureError; RateHint.Text = T("无法采集实时速率，请查看底部错误提示", "Capture unavailable. See the error below."); }
        UpdateDetail(Table.SelectedItem as AppRow);
    }
    private Window Dialog(string title, double width, double height) => new()
    {
        Owner = this, Title = title, Width = width, Height = height, MinWidth = width, MinHeight = height,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(20,30,43)), Foreground = Brushes.White
    };
    private void OpenSettings(object sender, RoutedEventArgs e)
    {
        var dialog = Dialog(T("设置", "Settings"), 480, 300);
        var panel = new StackPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { FontSize = 22, Text = T("设置", "Settings") };
        var label = new TextBlock { Text = "语言 / Language", Margin = new Thickness(0,20,0,8) };
        var languages = new ComboBox { ItemsSource = new[] { "简体中文", "English" }, SelectedIndex = Localization.Language == "en" ? 1 : 0, Padding = new Thickness(10) };
        var note = new TextBlock { Text = T("语言切换立即生效，下次启动继续使用。", "Changes apply immediately and are remembered."), Margin = new Thickness(0,16,0,0), TextWrapping = TextWrapping.Wrap };
        languages.SelectionChanged += (_, _) =>
        {
            try { Localization.Set(languages.SelectedIndex == 1 ? "en" : "zh"); ApplyLanguage(); Refresh(); dialog.Title = heading.Text = T("设置", "Settings"); note.Text = T("语言切换立即生效，下次启动继续使用。", "Changes apply immediately and are remembered."); }
            catch (Exception error) { MessageBox.Show(dialog, error.Message, T("保存设置失败", "Settings save failed")); }
        };
        panel.Children.Add(heading); panel.Children.Add(label); panel.Children.Add(languages); panel.Children.Add(note);
        dialog.Content = panel; dialog.ShowDialog();
    }
    private void OpenUsage(object sender, RoutedEventArgs e)
    {
        Refresh();
        var dialog = Dialog(T("流量统计", "Usage statistics"), 880, 560);
        var root = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top);
        var range = new ComboBox { ItemsSource = new[] { T("今天", "Today"), T("全部日期", "All dates") }, SelectedIndex = 0, Width = 160, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8) };
        var summary = new TextBlock { Margin = new Thickness(0,12,0,12), FontSize = 17 };
        top.Children.Add(range); top.Children.Add(summary); root.Children.Add(top);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var clearApp = new Button { Content = T("清理选中应用", "Clear selected app") };
        var clearAll = new Button { Content = T("清理全部统计", "Clear all usage") };
        buttons.Children.Add(clearApp); buttons.Children.Add(clearAll); bottom.Children.Add(buttons);
        bottom.Children.Add(new TextBlock { Text = T("仅统计 ByteFlow 运行期间的流量；清理不影响实时速度或断网规则。", "Tracks traffic while ByteFlow runs. Clearing keeps live rates and firewall rules."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,0) });
        bottom.Children.Add(new TextBlock { Text = UsageStore.DataDirectory, Foreground = Brushes.LightSlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,0) });
        root.Children.Add(bottom);
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, Foreground = Brushes.Black };
        foreach (var (name, binding) in new[] { (T("日期", "Date"), "Day"), (T("应用", "Application"), "Name"), (T("下载", "Downloaded"), "Down"), (T("上传", "Uploaded"), "Up"), (T("合计", "Total"), "Total") })
            grid.Columns.Add(new DataGridTextColumn { Header = name, Binding = new System.Windows.Data.Binding(binding), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        root.Children.Add(grid);
        void Reload()
        {
            string? path = (grid.SelectedItem as UsageView)?.Path;
            var entries = usage.Snapshot().Where(x => range.SelectedIndex == 1 || x.Day == DateTime.Now.ToString("yyyy-MM-dd")).ToList();
            grid.ItemsSource = entries.Select(x => new UsageView(x.Day, x.Path, x.Name, Format(x.Received), Format(x.Sent), Format(x.Total))).ToList();
            grid.SelectedItem = grid.Items.Cast<UsageView>().FirstOrDefault(x => x.Path == path);
            summary.Text = T("下载 ", "Downloaded ") + Format(entries.Sum(x => x.Received)) + "    ↑ " + Format(entries.Sum(x => x.Sent)) + T("    合计 ", "    Total ") + Format(entries.Sum(x => x.Total));
        }
        void Clear(string? path)
        {
            if (MessageBox.Show(dialog, path == null ? T("清理全部日期、全部应用的统计？此操作不可撤销。", "Clear usage for all apps and all dates? This cannot be undone.") : T("清理此应用全部日期的统计？此操作不可撤销。", "Clear all dates for this app? This cannot be undone."), T("清理统计", "Clear usage"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                Refresh();
                // Baseline already-observed counters so cleared bytes cannot reappear.
                foreach (var pair in monitor.Counters) recorded[pair.Key] = (Interlocked.Read(ref pair.Value.Received), Interlocked.Read(ref pair.Value.Sent));
                usage.Clear(path); Reload();
            }
            catch (Exception error) { MessageBox.Show(dialog, error.Message, T("清理失败", "Clear failed")); }
        }
        clearAll.Click += (_, _) => Clear(null);
        clearApp.IsEnabled = false;
        grid.SelectionChanged += (_, _) => clearApp.IsEnabled = grid.SelectedItem is UsageView;
        clearApp.Click += (_, _) => { if (grid.SelectedItem is UsageView selected) Clear(selected.Path); };
        range.SelectionChanged += (_, _) => Reload();
        var refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        refreshTimer.Tick += (_, _) => Reload();
        dialog.Closed += (_, _) => refreshTimer.Stop();
        dialog.Content = root; Reload(); refreshTimer.Start(); dialog.ShowDialog();
    }
    private sealed record UsageView(string Day, string Path, string Name, string Down, string Up, string Total);
}


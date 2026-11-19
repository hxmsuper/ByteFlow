using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using static ByteFlow.Localization;
namespace ByteFlow;
public sealed class ViewPreferences
{
    public HashSet<string> Hidden { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Favorites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool OnlyFavorites { get; set; }
    public bool ShowDetails { get; set; }
    public bool Compact { get; set; }
    public double FullWidth { get; set; } = 1054;
    public double FullHeight { get; set; } = 820;
    public double MiniWidth { get; set; } = 430;
    public double MiniHeight { get; set; } = 240;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Pinned { get; set; }
}
public partial class MainWindow
{
    private ViewPreferences preferences = new();
    private bool viewOptionsLoaded;
    private string PreferencesFile => Path.Combine(UsageStore.DataDirectory, "view.json");
    private void LoadViewOptions()
    {
        try
        {
            if (File.Exists(PreferencesFile)) preferences = JsonSerializer.Deserialize<ViewPreferences>(File.ReadAllText(PreferencesFile)) ?? new();
            preferences.Hidden = new(preferences.Hidden, StringComparer.OrdinalIgnoreCase);
            preferences.Favorites = new(preferences.Favorites, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, T("读取显示设置失败", "Unable to load view preferences")); }
        OnlyFavorites.IsChecked = preferences.OnlyFavorites;
        preferences.ShowDetails = false;
        ShowDetails.IsChecked = false;
        viewOptionsLoaded = true;
        UpdateDetailVisibility();
        Width = Math.Max(MinWidth, preferences.FullWidth);
        Height = Math.Max(MinHeight, preferences.FullHeight);
        Topmost = preferences.Pinned;
        bool rememberedFavorites = preferences.OnlyFavorites;
        if (preferences.Compact) ToggleCompact(this, new RoutedEventArgs());
        OnlyFavorites.IsChecked = rememberedFavorites;
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width); Height = Math.Min(Height, area.Height);
        if (preferences.Left is double left && double.IsFinite(left)) Left = Math.Clamp(left, area.Left, Math.Max(area.Left, area.Right - Width));
        if (preferences.Top is double top && double.IsFinite(top)) Top = Math.Clamp(top, area.Top, Math.Max(area.Top, area.Bottom - Height));
        Closing += (_, _) =>
        {
            RememberWindowSize();
            preferences.Compact = compact; preferences.Pinned = Topmost;
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            preferences.Left = bounds.Left; preferences.Top = bounds.Top;
            SaveViewOptions();
        };
        Table.SizeChanged += (_, _) => FitCompactColumns();
    }
    private void SaveViewOptions()
    {
        try
        {
            Directory.CreateDirectory(UsageStore.DataDirectory);
            File.WriteAllText(PreferencesFile + ".tmp", JsonSerializer.Serialize(preferences));
            File.Move(PreferencesFile + ".tmp", PreferencesFile, true);
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, T("保存显示设置失败", "Unable to save view preferences")); }
    }
    private void UpdateDetailVisibility()
    {
        bool wasVisible = DetailPanel.Visibility == Visibility.Visible;
        if (wasVisible != preferences.ShowDetails)
        {
            double width = WindowState == WindowState.Normal ? Width : RestoreBounds.Width;
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
            MinWidth = preferences.ShowDetails ? 1120 : 1000;
            Width = Math.Min(SystemParameters.WorkArea.Width, Math.Max(MinWidth, width + (preferences.ShowDetails ? 326 : -326)));
        }
        DetailPanel.Visibility = preferences.ShowDetails ? Visibility.Visible : Visibility.Collapsed;
        DetailColumn.Width = new GridLength(preferences.ShowDetails ? 310 : 0);
        DetailGap.Width = new GridLength(preferences.ShowDetails ? 16 : 0);
    }
    private void ViewOptionsChanged(object sender, RoutedEventArgs e)
    {
        if (!viewOptionsLoaded) return;
        preferences.OnlyFavorites = OnlyFavorites.IsChecked == true;
        preferences.ShowDetails = ShowDetails.IsChecked == true;
        UpdateDetailVisibility(); SaveViewOptions(); Filter();
    }
    private void CloseDetails(object sender, RoutedEventArgs e) => ShowDetails.IsChecked = false;
    private void ToggleFavorite(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not AppRow row) return;
        if (!preferences.Favorites.Remove(row.Key)) { preferences.Favorites.Add(row.Key); preferences.Hidden.Remove(row.Key); }
        SaveViewOptions(); Filter();
    }
    private void HideApp(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not AppRow row) return;
        preferences.Hidden.Add(row.Key); SaveViewOptions(); Filter();
    }
    private void RestoreHidden(object sender, RoutedEventArgs e)
    {
        preferences.Hidden.Clear(); SaveViewOptions(); Filter();
    }
    private void SortApplications(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        string member = e.Column.SortMemberPath;
        if (string.IsNullOrEmpty(member)) return;
        var direction = e.Column.SortDirection == System.ComponentModel.ListSortDirection.Ascending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending;
        foreach (var column in Table.Columns) column.SortDirection = null;
        e.Column.SortDirection = direction;
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(displayed);
        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new(nameof(AppRow.IsFavorite), System.ComponentModel.ListSortDirection.Descending));
            view.SortDescriptions.Add(new(member, direction));
            if (member != nameof(AppRow.Name)) view.SortDescriptions.Add(new(nameof(AppRow.Name), System.ComponentModel.ListSortDirection.Ascending));
        }
    }
}

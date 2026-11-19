using System.Windows;
using System.Windows.Controls;
namespace ByteFlow;
public partial class MainWindow
{
    private bool compact;
    private double fullWidth, fullHeight;
    private bool fullDetails;
    private bool fullFavorites;
    private void RememberWindowSize()
    {
        var size = WindowState == WindowState.Normal ? new Size(Width, Height) : RestoreBounds.Size;
        if (compact) { preferences.MiniWidth = size.Width; preferences.MiniHeight = size.Height; }
        else { preferences.FullWidth = size.Width; preferences.FullHeight = size.Height; }
    }
    private void FitCompactColumns()
    {
        Table.Columns[0].MinWidth = compact ? 80 : 130;
        Table.Columns[0].Width = compact ? new DataGridLength(Math.Max(80, Table.ActualWidth - 226)) : new DataGridLength(1, DataGridLengthUnitType.Star);
    }
    private readonly Dictionary<string, RateHistory> histories = new(StringComparer.OrdinalIgnoreCase);
    private double lastHistoryTime = -1;
    private void ToggleCompact(object sender, RoutedEventArgs e)
    {
        RememberWindowSize();
        compact = !compact;
        if (compact)
        {
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
            fullWidth = Width; fullHeight = Height; fullDetails = ShowDetails.IsChecked == true;
            fullFavorites = OnlyFavorites.IsChecked == true;
            ShowDetails.IsChecked = false;
            OnlyFavorites.IsChecked = preferences.Favorites.Count > 0;
            MinWidth = 380; MinHeight = 140; Width = Math.Max(MinWidth, preferences.MiniWidth); Height = Math.Max(MinHeight, preferences.MiniHeight);
        }
        else
        {
            MinWidth = 1000; MinHeight = 620;
            ShowDetails.IsChecked = fullDetails;
            OnlyFavorites.IsChecked = fullFavorites;
            Width = Math.Max(MinWidth, fullWidth); Height = Math.Max(MinHeight, fullHeight);
        }
        FullToolbar.Visibility = FullFilters.Visibility = FullStatus.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactToolbar.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        Table.Columns[1].Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Table.Columns[4].Visibility = Table.Columns[5].Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Table.Columns[2].Width = Table.Columns[3].Width = new DataGridLength(compact ? 102 : 110);
        ((DataGridTextColumn)Table.Columns[2]).Binding = new System.Windows.Data.Binding(compact ? nameof(AppRow.CompactDownText) : nameof(AppRow.DownText));
        ((DataGridTextColumn)Table.Columns[3]).Binding = new System.Windows.Data.Binding(compact ? nameof(AppRow.CompactUpText) : nameof(AppRow.UpText));
        Table.HeadersVisibility = compact ? DataGridHeadersVisibility.None : DataGridHeadersVisibility.Column;
        Table.RowHeight = compact ? 28 : 48;
        Table.FontSize = compact ? 11 : 12;
        RootLayout.Margin = new Thickness(compact ? 6 : 20);
        Table.EnableColumnVirtualization = !compact;
        Table.HorizontalScrollBarVisibility = compact ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        Dispatcher.BeginInvoke(() => { Table.UpdateLayout(); FitCompactColumns(); });
        Filter();
    }
    private void RecordHistory(double time)
    {
        if (!monitor.IsRunning || time - lastHistoryTime < 0.75) return;
        lastHistoryTime = time;
        foreach (var row in rows)
        {
            if (!histories.TryGetValue(row.Key, out var history)) histories[row.Key] = history = new();
            history.Add(new(time, row.Down, row.Up));
        }
    }
    private void ChartRangeChanged(object sender, SelectionChangedEventArgs e) => UpdateChart();
    private void UpdateChart()
    {
        if (HistoryChart == null || DetailPanel.Visibility != Visibility.Visible) return;
        var key = (Table.SelectedItem as AppRow)?.Key;
        var data = key != null && histories.TryGetValue(key, out var history) ? (IReadOnlyList<RatePoint>)history.Points : Array.Empty<RatePoint>();
        HistoryChart.SetData(data, elapsed.Elapsed.TotalSeconds, ChartRange.SelectedIndex switch { 0 => 60, 1 => 300, _ => 0 });
    }
}

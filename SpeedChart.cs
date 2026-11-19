using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace ByteFlow;
public readonly record struct RatePoint(double Time, double Down, double Up);
public sealed class RateHistory
{
    public List<RatePoint> Points { get; } = new();
    public void Add(RatePoint point)
    {
        if (Points.Count >= 2048)
        {
            // Compact the older half; keep the newest ~17 minutes at full resolution.
            var older = new List<RatePoint>();
            for (int i = 0; i < 1024; i += 2)
                older.Add(new(Points[i + 1].Time, (Points[i].Down + Points[i + 1].Down) / 2, (Points[i].Up + Points[i + 1].Up) / 2));
            Points.RemoveRange(0, 1024); Points.InsertRange(0, older);
        }
        Points.Add(point);
    }
}
public sealed class SpeedChart : FrameworkElement
{
    private IReadOnlyList<RatePoint> points = Array.Empty<RatePoint>();
    private double now, range;
    public void SetData(IReadOnlyList<RatePoint> data, double time, double seconds) { points = data; now = time; range = seconds; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double width = ActualWidth, height = ActualHeight;
        if (width < 80 || height < 80) return;
        void Label(string text, double x, double y, Brush color) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, color, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x,y));
        double start = range > 0 ? Math.Max(0, now - range) : (points.Count > 0 ? points[0].Time : 0);
        var visible = points.Where(p => p.Time >= start).ToList();
        double peak = Math.Max(1, visible.Count == 0 ? 1 : visible.Max(p => Math.Max(p.Down, p.Up)));
        double top = 28, bottom = height - 22, left = 4, right = width - 4;
        Label("↓ " + Localization.T("下载", "Download"), 4, 2, Brushes.Turquoise);
        Label("↑ " + Localization.T("上传", "Upload"), width / 2, 2, Brushes.CornflowerBlue);
        for (int i = 0; i < 3; i++)
        {
            double y = top + (bottom - top) * i / 2;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(43,60,78)), 1), new(left,y), new(right,y));
        }
        foreach (bool upload in new[] { false, true })
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                for (int i = 0; i < visible.Count; i++)
                {
                    var p = visible[i];
                    var point = new Point(left + (p.Time - start) / Math.Max(1, now - start) * (right - left), bottom - (upload ? p.Up : p.Down) / peak * (bottom - top));
                    if (i == 0) ctx.BeginFigure(point, false, false); else ctx.LineTo(point, true, false);
                }
            }
            dc.DrawGeometry(null, new Pen(upload ? Brushes.CornflowerBlue : Brushes.Turquoise, 1.8), geometry);
        }
        Label(MainWindow.Format(peak) + "/s", 4, 14, Brushes.LightSlateGray);
        Label(TimeSpan.FromSeconds(start).ToString(@"hh\:mm\:ss"), 4, height-16, Brushes.LightSlateGray);
        Label(TimeSpan.FromSeconds(now).ToString(@"hh\:mm\:ss"), width-50, height-16, Brushes.LightSlateGray);
        if (visible.Count < 2) Label(Localization.T("等待速度记录…", "Waiting for samples…"), 12, height/2, Brushes.LightSlateGray);
    }
}

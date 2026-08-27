using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace WarpAvalonia.Controls;

/// <summary>
/// Avalonia port of Warp.Controls.DualAxisScatter.
/// 2D scatter plot centered on origin, used for astigmatism display.
/// </summary>
public partial class DualAxisScatter : UserControl
{
    public static readonly StyledProperty<ObservableCollection<DualAxisPoint>> PointsProperty =
        AvaloniaProperty.Register<DualAxisScatter, ObservableCollection<DualAxisPoint>>(nameof(Points));
    public static readonly StyledProperty<double> AxisMaxProperty =
        AvaloniaProperty.Register<DualAxisScatter, double>(nameof(AxisMax), double.NaN);
    public static readonly StyledProperty<double> PointRadiusProperty =
        AvaloniaProperty.Register<DualAxisScatter, double>(nameof(PointRadius), 2.5);
    public static readonly StyledProperty<int> HighlightIDProperty =
        AvaloniaProperty.Register<DualAxisScatter, int>(nameof(HighlightID), -1);

    public ObservableCollection<DualAxisPoint>? Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public double AxisMax { get => GetValue(AxisMaxProperty); set => SetValue(AxisMaxProperty, value); }
    public double PointRadius { get => GetValue(PointRadiusProperty); set => SetValue(PointRadiusProperty, value); }
    public int HighlightID { get => GetValue(HighlightIDProperty); set => SetValue(HighlightIDProperty, value); }

    public Color[] PointColors { get; set; } = { Color.FromArgb(120, 0x41, 0xb1, 0xe1) };
    private Point[] _pointCenters = Array.Empty<Point>();

    public DualAxisScatter()
    {
        InitializeComponent();
        Points = new ObservableCollection<DualAxisPoint>();
    }

    static DualAxisScatter()
    {
        PointsProperty.Changed.AddClassHandler<DualAxisScatter>((s, _) => s.Render());
        AxisMaxProperty.Changed.AddClassHandler<DualAxisScatter>((s, _) => s.Render());
        PointRadiusProperty.Changed.AddClassHandler<DualAxisScatter>((s, _) => s.Render());
        BoundsProperty.Changed.AddClassHandler<DualAxisScatter>((s, _) => s.Render());
        HighlightIDProperty.Changed.AddClassHandler<DualAxisScatter>((s, _) => s.OnHighlightIDChanged());
    }

    public void Render()
    {
        try
        {
            double w = Bounds.Width, h = Bounds.Height;
            double fullRange = AxisMax * 2;
            CanvasPlot.Children.Clear();

            if (double.IsNaN(AxisMax) || w <= 0 || h <= 0) return;
            if (Points == null || Points.Count == 0) { ImagePlot.Source = null; return; }

            var colorBrushes = PointColors.Select(c => new SolidColorBrush(Color.FromArgb(120, c.R, c.G, c.B))).ToArray();
            _pointCenters = new Point[Points.Count];

            var dg = new DrawingGroup();
            using (var dc = dg.Open())
            {
                dc.PushClip(new RoundedRect(new Rect(0, 0, w, h)));
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

                for (int i = 0; i < Points.Count; i++)
                {
                    if (double.IsNaN(Points[i].X) || double.IsNaN(Points[i].Y)) continue;

                    double px = Points[i].X / fullRange * w + w / 2;
                    double py = Points[i].Y / fullRange * h + h / 2;
                    dc.DrawEllipse(colorBrushes[Points[i].ColorID % colorBrushes.Length], null, new Point(px, py), PointRadius, PointRadius);
                    _pointCenters[i] = new Point(px, py);
                }
            }
            ImagePlot.Source = new DrawingImage(dg);
        }
        catch { }
    }

    private int GetClosestPoint(Point pos)
    {
        if (_pointCenters.Length == 0) return 0;
        double minDist = double.MaxValue;
        int closest = 0;
        for (int i = 0; i < _pointCenters.Length; i++)
        {
            double dx = _pointCenters[i].X - pos.X, dy = _pointCenters[i].Y - pos.Y;
            double d = dx * dx + dy * dy;
            if (d < minDist) { minDist = d; closest = i; }
        }
        return closest;
    }

    private void OnHighlightIDChanged()
    {
        CanvasPlot.Children.Clear();
        if (Points == null || HighlightID < 0 || HighlightID >= Points.Count) return;

        var outline = new Ellipse { Width = PointRadius * 2 + 4, Height = PointRadius * 2 + 4, Stroke = Brushes.Gray, StrokeThickness = 3, IsHitTestVisible = false };
        CanvasPlot.Children.Add(outline);
        Canvas.SetLeft(outline, _pointCenters[HighlightID].X - PointRadius - 2);
        Canvas.SetTop(outline, _pointCenters[HighlightID].Y - PointRadius - 2);
    }

    private void ImagePlot_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (Points == null || Points.Count == 0 || _pointCenters.Length == 0) { HighlightID = -1; return; }
        var pos = e.GetPosition(ImagePlot);
        int id = GetClosestPoint(pos);
        double dx = _pointCenters[id].X - pos.X, dy = _pointCenters[id].Y - pos.Y;
        HighlightID = Math.Sqrt(dx * dx + dy * dy) <= PointRadius * 3 ? id : -1;
    }

    private void ImagePlot_PointerPressed(object? sender, PointerPressedEventArgs e) { }
    private void ImagePlot_PointerExited(object? sender, PointerEventArgs e) => HighlightID = -1;

    public static ObservableCollection<DualAxisPoint> GenerateTestData(int count, double spread)
    {
        var rng = new Random(42);
        var pts = new ObservableCollection<DualAxisPoint>();
        for (int i = 0; i < count; i++)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = 1.0 - rng.NextDouble();
            double x = spread * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            double y = spread * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            pts.Add(new DualAxisPoint(x, y, i % 4));
        }
        return pts;
    }
}

public struct DualAxisPoint
{
    public double X, Y;
    public int ColorID;
    public DualAxisPoint(double x, double y, int colorID) { X = x; Y = y; ColorID = colorID; }
}

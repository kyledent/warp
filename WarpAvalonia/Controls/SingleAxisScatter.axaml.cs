using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Warp.Tools;

namespace WarpAvalonia.Controls;

/// <summary>
/// Avalonia port of Warp.Controls.SingleAxisScatter.
/// 1D scatter plot with histogram sidebar, range highlight, and point interaction.
/// Used 6× in the Overview tab for defocus, phase shift, resolution, motion, particles, mask.
/// </summary>
public partial class SingleAxisScatter : UserControl
{
    public static readonly StyledProperty<ObservableCollection<ScatterPoint>> PointsProperty =
        AvaloniaProperty.Register<SingleAxisScatter, ObservableCollection<ScatterPoint>>(nameof(Points));
    public static readonly StyledProperty<double> AxisMinProperty =
        AvaloniaProperty.Register<SingleAxisScatter, double>(nameof(AxisMin), double.NaN);
    public static readonly StyledProperty<double> AxisMaxProperty =
        AvaloniaProperty.Register<SingleAxisScatter, double>(nameof(AxisMax), double.NaN);
    public static readonly StyledProperty<double> RangeHighlightMinProperty =
        AvaloniaProperty.Register<SingleAxisScatter, double>(nameof(RangeHighlightMin), 0.0);
    public static readonly StyledProperty<double> RangeHighlightMaxProperty =
        AvaloniaProperty.Register<SingleAxisScatter, double>(nameof(RangeHighlightMax), 1.0);
    public static readonly StyledProperty<double> PointRadiusProperty =
        AvaloniaProperty.Register<SingleAxisScatter, double>(nameof(PointRadius), 2.5);
    public static readonly StyledProperty<int> HighlightIDProperty =
        AvaloniaProperty.Register<SingleAxisScatter, int>(nameof(HighlightID), -1);

    public ObservableCollection<ScatterPoint>? Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public double AxisMin { get => GetValue(AxisMinProperty); set => SetValue(AxisMinProperty, value); }
    public double AxisMax { get => GetValue(AxisMaxProperty); set => SetValue(AxisMaxProperty, value); }
    public double RangeHighlightMin { get => GetValue(RangeHighlightMinProperty); set => SetValue(RangeHighlightMinProperty, value); }
    public double RangeHighlightMax { get => GetValue(RangeHighlightMaxProperty); set => SetValue(RangeHighlightMaxProperty, value); }
    public double PointRadius { get => GetValue(PointRadiusProperty); set => SetValue(PointRadiusProperty, value); }
    public int HighlightID { get => GetValue(HighlightIDProperty); set => SetValue(HighlightIDProperty, value); }

    public Color[] PointColors { get; set; } = { Color.FromArgb(200, 0x41, 0xb1, 0xe1) }; // default: Warp cyan

    private double _valueMin, _valueMax, _stepX, _offsetX, _stepY;
    private Point[] _pointCenters = Array.Empty<Point>();

    public event Action<int>? PointClicked;

    public SingleAxisScatter()
    {
        InitializeComponent();
        Points = new ObservableCollection<ScatterPoint>();
    }

    static SingleAxisScatter()
    {
        PointsProperty.Changed.AddClassHandler<SingleAxisScatter>((s, _) => s.Render());
        AxisMinProperty.Changed.AddClassHandler<SingleAxisScatter>((s, _) => s.Render());
        AxisMaxProperty.Changed.AddClassHandler<SingleAxisScatter>((s, _) => s.Render());
        PointRadiusProperty.Changed.AddClassHandler<SingleAxisScatter>((s, _) => s.Render());
        BoundsProperty.Changed.AddClassHandler<SingleAxisScatter>((s, _) => s.Render());
        RangeHighlightMinProperty.Changed.AddClassHandler<SingleAxisScatter>((s, _) => s.AdjustRangeHighlight());
        RangeHighlightMaxProperty.Changed.AddClassHandler<SingleAxisScatter>((s, _) => s.AdjustRangeHighlight());
        HighlightIDProperty.Changed.AddClassHandler<SingleAxisScatter>((s, _) => s.OnHighlightIDChanged());
    }

    public void Render()
    {
        try
        {
            double canvasWidth = Bounds.Width - 24;
            double canvasHeight = Height - 14;
            CanvasPlot.Children.Clear();

            if (canvasWidth <= 0 || canvasHeight <= 0) return;

            // Determine axis range
            _valueMin = double.MaxValue;
            _valueMax = -double.MaxValue;

            if (!double.IsNaN(AxisMin)) _valueMin = AxisMin;
            else if (Points != null && Points.Count > 0)
                foreach (var p in Points) { if (!double.IsNaN(p.Value)) _valueMin = Math.Min(_valueMin, p.Value); }
            else _valueMin = 0;

            if (!double.IsNaN(AxisMax)) _valueMax = AxisMax;
            else if (Points != null && Points.Count > 0 && Points.Any(v => !double.IsNaN(v.Value)))
                foreach (var p in Points) { if (!double.IsNaN(p.Value)) _valueMax = Math.Max(_valueMax, p.Value); }
            else _valueMax = 1;

            if (_valueMin == _valueMax) { _valueMax += 1; _valueMin -= 1; }
            else
            {
                double range = _valueMax - _valueMin;
                if (double.IsNaN(AxisMax)) _valueMax += range / canvasHeight * PointRadius;
                if (double.IsNaN(AxisMin)) _valueMin -= range / canvasHeight * PointRadius;
            }

            double totalRange = _valueMax - _valueMin;

            // Update axis labels
            string fmt = Math.Max(_valueMax, _valueMin) < 10 ? "F2" : Math.Max(_valueMax, _valueMin) < 100 ? "F1" : "F0";
            TextLineBottom.Text = _valueMin.ToString(fmt, CultureInfo.InvariantCulture);
            TextLineCenter.Text = ((_valueMin + _valueMax) * 0.5).ToString(fmt, CultureInfo.InvariantCulture);
            TextLineTop.Text = _valueMax.ToString(fmt, CultureInfo.InvariantCulture);

            AdjustRangeHighlight();

            if (totalRange <= 0 || Points == null || Points.Count == 0)
            {
                ImagePlot.Source = null;
                return;
            }

            // Render scatter points via DrawingImage
            float[] histogramBins = new float[50];
            var dg = new DrawingGroup();
            using (var dc = dg.Open())
            {
                dc.PushClip(new RoundedRect(new Rect(0, 0, canvasWidth, canvasHeight)));
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, canvasWidth, canvasHeight));

                var colorBrushes = PointColors.Select(c => new SolidColorBrush(c)).ToArray();

                _stepX = (canvasWidth - PointRadius * 2) / Points.Count;
                _offsetX = _stepX / 2 + PointRadius;
                _stepY = canvasHeight / totalRange;
                _pointCenters = new Point[Points.Count];

                for (int i = 0; i < Points.Count; i++)
                {
                    if (double.IsNaN(Points[i].Value)) continue;

                    double x = i * _stepX + _offsetX;
                    double y = (_valueMax - Points[i].Value) * _stepY;
                    dc.DrawEllipse(colorBrushes[Points[i].ColorID % colorBrushes.Length], null, new Point(x, y), PointRadius, PointRadius);
                    _pointCenters[i] = new Point(x, y);

                    int bin = Math.Clamp((int)((Points[i].Value - _valueMin) / totalRange * (histogramBins.Length - 1) + 0.5), 0, histogramBins.Length - 1);
                    histogramBins[bin]++;
                }
            }
            ImagePlot.Source = new DrawingImage(dg);

            // Render histogram
            RenderHistogram(histogramBins, canvasHeight);
        }
        catch { }
    }

    private void RenderHistogram(float[] bins, double canvasHeight)
    {
        // Kernel convolution smoothing (same as WPF)
        float[] conv = new float[bins.Length];
        float[] kernel = { 0.11f, 0.37f, 0.78f, 1f, 0.78f, 0.37f, 0.11f };
        for (int i = 0; i < bins.Length; i++)
        {
            float sum = 0, samples = 0;
            for (int j = 0; j < kernel.Length; j++)
            {
                int ij = i - 3 + j;
                if (ij < 0 || ij >= bins.Length) continue;
                sum += kernel[j] * bins[ij];
                samples += kernel[j];
            }
            conv[i] = sum / samples;
        }

        float max = conv.Max();
        if (max > 0) conv = conv.Select(v => v / max * 16).ToArray();

        var polygon = new Polygon
        {
            Stroke = Brushes.Transparent,
            Fill = Brushes.Gray,
            Opacity = 0.15
        };

        var pts = new Points();
        pts.Add(new Point(16, 0));
        pts.Add(new Point(16, canvasHeight));
        for (int i = 0; i < conv.Length; i++)
        {
            double x = 15 - conv[i];
            double y = canvasHeight - (i / (float)(conv.Length - 1) * canvasHeight);
            pts.Add(new Point(x, y));
        }
        polygon.Points = pts;

        CanvasHistogram.Children.Clear();
        CanvasHistogram.Children.Add(polygon);
    }

    private void AdjustRangeHighlight()
    {
        double range = _valueMax - _valueMin;
        double canvasHeight = Height - 14;
        if (canvasHeight <= 0 || range <= 0) return;

        double clampedMin = Math.Max(_valueMin, RangeHighlightMin);
        double clampedMax = Math.Min(_valueMax, RangeHighlightMax);

        PanelRangeHighlight.Margin = new Thickness(0, 7 + (_valueMax - clampedMax) / range * canvasHeight, 0, 0);
        PanelRangeHighlight.Height = Math.Max(0, clampedMax - clampedMin) / range * canvasHeight;
    }

    private void OnHighlightIDChanged()
    {
        CanvasPlot.Children.Clear();
        if (Points == null || HighlightID < 0 || HighlightID >= Points.Count || double.IsNaN(Points[HighlightID].Value))
            return;

        var outline = new Ellipse
        {
            Width = PointRadius * 2 + 4,
            Height = PointRadius * 2 + 4,
            Stroke = Brushes.Gray,
            StrokeThickness = 3,
            IsHitTestVisible = false
        };
        CanvasPlot.Children.Add(outline);
        Canvas.SetLeft(outline, _pointCenters[HighlightID].X - PointRadius - 2);
        Canvas.SetTop(outline, _pointCenters[HighlightID].Y - PointRadius - 2);
    }

    private void ImagePlot_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (Points == null || Points.Count == 0 || _pointCenters.Length == 0) { HighlightID = -1; return; }

        var pos = e.GetPosition(ImagePlot);
        int closest = Math.Clamp((int)Math.Round((pos.X - _offsetX) / _stepX), 0, Points.Count - 1);
        double dist = Math.Sqrt(Math.Pow(_pointCenters[closest].X - pos.X, 2) + Math.Pow(_pointCenters[closest].Y - pos.Y, 2));

        HighlightID = dist <= PointRadius * 4 ? closest : -1;
    }

    private void ImagePlot_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (HighlightID >= 0) PointClicked?.Invoke(HighlightID);
    }

    private void ImagePlot_PointerExited(object? sender, PointerEventArgs e) => HighlightID = -1;

    /// <summary>Generate test data for PoC validation.</summary>
    public static ObservableCollection<ScatterPoint> GenerateTestData(int count, double mean, double stddev)
    {
        var rng = new Random(42);
        var pts = new ObservableCollection<ScatterPoint>();
        var colors = new[] { 0, 0, 0, 0, 1, 2, 3 }; // mostly color 0, some colored
        for (int i = 0; i < count; i++)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            double val = mean + stddev * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            pts.Add(new ScatterPoint(val, colors[i % colors.Length]));
        }
        return pts;
    }
}

public struct ScatterPoint
{
    public double Value;
    public int ColorID;

    public ScatterPoint(double value, int colorID)
    {
        Value = value;
        ColorID = colorID;
    }
}

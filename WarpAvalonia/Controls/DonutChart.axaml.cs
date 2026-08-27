using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WarpAvalonia.Controls;

/// <summary>
/// Custom donut/pie chart replacing LiveCharts.Wpf.PieChart.
/// Renders arc segments via DrawingContext. Matches the WPF Warp
/// processing status chart (5 wedges: processed, outdated, unprocessed, filtered, deselected).
/// </summary>
public partial class DonutChart : UserControl
{
    public static readonly StyledProperty<string> CenterTextProperty =
        AvaloniaProperty.Register<DonutChart, string>(nameof(CenterText), "");

    public string CenterText { get => GetValue(CenterTextProperty); set => SetValue(CenterTextProperty, value); }

    private List<ChartSlice> _slices = new();

    public DonutChart()
    {
        InitializeComponent();
    }

    static DonutChart()
    {
        BoundsProperty.Changed.AddClassHandler<DonutChart>((s, _) => s.Render());
        CenterTextProperty.Changed.AddClassHandler<DonutChart>((s, _) => s.TextCenter.Text = s.CenterText);
    }

    public void SetSlices(List<ChartSlice> slices)
    {
        _slices = slices;
        Render();
    }

    public void Render()
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;

        double total = 0;
        foreach (var s in _slices) total += s.Value;
        if (total <= 0) { ImageChart.Source = null; return; }

        double cx = size / 2, cy = size / 2;
        double outerR = size / 2 - 2;
        double innerR = outerR * 0.6; // donut hole

        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));

            double startAngle = -Math.PI / 2; // start from top

            foreach (var slice in _slices)
            {
                if (slice.Value <= 0) continue;

                double sweepAngle = slice.Value / total * 2 * Math.PI;
                double endAngle = startAngle + sweepAngle;

                // Build arc path
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    // Outer arc start
                    Point outerStart = new(cx + outerR * Math.Cos(startAngle), cy + outerR * Math.Sin(startAngle));
                    Point outerEnd = new(cx + outerR * Math.Cos(endAngle), cy + outerR * Math.Sin(endAngle));
                    Point innerEnd = new(cx + innerR * Math.Cos(endAngle), cy + innerR * Math.Sin(endAngle));
                    Point innerStart = new(cx + innerR * Math.Cos(startAngle), cy + innerR * Math.Sin(startAngle));

                    bool isLargeArc = sweepAngle > Math.PI;

                    ctx.BeginFigure(outerStart, true);
                    ctx.ArcTo(outerEnd, new Size(outerR, outerR), 0, isLargeArc, SweepDirection.Clockwise);
                    ctx.LineTo(innerEnd);
                    ctx.ArcTo(innerStart, new Size(innerR, innerR), 0, isLargeArc, SweepDirection.CounterClockwise);
                    ctx.EndFigure(true);
                }

                dc.DrawGeometry(new SolidColorBrush(slice.Color), null, geometry);

                // Draw label at midpoint of arc
                double midAngle = startAngle + sweepAngle / 2;
                double labelR = (outerR + innerR) / 2;
                if (slice.Value / total > 0.05) // only label slices > 5%
                {
                    var labelPos = new Point(cx + labelR * Math.Cos(midAngle) - 10, cy + labelR * Math.Sin(midAngle) - 7);
                    var ft = new FormattedText(
                        ((int)slice.Value).ToString(),
                        System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Inter", FontStyle.Normal, FontWeight.Normal),
                        12, Brushes.DimGray);
                    dc.DrawText(ft, labelPos);
                }

                startAngle = endAngle;
            }
        }

        ImageChart.Source = new DrawingImage(dg);
    }
}

public struct ChartSlice
{
    public string Title;
    public double Value;
    public Color Color;

    public ChartSlice(string title, double value, Color color)
    {
        Title = title;
        Value = value;
        Color = color;
    }
}

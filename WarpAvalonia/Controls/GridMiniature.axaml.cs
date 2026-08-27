using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Warp.Tools;

namespace WarpAvalonia.Controls;

/// <summary>
/// Avalonia port of Warp.Controls.GridMiniature.
/// Isometric 3D grid visualization for CTF and Motion model grids.
/// </summary>
public partial class GridMiniature : UserControl
{
    public static readonly StyledProperty<int> PointsXProperty =
        AvaloniaProperty.Register<GridMiniature, int>(nameof(PointsX), defaultValue: 1);
    public static readonly StyledProperty<int> PointsYProperty =
        AvaloniaProperty.Register<GridMiniature, int>(nameof(PointsY), defaultValue: 1);
    public static readonly StyledProperty<int> PointsZProperty =
        AvaloniaProperty.Register<GridMiniature, int>(nameof(PointsZ), defaultValue: 1);
    public static readonly StyledProperty<int> DrawMaxZProperty =
        AvaloniaProperty.Register<GridMiniature, int>(nameof(DrawMaxZ), defaultValue: 15);

    public int PointsX { get => GetValue(PointsXProperty); set => SetValue(PointsXProperty, value); }
    public int PointsY { get => GetValue(PointsYProperty); set => SetValue(PointsYProperty, value); }
    public int PointsZ { get => GetValue(PointsZProperty); set => SetValue(PointsZProperty, value); }
    public int DrawMaxZ { get => GetValue(DrawMaxZProperty); set => SetValue(DrawMaxZProperty, value); }

    public GridMiniature()
    {
        InitializeComponent();
    }

    static GridMiniature()
    {
        PointsXProperty.Changed.AddClassHandler<GridMiniature>((s, _) => s.UpdateLines());
        PointsYProperty.Changed.AddClassHandler<GridMiniature>((s, _) => s.UpdateLines());
        PointsZProperty.Changed.AddClassHandler<GridMiniature>((s, _) => s.UpdateLines());
        BoundsProperty.Changed.AddClassHandler<GridMiniature>((s, _) => s.UpdateLines());
    }

    private IBrush GetForegroundBrush(double opacity = 1.0)
    {
        var fg = Foreground;
        if (fg is ISolidColorBrush scb)
            return new SolidColorBrush(scb.Color, opacity);
        return fg ?? Brushes.White;
    }

    public void UpdateLines()
    {
        MainCanvas.Children.Clear();

        double w = Bounds.Width;
        if (w <= 0) return;

        float StepX = PointsX > 1 ? 1f / (PointsX - 1) : 0f;
        float StepY = PointsY > 1 ? 1f / (PointsY - 1) : 0f;
        float2 Origin = new float2(PointsX > 1 ? 0 : 0.5f, PointsY > 1 ? 0 : 0.5f);

        float2 VecX = new float2((float)w / 2f, (float)w / 6f);
        float2 VecY = new float2(-(float)w / 2f, (float)w / 6f);
        float2 Offset = new float2((float)w / 2f, 0f);

        float ZLayerOffset = Math.Max(1.5f, Math.Min(5f, (float)DrawMaxZ * 1.5f / Math.Min(DrawMaxZ, PointsZ)));
        Height = w / 3 + Math.Min(DrawMaxZ, PointsZ) * ZLayerOffset + 6;

        // Draw X-direction lines
        for (int x = 0; x < PointsX; x++)
        {
            float2 From = new float2(Origin.X + x * StepX, Origin.Y);
            From = new float2(VecX.X * From.X + VecY.X * From.Y + Offset.X, VecX.Y * From.X + VecY.Y * From.Y + Offset.Y);
            float2 To = new float2(Origin.X + x * StepX, Origin.Y + (PointsY - 1) * StepY);
            To = new float2(VecX.X * To.X + VecY.X * To.Y + Offset.X, VecX.Y * To.X + VecY.Y * To.Y + Offset.Y);

            if (PointsY > 1)
            {
                MainCanvas.Children.Add(new Line
                {
                    StartPoint = new Point(From.X, From.Y),
                    EndPoint = new Point(To.X, To.Y),
                    Stroke = GetForegroundBrush()
                });
            }
            else
            {
                var circle = new Ellipse { Fill = GetForegroundBrush(), Width = 4, Height = 4 };
                Canvas.SetLeft(circle, From.X - 2);
                Canvas.SetTop(circle, From.Y - 2);
                MainCanvas.Children.Add(circle);
            }

            if (PointsZ > 1 && (x == PointsX - 1 || PointsY <= 1))
            {
                for (int z = 1; z < Math.Min(DrawMaxZ, PointsZ); z++)
                {
                    double opacity = 1.0 - (z / (double)Math.Min(DrawMaxZ, PointsZ) * 0.7 + 0.15);
                    if (PointsY > 1)
                    {
                        MainCanvas.Children.Add(new Line
                        {
                            StartPoint = new Point(From.X, From.Y + ZLayerOffset * z),
                            EndPoint = new Point(To.X, To.Y + ZLayerOffset * z),
                            Stroke = GetForegroundBrush(opacity)
                        });
                    }
                    else
                    {
                        var circle = new Ellipse { Fill = GetForegroundBrush(opacity), Width = 4, Height = 4 };
                        Canvas.SetLeft(circle, From.X - 2);
                        Canvas.SetTop(circle, From.Y - 2 + ZLayerOffset * z);
                        MainCanvas.Children.Add(circle);
                    }
                }
            }
        }

        // Draw Y-direction lines
        for (int y = 0; y < PointsY; y++)
        {
            float2 From = new float2(Origin.X, Origin.Y + y * StepY);
            From = new float2(VecX.X * From.X + VecY.X * From.Y + Offset.X, VecX.Y * From.X + VecY.Y * From.Y + Offset.Y);
            float2 To = new float2(Origin.X + (PointsX - 1) * StepX, Origin.Y + y * StepY);
            To = new float2(VecX.X * To.X + VecY.X * To.Y + Offset.X, VecX.Y * To.X + VecY.Y * To.Y + Offset.Y);

            if (PointsX > 1)
            {
                MainCanvas.Children.Add(new Line
                {
                    StartPoint = new Point(From.X, From.Y),
                    EndPoint = new Point(To.X, To.Y),
                    Stroke = GetForegroundBrush()
                });
            }
            else
            {
                var circle = new Ellipse { Fill = GetForegroundBrush(), Width = 4, Height = 4 };
                Canvas.SetLeft(circle, From.X - 2);
                Canvas.SetTop(circle, From.Y - 2);
                MainCanvas.Children.Add(circle);
            }

            if (PointsZ > 1 && (y == PointsY - 1 || PointsX <= 1))
            {
                for (int z = 1; z < Math.Min(DrawMaxZ, PointsZ); z++)
                {
                    double opacity = 1.0 - (z / (double)Math.Min(DrawMaxZ, PointsZ) * 0.7 + 0.15);
                    if (PointsX > 1)
                    {
                        MainCanvas.Children.Add(new Line
                        {
                            StartPoint = new Point(From.X, From.Y + ZLayerOffset * z),
                            EndPoint = new Point(To.X, To.Y + ZLayerOffset * z),
                            Stroke = GetForegroundBrush(opacity)
                        });
                    }
                    else
                    {
                        var circle = new Ellipse { Fill = GetForegroundBrush(opacity), Width = 4, Height = 4 };
                        Canvas.SetLeft(circle, From.X - 2);
                        Canvas.SetTop(circle, From.Y - 2 + ZLayerOffset * z);
                        MainCanvas.Children.Add(circle);
                    }
                }
            }
        }
    }
}

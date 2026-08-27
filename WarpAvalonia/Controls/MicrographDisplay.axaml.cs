using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace WarpAvalonia.Controls;

/// <summary>
/// Avalonia port of Warp.Controls.MicrographDisplay (2,608 lines in WPF).
/// Multi-layer interactive micrograph viewer with overlays for elevation,
/// motion tracks, particles, and mask painting.
/// </summary>
public partial class MicrographDisplay : UserControl
{
    #region Styled Properties

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<MicrographDisplay, double>(nameof(Zoom), 0.25);
    public static readonly StyledProperty<double> IntensityRangeProperty =
        AvaloniaProperty.Register<MicrographDisplay, double>(nameof(IntensityRange), 2.5);
    public static readonly StyledProperty<bool> ElevationShowProperty =
        AvaloniaProperty.Register<MicrographDisplay, bool>(nameof(ElevationShow), false);
    public static readonly StyledProperty<bool> TrackShowProperty =
        AvaloniaProperty.Register<MicrographDisplay, bool>(nameof(TrackShow), false);
    public static readonly StyledProperty<double> TrackScaleProperty =
        AvaloniaProperty.Register<MicrographDisplay, double>(nameof(TrackScale), 20);
    public static readonly StyledProperty<double> TrackGridXProperty =
        AvaloniaProperty.Register<MicrographDisplay, double>(nameof(TrackGridX), 8);
    public static readonly StyledProperty<double> TrackGridYProperty =
        AvaloniaProperty.Register<MicrographDisplay, double>(nameof(TrackGridY), 8);
    public static readonly StyledProperty<bool> ParticlesShowProperty =
        AvaloniaProperty.Register<MicrographDisplay, bool>(nameof(ParticlesShow), false);
    public static readonly StyledProperty<int> ParticlesDiameterProperty =
        AvaloniaProperty.Register<MicrographDisplay, int>(nameof(ParticlesDiameter), 100);
    public static readonly StyledProperty<double> ParticlesThresholdProperty =
        AvaloniaProperty.Register<MicrographDisplay, double>(nameof(ParticlesThreshold), 0);
    public static readonly StyledProperty<bool> MaskShowProperty =
        AvaloniaProperty.Register<MicrographDisplay, bool>(nameof(MaskShow), false);
    public static readonly StyledProperty<bool> BrushActiveProperty =
        AvaloniaProperty.Register<MicrographDisplay, bool>(nameof(BrushActive), false);
    public static readonly StyledProperty<int> BrushDiameterProperty =
        AvaloniaProperty.Register<MicrographDisplay, int>(nameof(BrushDiameter), 300);

    public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public double IntensityRange { get => GetValue(IntensityRangeProperty); set => SetValue(IntensityRangeProperty, value); }
    public bool ElevationShow { get => GetValue(ElevationShowProperty); set => SetValue(ElevationShowProperty, value); }
    public bool TrackShow { get => GetValue(TrackShowProperty); set => SetValue(TrackShowProperty, value); }
    public double TrackScale { get => GetValue(TrackScaleProperty); set => SetValue(TrackScaleProperty, value); }
    public double TrackGridX { get => GetValue(TrackGridXProperty); set => SetValue(TrackGridXProperty, value); }
    public double TrackGridY { get => GetValue(TrackGridYProperty); set => SetValue(TrackGridYProperty, value); }
    public bool ParticlesShow { get => GetValue(ParticlesShowProperty); set => SetValue(ParticlesShowProperty, value); }
    public int ParticlesDiameter { get => GetValue(ParticlesDiameterProperty); set => SetValue(ParticlesDiameterProperty, value); }
    public double ParticlesThreshold { get => GetValue(ParticlesThresholdProperty); set => SetValue(ParticlesThresholdProperty, value); }
    public bool MaskShow { get => GetValue(MaskShowProperty); set => SetValue(MaskShowProperty, value); }
    public bool BrushActive { get => GetValue(BrushActiveProperty); set => SetValue(BrushActiveProperty, value); }
    public int BrushDiameter { get => GetValue(BrushDiameterProperty); set => SetValue(BrushDiameterProperty, value); }

    #endregion

    #region State

    private float[]? _imageData;
    private int _imageWidth, _imageHeight;
    private byte[]? _maskData;
    private float _pixelSize = 1.5f; // Å/px

    private Point _panOffset = new(0, 0);
    private bool _isDragging;
    private Point _dragStart;
    private Point _dragOffsetStart;

    private bool _isPainting;
    private Point _lastPaintPos;

    // Synthetic test data
    private List<ParticlePos> _particles = new();
    private float[,]? _elevationGrid;
    private List<Point[]> _motionTracks = new();

    private SemaphoreSlim _updateSync = new(1, 1);

    #endregion

    public MicrographDisplay()
    {
        InitializeComponent();
    }

    static MicrographDisplay()
    {
        ZoomProperty.Changed.AddClassHandler<MicrographDisplay>((s, _) => s.UpdateAll());
        IntensityRangeProperty.Changed.AddClassHandler<MicrographDisplay>((s, _) => s.UpdateImage());
        ElevationShowProperty.Changed.AddClassHandler<MicrographDisplay>((s, _) => { s.ImageElevation.IsVisible = s.ElevationShow; });
        TrackShowProperty.Changed.AddClassHandler<MicrographDisplay>((s, _) => { s.CanvasTrack.IsVisible = s.TrackShow; s.UpdateTracks(); });
        TrackScaleProperty.Changed.AddClassHandler<MicrographDisplay>((s, _) => s.UpdateTracks());
        TrackGridXProperty.Changed.AddClassHandler<MicrographDisplay>((s, _) => s.UpdateTracks());
        TrackGridYProperty.Changed.AddClassHandler<MicrographDisplay>((s, _) => s.UpdateTracks());
        ParticlesShowProperty.Changed.AddClassHandler<MicrographDisplay>((s, _) => { s.CanvasParticles.IsVisible = s.ParticlesShow; s.UpdateParticles(); });
        ParticlesDiameterProperty.Changed.AddClassHandler<MicrographDisplay>((s, _) => s.UpdateParticles());
        ParticlesThresholdProperty.Changed.AddClassHandler<MicrographDisplay>((s, _) => s.UpdateParticles());
        MaskShowProperty.Changed.AddClassHandler<MicrographDisplay>((s, _) => { s.ImageMask.IsVisible = s.MaskShow; s.UpdateMask(); });
        BrushActiveProperty.Changed.AddClassHandler<MicrographDisplay>((s, _) => s.EllipseBrush.IsVisible = s.BrushActive);
        BrushDiameterProperty.Changed.AddClassHandler<MicrographDisplay>((s, _) => s.UpdateBrushEllipse());
        BoundsProperty.Changed.AddClassHandler<MicrographDisplay>((s, _) => s.UpdateAll());
    }

    #region Data Loading

    /// <summary>
    /// Load real image data from a Movie's average image.
    /// </summary>
    public void LoadImageData(float[] data, int width, int height, float pixelSize = 1.5f)
    {
        _imageWidth = width;
        _imageHeight = height;
        _pixelSize = pixelSize;
        _imageData = data;
        _particles.Clear();
        _motionTracks.Clear();
        _elevationGrid = null;
        _maskData = null;
        UpdateAll();
    }

    /// <summary>
    /// Load synthetic test data for all layers.
    /// Only called when launched with --demo flag.
    /// </summary>
    public void LoadTestData(int width = 512, int height = 512)
    {
        _imageWidth = width;
        _imageHeight = height;
        _pixelSize = 1.5f;

        // Generate synthetic micrograph (Gaussian blobs + noise)
        var rng = new Random(42);
        _imageData = new float[width * height];
        for (int i = 0; i < _imageData.Length; i++)
            _imageData[i] = (float)(rng.NextDouble() - 0.5) * 0.3f;

        // Add Gaussian particles
        for (int p = 0; p < 50; p++)
        {
            double cx = rng.NextDouble() * width;
            double cy = rng.NextDouble() * height;
            double sigma = 8 + rng.NextDouble() * 12;
            double amp = 0.5 + rng.NextDouble() * 0.5;
            for (int y = Math.Max(0, (int)(cy - sigma * 3)); y < Math.Min(height, (int)(cy + sigma * 3)); y++)
                for (int x = Math.Max(0, (int)(cx - sigma * 3)); x < Math.Min(width, (int)(cx + sigma * 3)); x++)
                {
                    double r2 = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    _imageData[y * width + x] += (float)(amp * Math.Exp(-r2 / (2 * sigma * sigma)));
                }

            _particles.Add(new ParticlePos(cx * _pixelSize, cy * _pixelSize, 0.5 + rng.NextDouble() * 0.5));
        }

        // Generate elevation grid (smooth defocus variation)
        _elevationGrid = new float[16, 16];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                _elevationGrid[y, x] = (float)(2.0 + Math.Sin(x * 0.5) * 0.5 + Math.Cos(y * 0.3) * 0.3);

        // Generate motion tracks
        _motionTracks.Clear();
        int gx = (int)TrackGridX, gy = (int)TrackGridY;
        for (int ty = 0; ty < gy; ty++)
        {
            for (int tx = 0; tx < gx; tx++)
            {
                double ox = (tx + 0.5) / gx * width;
                double oy = (ty + 0.5) / gy * height;
                var track = new Point[20];
                double dx = 0, dy = 0;
                for (int f = 0; f < 20; f++)
                {
                    dx += (rng.NextDouble() - 0.5) * 2;
                    dy += (rng.NextDouble() - 0.5) * 2;
                    track[f] = new Point(ox + dx, oy + dy);
                }
                _motionTracks.Add(track);
            }
        }

        // Generate mask (circular hole)
        _maskData = new byte[width * height];
        double mcx = width * 0.7, mcy = height * 0.3, mr = 40;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if ((x - mcx) * (x - mcx) + (y - mcy) * (y - mcy) < mr * mr)
                    _maskData[y * width + x] = 1;

        UpdateAll();
    }

    #endregion

    #region Rendering

    private void UpdateAll()
    {
        UpdateImage();
        UpdateElevation();
        UpdateTracks();
        UpdateParticles();
        UpdateMask();
    }

    private async void UpdateImage()
    {
        if (_imageData == null || _imageWidth <= 0) return;
        if (!await _updateSync.WaitAsync(0)) return;

        try
        {
            int w = _imageWidth, h = _imageHeight;
            float[] data = _imageData;
            double zoom = Zoom;
            double intensityRange = IntensityRange;

            var bitmap = await Task.Run(() =>
            {
                // Compute display dimensions
                int dw = Math.Max(1, (int)(w * zoom));
                int dh = Math.Max(1, (int)(h * zoom));

                // Compute statistics for intensity windowing
                double sum = 0, sum2 = 0;
                for (int i = 0; i < data.Length; i++) { sum += data[i]; sum2 += data[i] * data[i]; }
                double mean = sum / data.Length;
                double std = Math.Sqrt(sum2 / data.Length - mean * mean);
                float vmin = (float)(mean - intensityRange * std);
                float vmax = (float)(mean + intensityRange * std);
                float range = vmax - vmin;
                if (range <= 0) range = 1;

                // Render to BGRA32
                byte[] bgra = new byte[dw * dh * 4];
                for (int dy = 0; dy < dh; dy++)
                {
                    int sy = Math.Clamp((int)(dy / zoom), 0, h - 1);
                    for (int dx = 0; dx < dw; dx++)
                    {
                        int sx = Math.Clamp((int)(dx / zoom), 0, w - 1);
                        float v = (data[sy * w + sx] - vmin) / range;
                        byte b = (byte)Math.Clamp(v * 255, 0, 255);
                        int idx = (dy * dw + dx) * 4;
                        bgra[idx] = b; bgra[idx + 1] = b; bgra[idx + 2] = b; bgra[idx + 3] = 255;
                    }
                }

                var bmp = new WriteableBitmap(new PixelSize(dw, dh), new Vector(96, 96));
                using (var fb = bmp.Lock())
                    Marshal.Copy(bgra, 0, fb.Address, bgra.Length);
                return bmp;
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ImageDisplay.Source = bitmap;
                ImageDisplay.RenderTransform = new TranslateTransform(_panOffset.X, _panOffset.Y);
            });
        }
        finally { _updateSync.Release(); }
    }

    private void UpdateElevation()
    {
        if (_elevationGrid == null || _imageWidth <= 0) return;

        int ew = _elevationGrid.GetLength(1), eh = _elevationGrid.GetLength(0);
        int dw = (int)(_imageWidth * Zoom), dh = (int)(_imageHeight * Zoom);
        if (dw <= 0 || dh <= 0) return;

        // Find range
        float emin = float.MaxValue, emax = float.MinValue;
        for (int y = 0; y < eh; y++)
            for (int x = 0; x < ew; x++)
            {
                emin = Math.Min(emin, _elevationGrid[y, x]);
                emax = Math.Max(emax, _elevationGrid[y, x]);
            }
        float erange = emax - emin;
        if (erange <= 0) erange = 1;

        byte[] bgra = new byte[dw * dh * 4];
        for (int dy = 0; dy < dh; dy++)
        {
            float fy = (float)dy / dh * (eh - 1);
            int iy = Math.Clamp((int)fy, 0, eh - 2);
            float ty = fy - iy;
            for (int dx = 0; dx < dw; dx++)
            {
                float fx = (float)dx / dw * (ew - 1);
                int ix = Math.Clamp((int)fx, 0, ew - 2);
                float txv = fx - ix;

                // Bilinear interpolation
                float v00 = _elevationGrid[iy, ix], v10 = _elevationGrid[iy, ix + 1];
                float v01 = _elevationGrid[iy + 1, ix], v11 = _elevationGrid[iy + 1, ix + 1];
                float v = v00 * (1 - txv) * (1 - ty) + v10 * txv * (1 - ty) + v01 * (1 - txv) * ty + v11 * txv * ty;
                float t = (v - emin) / erange; // 0=low defocus, 1=high

                // Pink (DeepPink) → Blue (DeepSkyBlue)
                byte r = (byte)(255 * (1 - t) * 0.9 + 0 * t);
                byte g = (byte)(20 * (1 - t) + 191 * t);
                byte b = (byte)(147 * (1 - t) + 255 * t);

                int idx = (dy * dw + dx) * 4;
                bgra[idx] = b; bgra[idx + 1] = g; bgra[idx + 2] = r; bgra[idx + 3] = 180;
            }
        }

        var bmp = new WriteableBitmap(new PixelSize(dw, dh), new Vector(96, 96));
        using (var fb = bmp.Lock())
            Marshal.Copy(bgra, 0, fb.Address, bgra.Length);
        ImageElevation.Source = bmp;
        ImageElevation.RenderTransform = new TranslateTransform(_panOffset.X, _panOffset.Y);
    }

    private void UpdateTracks()
    {
        CanvasTrack.Children.Clear();
        if (!TrackShow || _motionTracks.Count == 0) return;

        double scale = TrackScale;
        double zoom = Zoom;

        foreach (var track in _motionTracks)
        {
            var polyline = new Polyline
            {
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                Opacity = 0.7,
                IsHitTestVisible = false,
                Points = new Avalonia.Points()
            };

            double baseX = track[0].X * zoom + _panOffset.X;
            double baseY = track[0].Y * zoom + _panOffset.Y;

            foreach (var pt in track)
            {
                double dx = (pt.X - track[0].X) * scale;
                double dy = (pt.Y - track[0].Y) * scale;
                ((Avalonia.Points)polyline.Points).Add(new Point(baseX + dx, baseY + dy));
            }

            CanvasTrack.Children.Add(polyline);
        }
    }

    private void UpdateParticles()
    {
        CanvasParticles.Children.Clear();
        if (!ParticlesShow || _particles.Count == 0) return;

        double zoom = Zoom;
        double diam = ParticlesDiameter / _pixelSize * zoom; // diameter in display pixels

        foreach (var p in _particles)
        {
            if (p.FOM < ParticlesThreshold) continue;

            double cx = p.X / _pixelSize * zoom + _panOffset.X;
            double cy = p.Y / _pixelSize * zoom + _panOffset.Y;

            var circle = new Ellipse
            {
                Width = diam,
                Height = diam,
                Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 0)),
                StrokeThickness = 1.5,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(circle, cx - diam / 2);
            Canvas.SetTop(circle, cy - diam / 2);
            CanvasParticles.Children.Add(circle);
        }
    }

    private void UpdateMask()
    {
        if (_maskData == null || _imageWidth <= 0 || !MaskShow) return;

        int dw = (int)(_imageWidth * Zoom), dh = (int)(_imageHeight * Zoom);
        if (dw <= 0 || dh <= 0) return;

        byte[] bgra = new byte[dw * dh * 4];
        for (int dy = 0; dy < dh; dy++)
        {
            int sy = Math.Clamp((int)(dy / Zoom), 0, _imageHeight - 1);
            for (int dx = 0; dx < dw; dx++)
            {
                int sx = Math.Clamp((int)(dx / Zoom), 0, _imageWidth - 1);
                if (_maskData[sy * _imageWidth + sx] > 0)
                {
                    int idx = (dy * dw + dx) * 4;
                    bgra[idx] = 0;     // B
                    bgra[idx + 1] = 128; // G
                    bgra[idx + 2] = 255; // R (orange)
                    bgra[idx + 3] = 120; // A
                }
            }
        }

        var bmp = new WriteableBitmap(new PixelSize(dw, dh), new Vector(96, 96));
        using (var fb = bmp.Lock())
            Marshal.Copy(bgra, 0, fb.Address, bgra.Length);
        ImageMask.Source = bmp;
        ImageMask.RenderTransform = new TranslateTransform(_panOffset.X, _panOffset.Y);
    }

    private void UpdateBrushEllipse()
    {
        double d = BrushDiameter / _pixelSize * Zoom;
        EllipseBrush.Width = d;
        EllipseBrush.Height = d;
    }

    #endregion

    #region Mouse Interaction

    private void Display_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (BrushActive)
        {
            BrushDiameter = Math.Clamp(BrushDiameter + (int)(Math.Sign(e.Delta.Y) * 10), 10, 2000);
            return;
        }

        double oldZoom = Zoom;
        Zoom = Math.Clamp(Zoom + Math.Sign(e.Delta.Y) * 0.05, 0.05, 8);

        // Keep point under cursor stable
        var pos = e.GetPosition(GridDisplay);
        double factor = Zoom / oldZoom;
        _panOffset = new Point(
            pos.X - (pos.X - _panOffset.X) * factor,
            pos.Y - (pos.Y - _panOffset.Y) * factor);

        e.Handled = true;
    }

    private void Display_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(GridDisplay);

        if (BrushActive && MaskShow)
        {
            _isPainting = true;
            _lastPaintPos = pos;
            PaintAt(pos, e.GetCurrentPoint(GridDisplay).Properties.IsRightButtonPressed);
            return;
        }

        _isDragging = true;
        _dragStart = pos;
        _dragOffsetStart = _panOffset;
        e.Handled = true;
    }

    private void Display_PointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(GridDisplay);

        if (BrushActive)
        {
            double d = BrushDiameter / _pixelSize * Zoom;
            Canvas.SetLeft(EllipseBrush, pos.X - d / 2);
            Canvas.SetTop(EllipseBrush, pos.Y - d / 2);
        }

        if (_isPainting && MaskShow)
        {
            PaintAt(pos, e.GetCurrentPoint(GridDisplay).Properties.IsRightButtonPressed);
            _lastPaintPos = pos;
            return;
        }

        if (_isDragging)
        {
            _panOffset = new Point(
                _dragOffsetStart.X + pos.X - _dragStart.X,
                _dragOffsetStart.Y + pos.Y - _dragStart.Y);
            PositionVisuals();
        }
    }

    private void Display_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
        _isPainting = false;
    }

    private void PositionVisuals()
    {
        var transform = new TranslateTransform(_panOffset.X, _panOffset.Y);
        ImageDisplay.RenderTransform = transform;
        ImageElevation.RenderTransform = transform;
        ImageMask.RenderTransform = transform;
        UpdateTracks();
        UpdateParticles();
    }

    #endregion

    #region Mask Painting

    private void PaintAt(Point displayPos, bool erase)
    {
        if (_maskData == null) return;

        // Convert display coordinates to image coordinates
        int imgX = (int)((displayPos.X - _panOffset.X) / Zoom);
        int imgY = (int)((displayPos.Y - _panOffset.Y) / Zoom);
        int radius = (int)(BrushDiameter / _pixelSize / 2);

        for (int dy = -radius; dy <= radius; dy++)
        {
            int py = imgY + dy;
            if (py < 0 || py >= _imageHeight) continue;
            for (int dx = -radius; dx <= radius; dx++)
            {
                int px = imgX + dx;
                if (px < 0 || px >= _imageWidth) continue;
                if (dx * dx + dy * dy <= radius * radius)
                    _maskData[py * _imageWidth + px] = erase ? (byte)0 : (byte)1;
            }
        }
        UpdateMask();
    }

    #endregion
}

public struct ParticlePos
{
    public double X, Y, FOM;
    public ParticlePos(double x, double y, double fom) { X = x; Y = y; FOM = fom; }
}

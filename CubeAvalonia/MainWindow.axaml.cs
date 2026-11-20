using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Warp;
using Warp.Headers;
using Warp.Tools;
using WarpImage = Warp.Image;

namespace CubeAvalonia;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private float[]? VolumeData;
    private int DimX, DimY, DimZ;
    private double VolumeMean, VolumeStd;

    private int _planeX, _planeY, _planeZ;
    public int PlaneX { get => _planeX; set { _planeX = Math.Clamp(value, 0, Math.Max(0, DimX - 1)); OnPropChanged(); UpdateSlices(); } }
    public int PlaneY { get => _planeY; set { _planeY = Math.Clamp(value, 0, Math.Max(0, DimY - 1)); OnPropChanged(); UpdateSlices(); } }
    public int PlaneZ { get => _planeZ; set { _planeZ = Math.Clamp(value, 0, Math.Max(0, DimZ - 1)); OnPropChanged(); UpdateSlices(); } }

    private int _inputAverageSlices = 1;
    public int InputAverageSlices { get => _inputAverageSlices; set { _inputAverageSlices = value; OnPropChanged(); UpdateSlices(); } }

    // Defaults as Cube/Options.cs (WPF)
    private double _displayIntensityMin = -2;
    public double DisplayIntensityMin { get => _displayIntensityMin; set { _displayIntensityMin = value; OnPropChanged(); UpdateSlices(); } }
    private double _displayIntensityMax = 2;
    public double DisplayIntensityMax { get => _displayIntensityMax; set { _displayIntensityMax = value; OnPropChanged(); UpdateSlices(); } }

    private int _boxSize = 16;
    public int BoxSize { get => _boxSize; set { _boxSize = value; OnPropChanged(); } }
    private double _particleScoreMin = 0;
    public double ParticleScoreMin { get => _particleScoreMin; set { _particleScoreMin = value; OnPropChanged(); UpdateParticleOverlays(); } }
    private double _particleScoreMax = 1;
    public double ParticleScoreMax { get => _particleScoreMax; set { _particleScoreMax = value; OnPropChanged(); UpdateParticleOverlays(); } }

    private List<float3> Particles = new();
    private List<double> ParticleScores = new();
    private int SelectedParticle = -1;

    public new event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        if (Program.DemoMode)
            LoadDemoVolume();
    }

    private async void ButtonTomogramPath_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Tomogram MRC",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("MRC") { Patterns = new[] { "*.mrc", "*.rec" } } }
        });
        if (files.Count == 0) return;

        string path = files[0].Path.LocalPath;
        ButtonTomogramPath.Content = System.IO.Path.GetFileName(path);

        try
        {
            MapHeader header = MapHeader.ReadFromFile(path);
            WarpImage vol = WarpImage.FromFile(path);
            VolumeData = vol.GetHostContinuousCopy();
            DimX = header.Dimensions.X;
            DimY = header.Dimensions.Y;
            DimZ = header.Dimensions.Z;
            vol.Dispose();

            ComputeStatistics();
            PlaneX = DimX / 2; PlaneY = DimY / 2; PlaneZ = DimZ / 2;
        }
        catch (Exception ex) { Title = $"Error: {ex.Message}"; }
    }

    private void LoadDemoVolume()
    {
        DimX = DimY = DimZ = 64;
        VolumeData = new float[64 * 64 * 64];
        var rng = new Random(42);

        for (int z = 0; z < 64; z++)
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float r = MathF.Sqrt((x - 32) * (x - 32) + (y - 32) * (y - 32) + (z - 32) * (z - 32));
                    VolumeData[z * 64 * 64 + y * 64 + x] = 1f / (1f + MathF.Exp((r - 20) * 2)) + (float)rng.NextDouble() * 0.1f;
                }

        for (int i = 0; i < 15; i++)
        {
            Particles.Add(new float3(10 + (float)rng.NextDouble() * 44, 10 + (float)rng.NextDouble() * 44, 10 + (float)rng.NextDouble() * 44));
            ParticleScores.Add(rng.NextDouble() * 2);
        }
        TextNParticles.Text = $"{Particles.Count} particles, ";

        ComputeStatistics();
        PlaneX = 32; PlaneY = 32; PlaneZ = 32;
    }

    private void ComputeStatistics()
    {
        if (VolumeData == null) return;
        double sum = 0, sum2 = 0;
        for (int i = 0; i < VolumeData.Length; i++) { sum += VolumeData[i]; sum2 += VolumeData[i] * (double)VolumeData[i]; }
        VolumeMean = sum / VolumeData.Length;
        VolumeStd = Math.Sqrt(sum2 / VolumeData.Length - VolumeMean * VolumeMean);
    }

    private void UpdateSlices()
    {
        if (VolumeData == null) return;
        ImageXY.Source = RenderSliceXY(PlaneZ);
        ImageZY.Source = RenderSliceZY(PlaneX);
        ImageXZ.Source = RenderSliceXZ(PlaneY);
        UpdateParticleOverlays();
    }

    private WriteableBitmap RenderSliceXY(int z)
    {
        z = Math.Clamp(z, 0, DimZ - 1);
        return RenderSlice(DimX, DimY, (x, y) =>
        {
            if (InputAverageSlices <= 1) return VolumeData![z * DimX * DimY + y * DimX + x];
            float sum = 0; int c = 0;
            for (int dz = -InputAverageSlices / 2; dz <= InputAverageSlices / 2; dz++)
            { int sz = Math.Clamp(z + dz, 0, DimZ - 1); sum += VolumeData![sz * DimX * DimY + y * DimX + x]; c++; }
            return sum / c;
        });
    }

    private WriteableBitmap RenderSliceZY(int x) =>
        RenderSlice(DimZ, DimY, (z, y) => VolumeData![Math.Clamp(z, 0, DimZ - 1) * DimX * DimY + y * DimX + Math.Clamp(x, 0, DimX - 1)]);

    private WriteableBitmap RenderSliceXZ(int y) =>
        RenderSlice(DimX, DimZ, (x, z) => VolumeData![Math.Clamp(z, 0, DimZ - 1) * DimX * DimY + Math.Clamp(y, 0, DimY - 1) * DimX + x]);

    private WriteableBitmap RenderSlice(int w, int h, Func<int, int, float> getValue)
    {
        float vmin = (float)(VolumeMean + DisplayIntensityMin * VolumeStd);
        float vmax = (float)(VolumeMean + DisplayIntensityMax * VolumeStd);
        float range = vmax - vmin; if (range <= 0) range = 1;

        byte[] bgra = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte b = (byte)Math.Clamp((getValue(x, y) - vmin) / range * 255, 0, 255);
                int idx = (y * w + x) * 4;
                bgra[idx] = b; bgra[idx + 1] = b; bgra[idx + 2] = b; bgra[idx + 3] = 255;
            }

        var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96));
        using (var fb = bmp.Lock()) Marshal.Copy(bgra, 0, fb.Address, bgra.Length);
        return bmp;
    }

    private void UpdateParticleOverlays()
    {
        RenderParticles(CanvasParticlesXY, p => new Point(p.X, p.Y), p => Math.Abs(p.Z - PlaneZ) < BoxSize / 2.0);
        RenderParticles(CanvasParticlesZY, p => new Point(p.Z, p.Y), p => Math.Abs(p.X - PlaneX) < BoxSize / 2.0);
        RenderParticles(CanvasParticlesXZ, p => new Point(p.X, p.Z), p => Math.Abs(p.Y - PlaneY) < BoxSize / 2.0);
    }

    private void RenderParticles(Canvas canvas, Func<float3, Point> project, Func<float3, bool> inSlice)
    {
        canvas.Children.Clear();
        for (int i = 0; i < Particles.Count; i++)
        {
            if (i < ParticleScores.Count && (ParticleScores[i] < ParticleScoreMin || ParticleScores[i] > ParticleScoreMax)) continue;
            if (!inSlice(Particles[i])) continue;
            var pos = project(Particles[i]);
            var c = new Ellipse { Width = 12, Height = 12, Stroke = i == SelectedParticle ? Brushes.Red : Brushes.Yellow,
                StrokeThickness = i == SelectedParticle ? 2 : 1, Fill = Brushes.Transparent, IsHitTestVisible = false };
            Canvas.SetLeft(c, pos.X - 6); Canvas.SetTop(c, pos.Y - 6);
            canvas.Children.Add(c);
        }
    }

    private void ImageXY_OnMouseWheel(object? s, PointerWheelEventArgs e) { PlaneZ -= (int)Math.Sign(e.Delta.Y); e.Handled = true; }
    private void ImageZY_OnMouseWheel(object? s, PointerWheelEventArgs e) { PlaneX -= (int)Math.Sign(e.Delta.Y); e.Handled = true; }
    private void ImageXZ_OnMouseWheel(object? s, PointerWheelEventArgs e) { PlaneY -= (int)Math.Sign(e.Delta.Y); e.Handled = true; }
    private void Image_OnPointerPressed(object? s, PointerPressedEventArgs e) { }

    private void ImageXY_OnPointerMoved(object? s, PointerEventArgs e)
    { if (!e.GetCurrentPoint(ImageXY).Properties.IsLeftButtonPressed) return; var p = e.GetPosition(ImageXY);
      PlaneX = (int)(p.X / ImageXY.Bounds.Width * DimX); PlaneY = (int)(p.Y / ImageXY.Bounds.Height * DimY); }
    private void ImageZY_OnPointerMoved(object? s, PointerEventArgs e)
    { if (!e.GetCurrentPoint(ImageZY).Properties.IsLeftButtonPressed) return; var p = e.GetPosition(ImageZY);
      PlaneZ = (int)(p.X / ImageZY.Bounds.Width * DimZ); PlaneY = (int)(p.Y / ImageZY.Bounds.Height * DimY); }
    private void ImageXZ_OnPointerMoved(object? s, PointerEventArgs e)
    { if (!e.GetCurrentPoint(ImageXZ).Properties.IsLeftButtonPressed) return; var p = e.GetPosition(ImageXZ);
      PlaneX = (int)(p.X / ImageXZ.Bounds.Width * DimX); PlaneZ = (int)(p.Y / ImageXZ.Bounds.Height * DimZ); }

    private void MainWindow_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && SelectedParticle >= 0 && SelectedParticle < Particles.Count)
        { Particles.RemoveAt(SelectedParticle); if (SelectedParticle < ParticleScores.Count) ParticleScores.RemoveAt(SelectedParticle);
          SelectedParticle = Math.Min(SelectedParticle, Particles.Count - 1); TextNParticles.Text = $"{Particles.Count} particles, "; UpdateParticleOverlays(); }
        else if (e.Key == Key.Left && SelectedParticle > 0) { SelectedParticle--; GoToParticle(SelectedParticle); }
        else if (e.Key == Key.Right && SelectedParticle < Particles.Count - 1) { SelectedParticle++; GoToParticle(SelectedParticle); }
    }

    private void GoToParticle(int i)
    { if (i < 0 || i >= Particles.Count) return; PlaneX = (int)Particles[i].X; PlaneY = (int)Particles[i].Y; PlaneZ = (int)Particles[i].Z; }

    private async void ButtonPointsImport_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = "Import Coordinates", AllowMultiple = false, FileTypeFilter = new[] { new FilePickerFileType("STAR") { Patterns = new[] { "*.star" } } } });
        if (files.Count == 0) return;
        try
        {
            Star table = new Star(files[0].Path.LocalPath); Particles.Clear(); ParticleScores.Clear();
            float[] x = table.GetColumn("rlnCoordinateX").Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            float[] y = table.GetColumn("rlnCoordinateY").Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            float[] z = table.GetColumn("rlnCoordinateZ").Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            for (int i = 0; i < x.Length; i++) Particles.Add(new float3(x[i], y[i], z[i]));
            if (table.HasColumn("rlnAutopickFigureOfMerit"))
                ParticleScores.AddRange(table.GetColumn("rlnAutopickFigureOfMerit").Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)));
            TextNParticles.Text = $"{Particles.Count} particles, "; UpdateParticleOverlays();
        }
        catch (Exception ex) { Title = $"Import error: {ex.Message}"; }
    }

    private async void ButtonPointsExport_OnClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = "Export Coordinates", DefaultExtension = "star", FileTypeChoices = new[] { new FilePickerFileType("STAR") { Patterns = new[] { "*.star" } } } });
        if (file == null) return;
        Star table = new Star(new[] { "rlnCoordinateX", "rlnCoordinateY", "rlnCoordinateZ" });
        foreach (var p in Particles) table.AddRow(new[] { p.X.ToString("F2"), p.Y.ToString("F2"), p.Z.ToString("F2") });
        table.Save(file.Path.LocalPath);
    }
}

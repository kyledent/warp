using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Warp.Tools;

namespace WarpAvalonia.Controls;

/// <summary>
/// Avalonia port of WarpControls SingleSliceRenderer.
/// Renders a 2D slice from a 3D float array as a BGRA32 bitmap.
/// This validates the core WPF→Avalonia bitmap rendering pipeline.
/// </summary>
public partial class SliceRenderer : UserControl
{
    // The 3D volume data (flat float array, X-major order)
    private float[]? _volumeData;
    private int _dimX, _dimY, _dimZ;

    // Current slice position
    public static readonly StyledProperty<int> SliceZProperty =
        AvaloniaProperty.Register<SliceRenderer, int>(nameof(SliceZ), defaultValue: 0);

    public static readonly StyledProperty<float> ThresholdLowerProperty =
        AvaloniaProperty.Register<SliceRenderer, float>(nameof(ThresholdLower), defaultValue: 0f);

    public static readonly StyledProperty<float> ThresholdUpperProperty =
        AvaloniaProperty.Register<SliceRenderer, float>(nameof(ThresholdUpper), defaultValue: 1f);

    public int SliceZ
    {
        get => GetValue(SliceZProperty);
        set => SetValue(SliceZProperty, value);
    }

    public float ThresholdLower
    {
        get => GetValue(ThresholdLowerProperty);
        set => SetValue(ThresholdLowerProperty, value);
    }

    public float ThresholdUpper
    {
        get => GetValue(ThresholdUpperProperty);
        set => SetValue(ThresholdUpperProperty, value);
    }

    public SliceRenderer()
    {
        InitializeComponent();
    }

    static SliceRenderer()
    {
        SliceZProperty.Changed.AddClassHandler<SliceRenderer>((s, _) => s.UpdateRendering());
        ThresholdLowerProperty.Changed.AddClassHandler<SliceRenderer>((s, _) => s.UpdateRendering());
        ThresholdUpperProperty.Changed.AddClassHandler<SliceRenderer>((s, _) => s.UpdateRendering());
    }

    /// <summary>
    /// Set volume data from a flat float array (X×Y×Z, X-major).
    /// This is the same data format WarpLib's Image class uses.
    /// </summary>
    public void SetVolumeData(float[] data, int dimX, int dimY, int dimZ)
    {
        _volumeData = data;
        _dimX = dimX;
        _dimY = dimY;
        _dimZ = dimZ;

        // Auto-threshold
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] < min) min = data[i];
            if (data[i] > max) max = data[i];
        }
        ThresholdLower = min;
        ThresholdUpper = max;

        SliceZ = dimZ / 2;
        UpdateRendering();
    }

    /// <summary>
    /// Core rendering method: extract Z-slice, convert to BGRA32 bitmap, display.
    /// This is the direct equivalent of WPF SingleSliceRenderer.DataToBitmap().
    /// </summary>
    public void UpdateRendering()
    {
        if (_volumeData == null || _dimX <= 0 || _dimY <= 0)
            return;

        int z = Math.Clamp(SliceZ, 0, _dimZ - 1);
        int sliceOffset = z * _dimX * _dimY;

        float min = ThresholdLower;
        float range = ThresholdUpper - ThresholdLower;
        if (range == 0) range = 1e-5f;

        // Convert float slice data to BGRA32 byte array
        // This is the exact same algorithm as WPF SingleSliceRenderer.DataToBitmap()
        byte[] bgra = new byte[_dimX * _dimY * 4];

        for (int y = 0; y < _dimY; y++)
        {
            for (int x = 0; x < _dimX; x++)
            {
                float v = (_volumeData[sliceOffset + y * _dimX + x] - min) / range;
                v = Math.Clamp(v, 0f, 1f);
                byte intensity = (byte)(v * 255);

                // BGRA order (same as WPF PixelFormats.Bgra32)
                int pixelIndex = ((_dimY - 1 - y) * _dimX + x) * 4;
                bgra[pixelIndex + 0] = intensity; // B
                bgra[pixelIndex + 1] = intensity; // G
                bgra[pixelIndex + 2] = intensity; // R
                bgra[pixelIndex + 3] = 255;       // A
            }
        }

        // Create Avalonia WriteableBitmap from raw BGRA32 bytes
        // This replaces WPF's BitmapSource.Create()
        var bitmap = new WriteableBitmap(
            new PixelSize(_dimX, _dimY),
            new Vector(96, 96));

        using (var fb = bitmap.Lock())
        {
            Marshal.Copy(bgra, 0, fb.Address, bgra.Length);
        }

        ImageViewer.Source = bitmap;
    }

    /// <summary>
    /// Generate synthetic 3D test volume (sphere + noise) for PoC validation.
    /// No GPU or NativeAcceleration required.
    /// </summary>
    public static float[] GenerateTestVolume(int dim, out int dimX, out int dimY, out int dimZ)
    {
        dimX = dim;
        dimY = dim;
        dimZ = dim;

        float[] data = new float[dim * dim * dim];
        float center = dim / 2f;
        float radius = dim / 3f;
        var rng = new Random(42);

        for (int z = 0; z < dim; z++)
        {
            for (int y = 0; y < dim; y++)
            {
                for (int x = 0; x < dim; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dz = z - center;
                    float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

                    // Sphere with soft edge + Gaussian noise
                    float sphere = 1f / (1f + MathF.Exp((dist - radius) * 2f));
                    float noise = (float)rng.NextDouble() * 0.1f;

                    data[z * dim * dim + y * dim + x] = sphere + noise;
                }
            }
        }

        return data;
    }
}

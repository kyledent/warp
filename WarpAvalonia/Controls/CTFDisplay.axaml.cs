using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace WarpAvalonia.Controls;

/// <summary>
/// Avalonia port of Warp.Controls.CTFDisplay.
/// Shows 2D simulated CTF, 2D power spectrum, and 1D CTF curves.
/// For the PoC, generates synthetic data to validate the rendering pipeline.
/// In production, this would be driven by Movie/TiltSeries CTF data from WarpLib.
/// </summary>
public partial class CTFDisplay : UserControl
{
    public CTFDisplay()
    {
        InitializeComponent();
    }

    static CTFDisplay()
    {
        BoundsProperty.Changed.AddClassHandler<CTFDisplay>((s, _) => s.Render1D());
    }

    /// <summary>
    /// Load synthetic test data to validate rendering.
    /// </summary>
    public void LoadTestData(double defocus = 3.0, double cs = 2.7, double voltage = 300, double pixelSize = 1.5)
    {
        TextDefocus.Text = $"{defocus:F3} um";
        TextAstigmatism.Text = $"0.050 um, 45.0 deg";
        TextPhase.Text = "0.00 pi";
        TextResolution.Text = "4.2 A";

        // Generate synthetic 2D CTF (simulated)
        int dim = 256;
        Generate2DCTF(dim, defocus, cs, voltage, pixelSize);

        // Generate synthetic 2D power spectrum
        GeneratePS2D(dim, defocus, cs, voltage, pixelSize);

        // Generate 1D CTF plot
        Render1D(defocus, cs, voltage, pixelSize);
    }

    private void Generate2DCTF(int dim, double defocus, double cs, double voltage, double pixelSize)
    {
        byte[] bgra = new byte[dim * dim * 4];
        double lambda = GetLambda(voltage);
        int half = dim / 2;

        for (int y = 0; y < dim; y++)
        {
            for (int x = 0; x < dim; x++)
            {
                double fx = (x - half) / (double)dim / pixelSize;
                double fy = (y - half) / (double)dim / pixelSize;
                double s2 = fx * fx + fy * fy;
                double s = Math.Sqrt(s2);

                double chi = Math.PI * lambda * s2 * (defocus * 1e4 - 0.5 * cs * 1e7 * lambda * lambda * s2);
                double ctf = -Math.Sin(chi);
                byte val = (byte)Math.Clamp((ctf * 0.5 + 0.5) * 255, 0, 255);

                int idx = (y * dim + x) * 4;
                bgra[idx + 0] = val; // B
                bgra[idx + 1] = val; // G
                bgra[idx + 2] = val; // R
                bgra[idx + 3] = 255; // A
            }
        }

        var bitmap = new WriteableBitmap(new PixelSize(dim, dim), new Vector(96, 96));
        using (var fb = bitmap.Lock())
            Marshal.Copy(bgra, 0, fb.Address, bgra.Length);
        ImageSimulated2D.Source = bitmap;
    }

    private void GeneratePS2D(int dim, double defocus, double cs, double voltage, double pixelSize)
    {
        byte[] bgra = new byte[dim * dim * 4];
        double lambda = GetLambda(voltage);
        int half = dim / 2;
        var rng = new Random(123);

        for (int y = 0; y < dim; y++)
        {
            for (int x = 0; x < dim; x++)
            {
                double fx = (x - half) / (double)dim / pixelSize;
                double fy = (y - half) / (double)dim / pixelSize;
                double s2 = fx * fx + fy * fy;

                double chi = Math.PI * lambda * s2 * (defocus * 1e4 - 0.5 * cs * 1e7 * lambda * lambda * s2);
                double ctf2 = Math.Sin(chi) * Math.Sin(chi);

                // Simulate PS = CTF^2 * envelope + noise
                double r = Math.Sqrt(s2) * dim * pixelSize;
                double envelope = Math.Exp(-r * r * 0.001);
                double noise = rng.NextDouble() * 0.3;
                double val = ctf2 * envelope + noise;
                byte bval = (byte)Math.Clamp(val * 200, 0, 255);

                int idx = (y * dim + x) * 4;
                bgra[idx + 0] = bval;
                bgra[idx + 1] = bval;
                bgra[idx + 2] = bval;
                bgra[idx + 3] = 255;
            }
        }

        var bitmap = new WriteableBitmap(new PixelSize(dim, dim), new Vector(96, 96));
        using (var fb = bitmap.Lock())
            Marshal.Copy(bgra, 0, fb.Address, bgra.Length);
        ImagePS2D.Source = bitmap;
    }

    private void Render1D(double defocus = 3.0, double cs = 2.7, double voltage = 300, double pixelSize = 1.5)
    {
        double w = CanvasPlot1D.Bounds.Width;
        double h = CanvasPlot1D.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        CanvasPlot1D.Children.Clear();

        double lambda = GetLambda(voltage);
        int nPoints = (int)w;
        var rng = new Random(456);

        // Background
        var bg = new Avalonia.Controls.Shapes.Rectangle { Width = w, Height = h, Fill = new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)) };
        CanvasPlot1D.Children.Add(bg);

        // Generate 1D CTF curves
        double maxFreq = 1.0 / (2.0 * pixelSize); // Nyquist
        var simPoints = new Point[nPoints];
        var expPoints = new Point[nPoints];
        var qualPoints = new Point[nPoints];

        for (int i = 0; i < nPoints; i++)
        {
            double frac = i / (double)(nPoints - 1);
            double s = frac * maxFreq;
            double s2 = s * s;

            double chi = Math.PI * lambda * s2 * (defocus * 1e4 - 0.5 * cs * 1e7 * lambda * lambda * s2);
            double ctfSim = -Math.Sin(chi);
            double ctfExp = ctfSim + (rng.NextDouble() - 0.5) * 0.3;
            double quality = Math.Max(0, 1.0 - frac * 1.5 + (rng.NextDouble() - 0.5) * 0.2);

            double yMargin = h * 0.1;
            simPoints[i] = new Point(i, yMargin + (1 - (ctfSim * 0.4 + 0.5)) * (h - 2 * yMargin));
            expPoints[i] = new Point(i, yMargin + (1 - (ctfExp * 0.4 + 0.5)) * (h - 2 * yMargin));
            qualPoints[i] = new Point(i, h - quality * h * 0.3);
        }

        // Draw lines
        DrawPolyline(CanvasPlot1D, expPoints, Color.Parse("#41b1e1"), 1); // DeepSkyBlue - experimental
        DrawPolyline(CanvasPlot1D, simPoints, Color.Parse("#FF1493"), 1); // DeepPink - simulated
        DrawPolyline(CanvasPlot1D, qualPoints, Color.Parse("#c0c0c0"), 1); // LightGray - quality

        // Legend
        AddLegendItem(CanvasPlot1D, 4, 4, "#FF1493", "Fitted");
        AddLegendItem(CanvasPlot1D, 4, 16, "#41b1e1", "Experimental");
        AddLegendItem(CanvasPlot1D, 4, 28, "#c0c0c0", "Quality");
    }

    private void DrawPolyline(Canvas canvas, Point[] points, Color color, double thickness)
    {
        var polyline = new Polyline
        {
            Points = new Avalonia.Points(points),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            IsHitTestVisible = false
        };
        canvas.Children.Add(polyline);
    }

    private void AddLegendItem(Canvas canvas, double x, double y, string color, string text)
    {
        var line = new Line
        {
            StartPoint = new Point(x, y + 5),
            EndPoint = new Point(x + 20, y + 5),
            Stroke = new SolidColorBrush(Color.Parse(color)),
            StrokeThickness = 2,
            IsHitTestVisible = false
        };
        canvas.Children.Add(line);

        var label = new TextBlock
        {
            Text = text,
            FontSize = 9,
            Foreground = Brushes.Gray,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(label, x + 24);
        Canvas.SetTop(label, y);
        canvas.Children.Add(label);
    }

    private static double GetLambda(double voltage)
    {
        // Relativistic electron wavelength in Angstroms
        double v = voltage * 1000;
        return 12.2643 / Math.Sqrt(v + 0.97845e-6 * v * v) * 1e-10;
    }
}

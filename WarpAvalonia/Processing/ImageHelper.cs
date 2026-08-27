using System;
using System.IO;
using Warp;
using Warp.Headers;
using Warp.Tools;
using WarpImage = Warp.Image;

namespace WarpAvalonia;

/// <summary>
/// Static utility methods for image loading and preparation.
/// Extracted from WPF Warp's MainWindow.LoadAndPrepare* static methods
/// to avoid coupling dialog code to the UI layer.
/// </summary>
public static class ImageHelper
{
    /// <summary>
    /// Load and prepare gain reference image from the settings path.
    /// Equivalent to WPF MainWindow.LoadAndPrepareGainReference().
    /// </summary>
    public static WarpImage? LoadAndPrepareGainReference(OptionsWarp options)
    {
        if (string.IsNullOrEmpty(options.Import.GainPath) || !File.Exists(options.Import.GainPath))
            return null;

        WarpImage gain = WarpImage.FromFile(options.Import.GainPath);

        if (options.Import.GainFlipX)
            gain = gain.AsFlippedX();
        if (options.Import.GainFlipY)
            gain = gain.AsFlippedY();
        if (options.Import.GainTranspose)
            gain = gain.AsTransposed();

        return gain;
    }

    /// <summary>
    /// Load and prepare defect map from the settings path.
    /// Equivalent to WPF MainWindow.LoadAndPrepareDefectMap().
    /// </summary>
    public static DefectModel? LoadAndPrepareDefectMap(OptionsWarp options)
    {
        if (string.IsNullOrEmpty(options.Import.DefectsPath) || !File.Exists(options.Import.DefectsPath))
            return null;

        WarpImage defectImage = WarpImage.FromFile(options.Import.DefectsPath);
        var model = new DefectModel(defectImage, 4);
        defectImage.Dispose();
        return model;
    }

    /// <summary>
    /// Load header and image data from an MRC/TIFF/EER file, apply gain and defect corrections.
    /// Equivalent to WPF MainWindow.LoadAndPrepareHeaderAndMap().
    /// </summary>
    public static void LoadAndPrepareHeaderAndMap(string path, WarpImage? gain, DefectModel? defectMap,
        decimal scaleFactor, out MapHeader header, out WarpImage stack)
    {
        header = MapHeader.ReadFromFile(path);

        stack = WarpImage.FromFile(path);

        if (gain != null)
            stack.MultiplySlices(gain);

        if (defectMap != null)
        {
            WarpImage corrected = stack.GetCopy();
            defectMap.Correct(stack, corrected);
            stack.Dispose();
            stack = corrected;
        }

        if (scaleFactor != 1M)
        {
            WarpImage scaled = stack.AsScaled(new int2((int)Math.Round(header.Dimensions.X * (float)scaleFactor),
                                                       (int)Math.Round(header.Dimensions.Y * (float)scaleFactor)));
            stack.Dispose();
            stack = scaled;
        }
    }
}

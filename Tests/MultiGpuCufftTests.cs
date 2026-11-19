using System;
using Warp;
using Warp.Tools;
using Xunit;

namespace Tests;

/// <summary>Runs only on a CUDA host with at least two visible GPUs and WARP_RUN_CUDA_TESTS=1.</summary>
public sealed class MultiGpuCudaFactAttribute : FactAttribute
{
    public MultiGpuCudaFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WARP_RUN_CUDA_TESTS") != "1")
            Skip = "Set WARP_RUN_CUDA_TESTS=1 on a host with at least two CUDA devices.";
        else if (GPU.GetDeviceCount() < 2)
            Skip = $"Needs at least two CUDA devices; {GPU.GetDeviceCount()} visible.";
    }
}

/// <summary>
/// Regression test for the multi-GPU cuFFT failure fixed by building NativeAcceleration against the
/// shared CUDA runtime. With a statically embedded runtime, cudaSetDevice(N) changed only the static
/// runtime's current device while libcufft.so read its own runtime's, so a transform issued after
/// selecting GPU N != 0 ran against the wrong device and failed or returned garbage, intermittently.
/// Here every device computes the same transforms as device 0, and must agree with it.
/// </summary>
public class MultiGpuCufftTests
{
    static float[] Deterministic(int n, int seed)
    {
        var rng = new Random(seed);
        var data = new float[n];
        for (int i = 0; i < n; i++)
            data[i] = (float)(rng.NextDouble() * 2 - 1);
        return data;
    }

    static (float[] Spectrum, float[] RoundTrip) Transform(int device, float[] input, int3 dims, bool isVolume)
    {
        GPU.SetDevice(device);
        using var real = new Image(input, dims);
        using var ft = real.AsFFT(isVolume);
        float[] spectrum = ft.GetHostContinuousCopy();
        using var back = ft.AsIFFT(isVolume, 0, true);
        float[] roundTrip = back.GetHostContinuousCopy();
        Assert.Equal(device, GPU.GetDevice());   // the transform left the selected device current
        return (spectrum, roundTrip);
    }

    static double MaxRelativeDifference(float[] a, float[] b)
    {
        Assert.Equal(a.Length, b.Length);
        double maxAbs = 0, scale = 0;
        for (int i = 0; i < a.Length; i++)
        {
            maxAbs = Math.Max(maxAbs, Math.Abs(a[i] - b[i]));
            scale = Math.Max(scale, Math.Abs(a[i]));
        }
        return maxAbs / Math.Max(scale, 1e-30);
    }

    [MultiGpuCudaFact]
    public void CufftOnEveryDeviceMatchesDeviceZero()
    {
        int devices = GPU.GetDeviceCount();
        var cases = new (int3 Dims, bool IsVolume)[] { (new int3(512, 512, 1), false), (new int3(96, 96, 96), true) };

        foreach (var (dims, isVolume) in cases)
        {
            float[] input = Deterministic((int)dims.Elements(), seed: 1234);
            var reference = Transform(0, input, dims, isVolume);
            Assert.True(MaxRelativeDifference(input, reference.RoundTrip) < 1e-4,
                        $"device 0 round trip of {dims} is not the identity");

            // Repeat, alternating devices: the original failure depended on context initialisation
            // order, so one pass per device is not enough to exercise it.
            for (int repeat = 0; repeat < 3; repeat++)
                for (int d = 1; d < devices; d++)
                {
                    var result = Transform(d, input, dims, isVolume);
                    double spectrumDiff = MaxRelativeDifference(reference.Spectrum, result.Spectrum);
                    double roundTripDiff = MaxRelativeDifference(input, result.RoundTrip);
                    Assert.True(spectrumDiff < 1e-5, $"device {d} spectrum of {dims} differs from device 0 by {spectrumDiff:E2} (repeat {repeat})");
                    Assert.True(roundTripDiff < 1e-4, $"device {d} round trip of {dims} is off by {roundTripDiff:E2} (repeat {repeat})");
                    Transform(0, input, dims, isVolume);
                }
        }

        GPU.SetDevice(0);
    }
}

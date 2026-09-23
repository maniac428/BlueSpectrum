using System;
using System.Collections.Generic;
using System.Diagnostics;
using BlueSpectrum.Dsp;

namespace BlueSpectrum.Tests;

/// <summary>Deterministic DSP checks. These do not establish that WASAPI captures a physical output device.</summary>
public static class DspTests
{
    public static object Run()
    {
        var stopwatch = Stopwatch.StartNew();
        var passed = new List<string>();
        var measurements = new Dictionary<string, double>();

        var silence = new StereoSpectrumAnalyzer(48000);
        Require(AllFloor(silence.LeftDb) && AllFloor(silence.RightDb), "Initial state must be silent.");
        Require(FullRangeFloor(silence), "Initial full-range state must be silent.");
        silence.AddFrames(new float[48000 * 2]);
        Require(AllFloor(silence.LeftDb) && AllFloor(silence.RightDb), "Silence produced visible energy.");
        Require(FullRangeFloor(silence), "Silence produced full-range energy.");
        Require(silence.FramesAnalyzed == 43, "FFT/hop count is incorrect.");
        passed.Add("Silence, initial floor and FFT/hop accounting");

        foreach (int sampleRate in new[] { 44100, 48000 })
        {
            var leftOnly = Analyze(sampleRate, 1000, 1, 0);
            Require(MaxBand(leftOnly.LeftDb) == 3, "1 kHz must peak in the 1 kHz band.");
            Require(AllFloor(leftOnly.RightDb), "Left input leaked into right output.");
            Near(leftOnly.LeftDb[3], -3.0102999566, 0.025, "Full-scale sine RMS convention");
            Near(leftOnly.LeftFullRangeDb, -3.0102999566, 0.0001, "Full-range full-scale sine RMS convention");
            Require(leftOnly.RightFullRangeDb == StereoSpectrumAnalyzer.FloorDb, "Left input leaked into right full-range output.");
            var rightOnly = Analyze(sampleRate, 1000, 0, 1);
            Require(AllFloor(rightOnly.LeftDb), "Right input leaked into left output.");
            Require(MaxBand(rightOnly.RightDb) == 3, "Right 1 kHz band is incorrect.");
            Near(rightOnly.RightFullRangeDb, -3.0102999566, 0.0001, "Right full-range full-scale reference");
            Require(rightOnly.LeftFullRangeDb == StereoSpectrumAnalyzer.FloorDb, "Right input leaked into left full-range output.");
            var same = Analyze(sampleRate, 1000, 0.5, 0.5);
            for (int i = 0; i < 7; i++)
                Near(same.LeftDb[i], same.RightDb[i], 1e-12, "Identical channels");
            Near(same.LeftDb[3] - leftOnly.LeftDb[3], -6.0205999133, 0.0001, "Half amplitude should change power by -6.0206 dB");
            Near(same.LeftFullRangeDb, same.RightFullRangeDb, 1e-12, "Identical full-range channels");
            Near(same.LeftFullRangeDb - leftOnly.LeftFullRangeDb, -6.0205999133, 0.0001, "Full-range half-amplitude power scaling");
            measurements[$"full_scale_1k_{sampleRate}_dBFS"] = leftOnly.LeftDb[3];
            measurements[$"full_range_1k_{sampleRate}_dBFS"] = leftOnly.LeftFullRangeDb;
            passed.Add($"{sampleRate} Hz: L-only, R-only, equal channels, full-scale reference, amplitude scaling");

            var low = Analyze(sampleRate, 63, 0.8, 0.8);
            var high = Analyze(sampleRate, 16000, 0.8, 0.8);
            Require(MaxBand(low.LeftDb) == 0, "63 Hz was mapped to the wrong band.");
            Require(MaxBand(high.LeftDb) == 6, "16 kHz was mapped to the wrong band.");
            Require(low.LeftDb[0] - low.LeftDb[6] > 60, "Low signal contaminated the high band.");
            Require(high.LeftDb[6] - high.LeftDb[0] > 60, "High signal contaminated the low band.");
            passed.Add($"{sampleRate} Hz: 63 Hz and 16 kHz band separation");
        }

        var smallFft = Analyze(48000, 1000, 0.75, 0.25, 2048);
        var largeFft = Analyze(48000, 1000, 0.75, 0.25, 4096);
        Near(smallFft.LeftDb[3], largeFft.LeftDb[3], 0.01, "FFT-size-independent integrated power");
        Near(smallFft.RightDb[3], largeFft.RightDb[3], 0.01, "FFT-size-independent right power");
        Near(smallFft.LeftFullRangeDb, largeFft.LeftFullRangeDb, 0.0001, "FFT-size-independent full-range power");
        measurements["fft_2048_minus_4096_dB"] = smallFft.LeftDb[3] - largeFft.LeftDb[3];
        passed.Add("2048/4096 FFT power normalization");

        // Distinct, exactly bin-centered tones are orthogonal under this Hann
        // power window. Their mean-square powers add, not their dB values.
        var dualTone = new StereoSpectrumAnalyzer(48000);
        var dualSamples = MakeSignal(48000, 750, 0.5, 0.25, 4096);
        var secondTone = MakeSignal(48000, 6000, 0.25, 0, 4096);
        for (int i = 0; i < dualSamples.Length; i++) dualSamples[i] += secondTone[i];
        dualTone.AddFrames(dualSamples);
        Near(dualTone.LeftFullRangeDb, 10 * Math.Log10((0.5 * 0.5 + 0.25 * 0.25) / 2), 0.0001, "Dual-tone full-range power additivity");
        Near(dualTone.RightFullRangeDb, 10 * Math.Log10(0.25 * 0.25 / 2), 0.0001, "Dual-tone independent right full-range power");
        passed.Add("Full-range dual-tone mean-square power addition and stereo independence");

        var fragmented = new StereoSpectrumAnalyzer(48000);
        var singleChunk = new StereoSpectrumAnalyzer(48000);
        float[] signal = MakeSignal(48000, 6300, 0.3, 0.1, 12345);
        singleChunk.AddFrames(signal);
        int offset = 0;
        int[] chunkSizes = [2, 34, 1026, 14, 4094, 8];
        int chunk = 0;
        while (offset < signal.Length)
        {
            int count = Math.Min(chunkSizes[chunk++ % chunkSizes.Length], signal.Length - offset);
            fragmented.AddFrames(signal.AsSpan(offset, count));
            offset += count;
        }
        Require(fragmented.FramesAnalyzed == singleChunk.FramesAnalyzed, "Chunk boundaries changed FFT count.");
        for (int i = 0; i < 7; i++)
        {
            Near(fragmented.LeftDb[i], singleChunk.LeftDb[i], 1e-12, "Chunk-invariant left result");
            Near(fragmented.RightDb[i], singleChunk.RightDb[i], 1e-12, "Chunk-invariant right result");
        }
        fragmented.Reset();
        Require(fragmented.FramesAnalyzed == 0 && AllFloor(fragmented.LeftDb) && AllFloor(fragmented.RightDb), "Reset did not clear state.");
        Require(FullRangeFloor(fragmented), "Reset did not clear full-range state.");
        fragmented.AddFrames(new float[4096 * 2]);
        Require(AllFloor(fragmented.LeftDb) && AllFloor(fragmented.RightDb), "Reset retained old audio.");
        Require(FullRangeFloor(fragmented), "Reset retained old full-range energy.");
        passed.Add("Arbitrary complete-frame chunks, ring wrapping and reset");

        // At 16 kHz sample rate, Nyquist is 8 kHz. The nominal 16 kHz band is
        // wholly unavailable and must not wrap FFT indices into a lower band.
        // This checks bin mapping, not anti-aliasing of already sampled audio:
        // frequencies above Nyquist are inherently indistinguishable after sampling.
        var lowRate = Analyze(16000, 6300, 1, 0);
        Require(MaxBand(lowRate.LeftDb) == 5, "Nyquist-clipped 6.3 kHz band is incorrect.");
        Require(lowRate.LeftDb[6] == StereoSpectrumAnalyzer.FloorDb, "Unavailable 16 kHz band wrapped below Nyquist.");
        var nearNyquist = Analyze(44100, 19000, 0.5, 0);
        Require(MaxBand(nearNyquist.LeftDb) == 6, "High-frequency FFT bins wrapped into a low band.");
        var aboveDisplay = Analyze(48000, 22000, 1, 0);
        Require(MaxValue(aboveDisplay.LeftDb) < -65, "Signal above the 20 kHz display limit folded into visible bands.");
        Near(aboveDisplay.LeftFullRangeDb, -3.0102999566, 0.0001, "Full-range includes captured signal above the displayed bands");
        passed.Add("Nyquist-clipped unavailable bands, high-frequency mapping and 20 kHz display limit");

        var invalid = new StereoSpectrumAnalyzer(48000);
        var invalidSamples = new float[4096 * 2];
        invalidSamples[0] = float.NaN;
        invalidSamples[3] = float.PositiveInfinity;
        invalidSamples[11] = float.NegativeInfinity;
        invalid.AddFrames(invalidSamples);
        Require(AllFloor(invalid.LeftDb) && AllFloor(invalid.RightDb), "Non-finite input poisoned the FFT.");
        Require(FullRangeFloor(invalid), "Non-finite input poisoned full-range power.");
        bool rejected = false;
        try { invalid.AddFrames(new float[3]); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "Incomplete stereo frame was silently accepted.");
        passed.Add("Invalid-sample sanitization and incomplete-frame rejection");

        // Warm methods before measuring only the streaming path. Input and FFT
        // scratch buffers are preallocated. This catches per-hop object churn.
        var allocationAnalyzer = new StereoSpectrumAnalyzer(48000);
        float[] allocationSignal = MakeSignal(48000, 400, 0.5, 0.25, 8192);
        for (int i = 0; i < 4; i++) allocationAnalyzer.AddFrames(allocationSignal);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 8; i++) allocationAnalyzer.AddFrames(allocationSignal);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, $"Streaming FFT allocated {allocated} bytes.");
        measurements["streaming_allocated_bytes"] = allocated;
        passed.Add("Zero allocations in warmed streaming analysis");

        stopwatch.Stop();
        return new
        {
            success = true,
            suite = "StereoSpectrumAnalyzer",
            passed = passed.ToArray(),
            measurements,
            elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
            scope = "Synthetic DSP tests only; actual WASAPI capture requires separate integration verification."
        };
    }

    private static StereoSpectrumAnalyzer Analyze(int sampleRate, double frequency, double leftAmplitude, double rightAmplitude, int fftSize = 4096)
    {
        var analyzer = new StereoSpectrumAnalyzer(sampleRate, fftSize, Math.Min(1024, fftSize));
        analyzer.AddFrames(MakeSignal(sampleRate, frequency, leftAmplitude, rightAmplitude, sampleRate));
        return analyzer;
    }

    private static float[] MakeSignal(int sampleRate, double frequency, double leftAmplitude, double rightAmplitude, int frames)
    {
        var values = new float[frames * 2];
        for (int frame = 0; frame < frames; frame++)
        {
            double sine = Math.Sin(2 * Math.PI * frequency * frame / sampleRate);
            values[frame * 2] = (float)(sine * leftAmplitude);
            values[frame * 2 + 1] = (float)(sine * rightAmplitude);
        }
        return values;
    }

    private static bool FullRangeFloor(StereoSpectrumAnalyzer analyzer)
        => analyzer.LeftFullRangeDb == StereoSpectrumAnalyzer.FloorDb && analyzer.RightFullRangeDb == StereoSpectrumAnalyzer.FloorDb;

    private static bool AllFloor(double[] values)
    {
        foreach (double value in values) if (value != StereoSpectrumAnalyzer.FloorDb) return false;
        return true;
    }

    private static int MaxBand(double[] values)
    {
        int max = 0;
        for (int i = 1; i < values.Length; i++) if (values[i] > values[max]) max = i;
        return max;
    }

    private static double MaxValue(double[] values) => values[MaxBand(values)];
    private static void Near(double actual, double expected, double tolerance, string name)
        => Require(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance,
            $"{name}: expected {expected:F8}, received {actual:F8}, tolerance {tolerance}.");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

using System.Diagnostics;

namespace BlueSpectrum.Tests;

public static class PipelineTests
{
    public static async Task<object> Run()
    {
        var passed = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        var left = new double[7];
        var right = new double[7];
        using var pipeline = new SpectrumPipeline();
        var tone = Tone(48000, 1000, 4096);
        Require(!pipeline.Snapshot(left, right), "New pipeline reported audio.");
        Require(!pipeline.Snapshot(left, right, out double leftFullRange, out double rightFullRange) && leftFullRange == -100 && rightFullRange == -100,
            "New pipeline retained full-range data.");
        pipeline.Submit(tone, 48000);
        await Until(() => pipeline.Snapshot(left, right, out leftFullRange, out rightFullRange));
        Require(left[3] > -3.1 && right.All(value => value == -100), "Pipeline changed the independent channel signal.");
        Require(Math.Abs(leftFullRange + 3.0102999566) < 0.0001 && rightFullRange == -100, "Pipeline changed full-range RMS power or stereo separation.");
        passed.Add("Asynchronous stereo analysis and initial silence");

        await Task.Delay(260);
        Require(!pipeline.Snapshot(left, right), "Stopped input remained recent after the silence timeout.");
        passed.Add("No-packet silence timeout");

        // Queue loud old-generation packets, reset immediately, then feed fewer
        // than one new FFT window. No old result may resurrect after Reset.
        for (int i = 0; i < 32; i++) pipeline.Submit(tone, 48000);
        pipeline.Reset();
        Require(!pipeline.Snapshot(left, right, out leftFullRange, out rightFullRange) && left.All(value => value == -100) && leftFullRange == -100 && rightFullRange == -100,
            "Reset failed to clear the published band and full-range results immediately.");
        pipeline.Submit(Tone(48000, 1000, 2048), 48000);
        await Task.Delay(60);
        Require(!pipeline.Snapshot(left, right), "Old-generation data survived reset or a partial window was published.");
        pipeline.Submit(new float[2048 * 2], 48000);
        await Until(() => pipeline.Snapshot(left, right));
        passed.Add("Generation reset rejects queued/in-flight data and retains new-session chunk boundaries");

        // A rate change must clear the old snapshot while the new format builds
        // its first complete analysis window.
        pipeline.Submit(Tone(44100, 1000, 2048), 44100);
        await Until(() => !pipeline.Snapshot(left, right, out leftFullRange, out rightFullRange));
        Require(left.All(value => value == -100) && leftFullRange == -100 && rightFullRange == -100, "Sample-rate change retained old band or full-range values.");
        pipeline.Submit(Tone(44100, 1000, 4096), 44100);
        await Until(() => pipeline.Snapshot(left, right));
        Require(left[3] > -3.1, "Analysis failed after a sample-rate change.");
        passed.Add("Sample-rate transition clears old data and recovers");

        pipeline.Submit(new float[3], 48000);
        Require(pipeline.LastError is not null && !pipeline.Snapshot(left, right, out leftFullRange, out rightFullRange) && leftFullRange == -100 && rightFullRange == -100,
            "Invalid frame shape was not rejected safely or retained full-range values.");
        pipeline.Submit(tone, 0);
        Require(pipeline.LastError is not null, "Invalid sample rate was not rejected.");
        pipeline.Submit(tone, 48000);
        await Until(() => pipeline.Snapshot(left, right));
        Require(pipeline.LastError is null, "Successful recovery retained an obsolete error.");
        passed.Add("Malformed packets fail safely and subsequent valid input recovers");

        // Producer bursts are deliberately much faster than a complete FFT.
        // Confirm actual drops, then a later accepted packet must trigger reset
        // instead of splicing across missing audio.
        long dropsBefore = pipeline.DroppedPackets;
        long resetsBefore = pipeline.DiscontinuityResets;
        for (int i = 0; i < 512; i++) pipeline.Submit(tone, 48000);
        Require(pipeline.DroppedPackets > dropsBefore, "Overflow exercise did not saturate the bounded queue.");
        await Task.Delay(120);
        pipeline.Submit(tone, 48000);
        await Until(() => pipeline.DiscontinuityResets > resetsBefore && pipeline.Snapshot(left, right));
        passed.Add("Bounded-queue overflow returns to valid analysis with discontinuity reset");

        pipeline.Reset();
        pipeline.Submit(new float[4096 * 2], 48000);
        await Until(() => pipeline.Snapshot(left, right, out leftFullRange, out rightFullRange));
        Require(left.All(value => value == -100) && right.All(value => value == -100) && leftFullRange == -100 && rightFullRange == -100,
            "Silent audio retained previous band or full-range energy.");
        pipeline.Dispose();
        pipeline.Dispose();
        pipeline.Submit(tone, 48000);
        Require(!pipeline.Snapshot(left, right, out leftFullRange, out rightFullRange) && leftFullRange == -100 && rightFullRange == -100,
            "Disposed pipeline reported active data or retained full-range values.");
        passed.Add("Explicit silent packets, queue draining, repeated disposal and post-dispose submission");

        return new { success = true, suite = "SpectrumPipeline", passed, droppedPackets = pipeline.DroppedPackets,
            discontinuityResets = pipeline.DiscontinuityResets, elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds };
    }

    private static float[] Tone(int rate, double frequency, int frames)
    {
        var samples = new float[frames * 2];
        for (int i = 0; i < frames; i++) samples[2 * i] = (float)Math.Sin(2 * Math.PI * frequency * i / rate);
        return samples;
    }
    private static async Task Until(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.ElapsedMilliseconds > 2500) throw new InvalidOperationException("Pipeline did not reach the expected state within 2.5 seconds.");
            await Task.Delay(5);
        }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

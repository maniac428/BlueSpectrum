using System.Diagnostics;
using System.Runtime.InteropServices;
using BlueSpectrum.Audio;
using BlueSpectrum.Dsp;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BlueSpectrum.Tests;

/// <summary>
/// Explicit diagnostic only: sends quiet test tones to one actual render endpoint and
/// verifies the samples received back through the production WASAPI capture and pipeline.
/// Normal application startup never calls this method.
/// </summary>
public static class LiveLoopbackTests
{
    public static object Run(string? deviceId = null)
    {
        var report = new LiveReport();
        var stopwatch = Stopwatch.StartNew();
        var messages = new List<string>();
        var messageGate = new object();
        MMDeviceEnumerator? enumerator = null;
        MMDevice? endpoint = null;
        AudioCaptureService? capture = null;
        SpectrumPipeline? pipeline = null;
        WasapiPlayer? player = null;
        QuietTestProvider? provider = null;
        long packets = 0;
        int sampleRate = 0;
        Exception? playbackError = null;
        try
        {
            enumerator = new MMDeviceEnumerator();
            endpoint = string.IsNullOrEmpty(deviceId) ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(deviceId);
            report.EndpointId = endpoint.ID;
            report.EndpointName = endpoint.FriendlyName;
            pipeline = new SpectrumPipeline();
            capture = new AudioCaptureService();
            var activePipeline = pipeline;
            capture.StereoSamples += (data, rate) =>
            {
                Volatile.Write(ref sampleRate, rate);
                Interlocked.Increment(ref packets);
                activePipeline.Submit(data, rate);
            };
            capture.StatusChanged += message => { lock (messageGate) messages.Add(message); };
            capture.Start(endpoint.ID);
            if (!SpinWait.SpinUntil(() => capture.IsRunning, 2500))
            {
                report.Inconclusive = true;
                report.Reasons.Add("실제 WASAPI 캡처를 시작하지 못했습니다. 시험음은 재생하지 않았습니다.");
            }
            else
            {
                provider = new QuietTestProvider();
                player = new WasapiPlayerBuilder().WithDevice(endpoint).WithSharedMode()
                    .WithEventSync().WithLatency(40).Build();
                player.PlaybackStopped += (_, args) =>
                {
                    if (args.Exception is not null) Volatile.Write(ref playbackError, args.Exception);
                };
                player.Init(provider);
                report.PlayerFormat = player.OutputWaveFormat.ToString();
                player.Play();

                var baseline = MeasureStage(provider, pipeline, "silent-baseline", 0, 700);
                report.Stages.Add(baseline);
                if (baseline.MaxObservedDb > report.BaselineQuietThresholdDb)
                {
                    report.InterferenceDetected = report.Inconclusive = true;
                    report.Reasons.Add("무음 기준 구간에서 다른 재생 신호가 검출되어 간섭으로 판정했습니다. 시험음 단계는 생략했습니다.");
                }
                else
                {
                    report.Stages.Add(MeasureStage(provider, pipeline, "left-1000Hz", 1, 850));
                    report.Stages.Add(MeasureStage(provider, pipeline, "right-1000Hz", 2, 850));
                    report.Stages.Add(MeasureStage(provider, pipeline, "left-63Hz-right-6300Hz", 3, 850));
                    report.Stages.Add(MeasureStage(provider, pipeline, "silent-after-tones", 4, 700));
                    Evaluate(report);
                }
                provider.Mode = 4;
                report.ProviderFramesGenerated = provider.FramesGenerated;
                report.ProviderMaximumSeconds = QuietTestProvider.MaximumSeconds;
                if (Volatile.Read(ref playbackError) is { } error)
                    throw new InvalidOperationException("시험음 재생 중 오류가 발생했습니다.", error);
                if (pipeline.LastError is { } pipelineError)
                    throw new InvalidOperationException(pipelineError);
            }
        }
        catch (Exception error)
        {
            report.Inconclusive = true;
            report.Errors.Add(error.ToString());
        }
        finally
        {
            if (provider is not null) provider.Mode = 4;
            if (capture is not null)
            {
                report.CaptureWasRunning = capture.IsRunning;
                report.CaptureFormat = capture.FormatDescription;
                report.CapturedFrames = capture.TotalFramesCaptured;
            }
            void CleanUp(string resource, Action action)
            {
                try { action(); }
                catch (Exception error) { report.Errors.Add($"{resource}: {error.Message}"); }
            }
            if (player is not null) CleanUp("player", player.Dispose);
            if (capture is not null) CleanUp("capture", capture.Dispose);
            if (pipeline is not null) CleanUp("pipeline", pipeline.Dispose);
            if (endpoint is not null) CleanUp("endpoint", endpoint.Dispose);
            if (enumerator is not null) CleanUp("enumerator", enumerator.Dispose);
            report.CapturedPackets = Interlocked.Read(ref packets);
            report.CaptureSampleRate = Volatile.Read(ref sampleRate);
            lock (messageGate) report.Messages = messages.ToArray();
            report.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        }
        report.Passed = !report.Inconclusive && !report.InterferenceDetected &&
            report.Errors.Count == 0 && report.Checks.Count > 0 && report.Checks.All(check => check.Passed);
        report.Outcome = report.Passed ? "passed" : report.Inconclusive ? "inconclusive" : "failed";
        return report;
    }

    private static StageReport MeasureStage(QuietTestProvider provider, SpectrumPipeline pipeline,
        string name, int mode, int durationMilliseconds)
    {
        provider.Mode = mode;
        var watch = Stopwatch.StartNew();
        var leftWindows = new List<double[]>();
        var rightWindows = new List<double[]>();
        var freshCount = 0;
        // Observe the last 250 ms only, after playback buffering, the ramp and FFT settle.
        var nextSampleAt = durationMilliseconds - 250;
        while (watch.ElapsedMilliseconds < durationMilliseconds)
        {
            if (watch.ElapsedMilliseconds >= nextSampleAt)
            {
                var left = new double[7];
                var right = new double[7];
                if (pipeline.Snapshot(left, right)) freshCount++;
                leftWindows.Add(left); rightWindows.Add(right);
                nextSampleAt += 50;
            }
            Thread.Sleep(10);
        }
        if (leftWindows.Count == 0)
        {
            var left = new double[7]; var right = new double[7];
            if (pipeline.Snapshot(left, right)) freshCount++;
            leftWindows.Add(left); rightWindows.Add(right);
        }
        static double[] MedianBands(List<double[]> windows) => Enumerable.Range(0, 7)
            .Select(band => windows.Select(window => window[band]).OrderBy(value => value).ElementAt(windows.Count / 2)).ToArray();
        return new StageReport
        {
            Name = name, RequestedMilliseconds = durationMilliseconds, MeasuredMilliseconds = watch.Elapsed.TotalMilliseconds,
            LeftDb = MedianBands(leftWindows), RightDb = MedianBands(rightWindows), FreshSnapshots = freshCount,
            Snapshots = leftWindows.Count, MaxObservedDb = leftWindows.Concat(rightWindows).Max(window => window.Max())
        };
    }

    private static void Evaluate(LiveReport report)
    {
        var leftOnly = report.Stages[1];
        var rightOnly = report.Stages[2];
        var lowHigh = report.Stages[3];
        var silence = report.Stages[4];
        const int oneKhz = 3, low = 0, high = 5;
        const double minimumDetectedDb = -80;
        static int MaximumBand(double[] levels) => Array.IndexOf(levels, levels.Max());
        void Check(string name, bool passed, string detail) => report.Checks.Add(new(name, passed, detail));

        Check("Actual captured signal available during all tone stages",
            leftOnly.FreshSnapshots > 0 && rightOnly.FreshSnapshots > 0 && lowHigh.FreshSnapshots > 0 &&
            leftOnly.LeftDb[oneKhz] > minimumDetectedDb && rightOnly.RightDb[oneKhz] > minimumDetectedDb &&
            lowHigh.LeftDb[low] > minimumDetectedDb && lowHigh.RightDb[high] > minimumDetectedDb,
            $"Required tone level > {minimumDetectedDb} dBFS with fresh captured packets");
        Check("Left-only 1 kHz: left dominates right by at least 20 dB",
            MaximumBand(leftOnly.LeftDb) == oneKhz && leftOnly.LeftDb[oneKhz] - leftOnly.RightDb[oneKhz] >= 20,
            $"L={leftOnly.LeftDb[oneKhz]:F2}, R={leftOnly.RightDb[oneKhz]:F2} dBFS");
        Check("Right-only 1 kHz: right dominates left by at least 20 dB",
            MaximumBand(rightOnly.RightDb) == oneKhz && rightOnly.RightDb[oneKhz] - rightOnly.LeftDb[oneKhz] >= 20,
            $"L={rightOnly.LeftDb[oneKhz]:F2}, R={rightOnly.RightDb[oneKhz]:F2} dBFS");
        Check("63 Hz left and 6300 Hz right map to correct bands and channels",
            MaximumBand(lowHigh.LeftDb) == low && MaximumBand(lowHigh.RightDb) == high &&
            lowHigh.LeftDb[low] - lowHigh.RightDb[low] >= 20 && lowHigh.RightDb[high] - lowHigh.LeftDb[high] >= 20,
            $"Left peak={StereoSpectrumAnalyzer.Centers[MaximumBand(lowHigh.LeftDb)]} Hz; right peak={StereoSpectrumAnalyzer.Centers[MaximumBand(lowHigh.RightDb)]} Hz");
        Check("End silence returns every band to the numerical floor",
            silence.LeftDb.All(value => value <= report.SilenceFloorThresholdDb) &&
            silence.RightDb.All(value => value <= report.SilenceFloorThresholdDb),
            $"Required <= {report.SilenceFloorThresholdDb} dBFS; observed maximum {silence.MaxObservedDb:F2} dBFS");

        if (!report.Checks[0].Passed)
        {
            report.Inconclusive = true;
            report.Reasons.Add("예상 시험음 또는 새 캡처 신호를 충분히 검출하지 못했습니다. 음소거·앱 믹서 설정·장치 연결 상태를 확인해야 합니다.");
        }
        if (silence.MaxObservedDb > report.BaselineQuietThresholdDb)
        {
            report.InterferenceDetected = report.Inconclusive = true;
            report.Reasons.Add("시험음 종료 후에도 다른 재생 신호가 남아 있어 외부 신호 간섭 가능성이 있습니다.");
        }
        if (report.Checks.Any(check => !check.Passed) && !report.Inconclusive)
            report.Reasons.Add("일부 채널 또는 대역 검증이 기준에 미달했습니다. 실제 수치는 Stages와 Checks에 기록했습니다.");
    }

    private sealed class QuietTestProvider : IWaveProvider
    {
        internal const double MaximumSeconds = 5;
        private const int SourceRate = 48000, RampFrames = 384, MaximumFrames = SourceRate * 5;
        private const double Amplitude = .012;
        public volatile int Mode;
        private int _activeMode, _previousMode, _rampPosition = RampFrames;
        private long _frame;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SourceRate, 2);
        public long FramesGenerated => Interlocked.Read(ref _frame);

        public int Read(Span<byte> buffer)
        {
            var frames = (int)Math.Min(buffer.Length / 8, MaximumFrames - _frame);
            if (frames <= 0) return 0;
            var samples = MemoryMarshal.Cast<byte, float>(buffer[..(frames * 8)]);
            for (var index = 0; index < frames; index++)
            {
                var requestedMode = Mode;
                if (requestedMode != _activeMode)
                {
                    _previousMode = _activeMode;
                    _activeMode = requestedMode;
                    _rampPosition = 0;
                }
                var seconds = _frame / (double)SourceRate;
                var phase1k = Math.Sin(2 * Math.PI * 1000 * seconds);
                var phase63 = Math.Sin(2 * Math.PI * 63 * seconds);
                var phase6300 = Math.Sin(2 * Math.PI * 6300 * seconds);
                static (double Left, double Right) Tone(int mode, double oneK, double low, double high) => mode switch
                {
                    1 => (oneK, 0), 2 => (0, oneK), 3 => (low, high), _ => (0, 0)
                };
                var previous = Tone(_previousMode, phase1k, phase63, phase6300);
                var current = Tone(_activeMode, phase1k, phase63, phase6300);
                var blend = Math.Min(1.0, _rampPosition / (double)RampFrames);
                // Crossfade for 8 ms on mode changes, and fade before the hard 5-second end.
                var cutoff = Math.Min(1.0, (MaximumFrames - _frame) / (double)RampFrames);
                samples[index * 2] = (float)(Amplitude * cutoff * (previous.Left * (1 - blend) + current.Left * blend));
                samples[index * 2 + 1] = (float)(Amplitude * cutoff * (previous.Right * (1 - blend) + current.Right * blend));
                if (_rampPosition < RampFrames) _rampPosition++;
                _frame++;
            }
            return frames * 8;
        }
    }

    private sealed class LiveReport
    {
        public string Mode { get; } = "Actual WASAPI playback -> Windows mixer -> loopback capture -> production spectrum pipeline";
        public bool Passed { get; set; }
        public string Outcome { get; set; } = "inconclusive";
        public bool Inconclusive { get; set; }
        public bool InterferenceDetected { get; set; }
        public string? EndpointId { get; set; }
        public string? EndpointName { get; set; }
        public string? CaptureFormat { get; set; }
        public string? PlayerFormat { get; set; }
        public int CaptureSampleRate { get; set; }
        public bool CaptureWasRunning { get; set; }
        public long CapturedPackets { get; set; }
        public long CapturedFrames { get; set; }
        public long ProviderFramesGenerated { get; set; }
        public double ProviderMaximumSeconds { get; set; } = 5;
        public double SourcePeakAmplitude { get; } = .012;
        public double SourcePeakDbFS { get; } = 20 * Math.Log10(.012);
        public double BaselineQuietThresholdDb { get; } = -75;
        public double SilenceFloorThresholdDb { get; } = -90;
        public double[] BandCentersHz { get; } = StereoSpectrumAnalyzer.Centers.ToArray();
        public double ElapsedSeconds { get; set; }
        public List<StageReport> Stages { get; } = [];
        public List<CheckReport> Checks { get; } = [];
        public List<string> Reasons { get; } = [];
        public List<string> Errors { get; } = [];
        public string[] Messages { get; set; } = [];
        public string ScopeNote { get; } = "Does not measure physical loudspeaker output or end-to-end display latency; no endpoint/session volume settings are changed.";
    }

    private sealed class StageReport
    {
        public string Name { get; set; } = "";
        public int RequestedMilliseconds { get; set; }
        public double MeasuredMilliseconds { get; set; }
        public double[] LeftDb { get; set; } = [];
        public double[] RightDb { get; set; } = [];
        public int FreshSnapshots { get; set; }
        public int Snapshots { get; set; }
        public double MaxObservedDb { get; set; }
    }

    private sealed record CheckReport(string Name, bool Passed, string Detail);
}

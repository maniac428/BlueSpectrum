using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using BlueSpectrum;
using BlueSpectrum.Dsp;
using BlueSpectrum.UI;
using BlueSpectrum.Graphics;

namespace BlueSpectrum.Tests;

public static class FramePerformanceTest
{
    public static void Run(string output, int fps)
    {
        if (fps != 30 && fps != 60) throw new ArgumentOutOfRangeException(nameof(fps));
        var app = new Application();
        Program.ApplyTheme(app);
        var window = new MainWindow(false, new AppSettings { Width = 800, Height = 170, Fps = fps }, true)
        { ShowActivated = false, Title = "Blue Spectrum · 합성 입력 성능 검사" };
        var surface = window.Surface;
        var analyzer = new StereoSpectrumAnalyzer(48000, 2048, 512);
        var intervals = new List<double>();
        var work = new List<double>();
        var timer = new FrameClock(window.Dispatcher) { Interval = TimeSpan.FromSeconds(1.0 / fps) };
        var process = Process.GetCurrentProcess();
        var clock = new Stopwatch();
        long previous = 0, sampleIndex = 0, startFrames = 0;
        double measureStart = 0, cpuStart = 0;
        bool measuring = false;
        var samples = new float[2 * 48000 / fps];
        timer.Tick += (_, _) =>
        {
            long now = Stopwatch.GetTimestamp();
            double dt = previous == 0 ? 1.0 / fps : Stopwatch.GetElapsedTime(previous, now).TotalSeconds;
            previous = now;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < samples.Length / 2; i++, sampleIndex++)
            {
                double t = sampleIndex / 48000.0;
                double envelope = .25 + .75 * Math.Pow(Math.Max(0, Math.Sin(t * 2 * Math.PI * 2)), 3);
                double left = 0, right = 0;
                for (int k = 0; k < 7; k++)
                {
                    double wave = Math.Sin(t * 2 * Math.PI * StereoSpectrumAnalyzer.Centers[k]) * .06 * envelope;
                    left += wave; right += wave * (k % 2 == 0 ? .5 : 1);
                }
                samples[2*i]=(float)left; samples[2*i+1]=(float)right;
            }
            analyzer.AddFrames(samples);
            surface.Update(analyzer.LeftDb, analyzer.RightDb, dt, true, analyzer.LeftFullRangeDb, analyzer.RightFullRangeDb);
            sw.Stop();
            var host = surface.GpuHost;
            if (!measuring && clock.Elapsed.TotalSeconds >= 2)
            {
                measuring = true; measureStart = clock.Elapsed.TotalSeconds;
                cpuStart = process.TotalProcessorTime.TotalSeconds;
                startFrames = host?.Renderer?.RenderedFrameCount ?? 0;
            }
            else if (measuring)
            {
                intervals.Add(dt * 1000); work.Add(sw.Elapsed.TotalMilliseconds);
                if (clock.Elapsed.TotalSeconds - measureStart >= 10)
                {
                    timer.Stop(); process.Refresh();
                    double duration = clock.Elapsed.TotalSeconds - measureStart;
                    double cpu = process.TotalProcessorTime.TotalSeconds - cpuStart;
                    intervals.Sort(); work.Sort();
                    double P(List<double> list, double percentile) => list[Math.Min(list.Count-1, (int)Math.Floor(percentile * list.Count))];
                    var result = new {
                        TargetFps = fps, Seconds = duration, Updates = intervals.Count,
                        ActualUpdateFps = intervals.Count / duration,
                        GpuSubmittedFps = ((host?.Renderer?.RenderedFrameCount ?? 0) - startFrames) / duration,
                        FrameIntervalMedianMs = P(intervals, .5), FrameIntervalP95Ms = P(intervals, .95), FrameIntervalMaxMs = intervals.Last(),
                        UpdateWorkMedianMs = P(work,.5), UpdateWorkP95Ms = P(work,.95),
                        CpuPercentOneCore = 100 * cpu / duration, CpuPercentTotalMachine = 100 * cpu / duration / Environment.ProcessorCount,
                        WorkingSetMB = process.WorkingSet64 / 1048576.0, PrivateMB = process.PrivateMemorySize64 / 1048576.0,
                        LogicalProcessors = Environment.ProcessorCount, Adapter = host?.Renderer?.ActiveAdapter.Name,
                        Width = window.ActualWidth, Height = window.ActualHeight,
                        Scope = "Visible 800x170 window, 2-second warmup then 10-second synthetic stereo FFT and GPU rendering. Same compiled app assembly; timer matches MainWindow. Includes synthetic sample generation, excludes WASAPI, not end-to-end latency or scanout FPS." };
                    File.WriteAllText(output, JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
                    window.Close();
                }
            }
        };
        window.Loaded += (_,_)=> { clock.Start(); timer.Start(); };
        window.Closed += (_,_)=>timer.Dispose();
        app.Run(window);
    }
}

using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;
using BlueSpectrum.Dsp;

namespace BlueSpectrum;

public sealed class SpectrumPipeline : IDisposable
{
    private readonly record struct Packet(float[] Data, int Length, int SampleRate, long Timestamp, long Sequence, long Generation);
    private readonly Channel<Packet> queue = Channel.CreateBounded<Packet>(new BoundedChannelOptions(8) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly object sync = new();
    private readonly double[] left = Enumerable.Repeat(-100.0, 7).ToArray(), right = Enumerable.Repeat(-100.0, 7).ToArray();
    private double leftFullRangeDb = StereoSpectrumAnalyzer.FloorDb, rightFullRangeDb = StereoSpectrumAnalyzer.FloorDb;
    private readonly Task worker;
    private long lastProcessed;
    private long generation, sequence, droppedPackets, discontinuityResets;
    private int disposeStarted;
    private string? lastError;
    public string? LastError => Volatile.Read(ref lastError);
    internal long DroppedPackets => Interlocked.Read(ref droppedPackets);
    internal long DiscontinuityResets => Interlocked.Read(ref discontinuityResets);
    public SpectrumPipeline() { worker = Task.Run(Process); }
    public void Submit(float[] stereo, int sampleRate)
    {
        if (Volatile.Read(ref disposeStarted) != 0) return;
        if (stereo is null || (stereo.Length & 1) != 0 || sampleRate is < 1000 or > 768000)
        {
            lock (sync)
            {
                Invalidate();
                Volatile.Write(ref lastError, "분석할 오디오의 샘플레이트 또는 좌우 프레임 크기가 올바르지 않습니다.");
            }
            return;
        }
        if (stereo.Length < 2) return;
        // Take the generation before copying: Reset must also reject a producer
        // already copying a packet from the previous capture session.
        long packetGeneration = Volatile.Read(ref generation);
        long packetSequence = Interlocked.Increment(ref sequence);
        var data = ArrayPool<float>.Shared.Rent(stereo.Length);
        stereo.CopyTo(data, 0);
        if (!queue.Writer.TryWrite(new(data, stereo.Length, sampleRate, Stopwatch.GetTimestamp(), packetSequence, packetGeneration)))
        {
            Interlocked.Increment(ref droppedPackets);
            ArrayPool<float>.Shared.Return(data);
        }
    }
    /// <summary>Discard old session audio, including queued and currently analyzed packets.</summary>
    public void Reset()
    {
        lock (sync)
        {
            if (Volatile.Read(ref disposeStarted) != 0) return;
            Invalidate();
            Volatile.Write(ref lastError, null);
        }
    }
    // Caller holds sync. The same lock guards publication, so an in-flight FFT
    // can never publish a pre-reset result after the snapshot has been cleared.
    private void Invalidate()
    {
        Interlocked.Increment(ref generation);
        ClearSnapshot();
    }
    // Caller holds sync; clear band and full-range values as one publication.
    private void ClearSnapshot()
    {
        Array.Fill(left, StereoSpectrumAnalyzer.FloorDb);
        Array.Fill(right, StereoSpectrumAnalyzer.FloorDb);
        leftFullRangeDb = rightFullRangeDb = StereoSpectrumAnalyzer.FloorDb;
        lastProcessed = 0;
    }
    private async Task Process()
    {
        StereoSpectrumAnalyzer? analyzer = null;
        int rate = 0;
        long previous = 0, previousSequence = 0, activeGeneration = -1;
        try
        {
            await foreach (var packet in queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    if (Volatile.Read(ref disposeStarted) != 0 || packet.Generation != Volatile.Read(ref generation)) continue;
                    if (analyzer is null || rate != packet.SampleRate || activeGeneration != packet.Generation)
                    {
                        if (analyzer is null || rate != packet.SampleRate) analyzer = new(packet.SampleRate);
                        else analyzer.Reset();
                        rate = packet.SampleRate;
                        activeGeneration = packet.Generation;
                        previous = previousSequence = 0;
                        lock (sync)
                        {
                            if (packet.Generation == generation)
                            {
                                ClearSnapshot();
                            }
                        }
                    }
                    if (previousSequence != 0 && (packet.Sequence != previousSequence + 1 ||
                        Stopwatch.GetElapsedTime(previous, packet.Timestamp).TotalMilliseconds > 200))
                    {
                        analyzer.Reset();
                        Interlocked.Increment(ref discontinuityResets);
                    }
                    previous = packet.Timestamp;
                    previousSequence = packet.Sequence;
                    int framesBefore = analyzer.FramesAnalyzed;
                    analyzer.AddFrames(packet.Data.AsSpan(0, packet.Length));
                    if (analyzer.FramesAnalyzed != framesBefore)
                    {
                        lock (sync)
                        {
                            if (packet.Generation != generation || Volatile.Read(ref disposeStarted) != 0) continue;
                            Array.Copy(analyzer.LeftDb, left, 7); Array.Copy(analyzer.RightDb, right, 7);
                            leftFullRangeDb = analyzer.LeftFullRangeDb;
                            rightFullRangeDb = analyzer.RightFullRangeDb;
                            lastProcessed = packet.Timestamp;
                            Volatile.Write(ref lastError, null);
                        }
                    }
                }
                catch (Exception error)
                {
                    // One bad packet must not strand the bounded queue or kill
                    // analysis permanently. The next valid packet starts fresh.
                    analyzer = null;
                    previous = previousSequence = 0;
                    lock (sync)
                    {
                        if (packet.Generation == generation)
                        {
                            ClearSnapshot();
                            Volatile.Write(ref lastError, "오디오 분석 오류: " + error.Message);
                        }
                    }
                }
                finally { ArrayPool<float>.Shared.Return(packet.Data); }
            }
        }
        finally
        {
            // Disposal closes the writer and skips DSP while returning every
            // queued rental. This also covers an unexpected channel-reader exit.
            queue.Writer.TryComplete();
            while (queue.Reader.TryRead(out var remaining)) ArrayPool<float>.Shared.Return(remaining.Data);
        }
    }
    public bool Snapshot(double[] leftTarget, double[] rightTarget)
        => Snapshot(leftTarget, rightTarget, out _, out _);

    public bool Snapshot(double[] leftTarget, double[] rightTarget, out double leftFullRangeDb, out double rightFullRangeDb)
    {
        lock (sync)
        {
            Array.Copy(left, leftTarget, 7); Array.Copy(right, rightTarget, 7);
            leftFullRangeDb = this.leftFullRangeDb;
            rightFullRangeDb = this.rightFullRangeDb;
            return lastProcessed != 0 && Stopwatch.GetElapsedTime(lastProcessed).TotalMilliseconds < 220;
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) == 0)
        {
            lock (sync) Invalidate();
            queue.Writer.TryComplete();
        }
        worker.GetAwaiter().GetResult();
    }
}

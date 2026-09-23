using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BlueSpectrum.Audio;

public sealed record AudioDeviceInfo(string Id, string Name);

/// <summary>
/// Captures the chosen render endpoint in shared WASAPI loopback mode.
/// Start returns immediately; devices are opened and recovered on a dedicated MTA worker.
/// Stop normally joins the worker; a stalled driver gets at most three seconds on the caller
/// thread, with its detached worker finishing cleanup once the driver responds.
/// Events may arrive on worker threads. Handlers must return promptly and use asynchronous
/// UI dispatch; never call Start, Stop or Dispose from a capture event handler.
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    private readonly object _lifecycle = new();
    private CaptureRun? _run;
    private CaptureSnapshot _snapshot = CaptureSnapshot.Stopped;
    private bool _disposed;

    /// <summary>Interleaved L/R samples and their actual sample rate; buffer valid only during callback.</summary>
    public event Action<float[], int>? StereoSamples;
    public event Action<string>? StatusChanged;
    public event Action? StreamReset;

    public string DeviceName => Volatile.Read(ref _snapshot).DeviceName;
    public string FormatDescription => Volatile.Read(ref _snapshot).FormatDescription;
    public string? ActiveDeviceId => Volatile.Read(ref _snapshot).DeviceId;
    public bool IsRunning => Volatile.Read(ref _snapshot).IsRunning;
    public long TotalFramesCaptured
    {
        get { var run = Volatile.Read(ref _run); return run is null ? 0 : Interlocked.Read(ref run.TotalFrames); }
    }

    public static IReadOnlyList<AudioDeviceInfo> EnumerateDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        var result = new List<AudioDeviceInfo>();
        foreach (var endpoint in endpoints)
        {
            using (endpoint)
                result.Add(new AudioDeviceInfo(endpoint.ID, endpoint.FriendlyName));
        }
        return result.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    /// <param name="deviceId">Null follows the default multimedia render endpoint; an explicit ID waits for that endpoint.</param>
    public void Start(string? deviceId = null)
    {
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopCore();
            var run = new CaptureRun(string.IsNullOrWhiteSpace(deviceId) ? null : deviceId);
            run.Worker = new Thread(() => CaptureWorker(run))
            {
                IsBackground = true,
                Name = "BlueSpectrum endpoint monitor"
            };
            run.Worker.SetApartmentState(ApartmentState.MTA);
            Volatile.Write(ref _run, run);
            run.Worker.Start();
        }
    }

    public void Stop()
    {
        lock (_lifecycle)
            StopCore();
        Notify("캡처 일시 정지");
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
            StopCore();
            StereoSamples = null;
            StatusChanged = null;
            StreamReset = null;
        }
        GC.SuppressFinalize(this);
    }

    private void StopCore()
    {
        var run = Interlocked.Exchange(ref _run, null);
        if (run is not null)
        {
            run.RequestStop();
            run.Worker!.Join(3000);
        }
        Volatile.Write(ref _snapshot, CaptureSnapshot.Stopped);
    }

    private bool IsCurrent(CaptureRun run) => ReferenceEquals(Volatile.Read(ref _run), run);

    private void CaptureWorker(CaptureRun run)
    {
        var waitHandles = new WaitHandle[] { run.StopRequested, run.WakeRequested };
        try
        {
            while (IsCurrent(run))
            {
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    using var candidate = run.DeviceId is null
                        ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                        : enumerator.GetDevice(run.DeviceId);
                    if (candidate.DataFlow != DataFlow.Render || candidate.State != DeviceState.Active)
                        throw new InvalidOperationException("선택한 출력 장치가 연결되어 있지 않습니다.");

                    var session = Volatile.Read(ref run.Session);
                    if (session is not null && Volatile.Read(ref session.Ended) != 0)
                        throw session.Error ?? new InvalidOperationException("오디오 연결이 중단되었습니다.");
                    if (session is null ||
                        !StringComparer.Ordinal.Equals(session.DeviceId, candidate.ID))
                    {
                        CloseSession(run);
                        if (!IsCurrent(run)) break;
                        OpenSession(run, candidate);
                    }

                    session = Volatile.Read(ref run.Session);
                    if (session is not null)
                    {
                        if (Volatile.Read(ref session.Ended) != 0)
                        {
                            var reason = session.Error is null ? "오디오 연결이 중단되었습니다." : DescribeError(session.Error);
                            CloseSession(run);
                            SetSnapshot(run, CaptureSnapshot.Stopped);
                            Publish(run, reason + " · 자동 재연결 중");
                        }
                        else
                        {
                            var capturing = session.Recorder.CaptureState == CaptureState.Capturing;
                            SetSnapshot(run, new CaptureSnapshot(session.DeviceId, session.DeviceName, session.Decoder.Description, capturing));
                            var lastSignal = Interlocked.Read(ref session.LastSignalTicks);
                            var receiving = lastSignal != 0 && DateTime.UtcNow.Ticks - lastSignal < TimeSpan.TicksPerSecond * 2;
                            Publish(run, $"{(!capturing ? "출력 장치 응답 대기" : receiving ? "PC 소리 분석 중" : "재생 신호 대기")} · {session.Decoder.ChannelNote}");
                        }
                    }
                }
                catch (Exception error)
                {
                    CloseSession(run);
                    SetSnapshot(run, CaptureSnapshot.Stopped);
                    Publish(run, DescribeError(error) + " · 자동 재연결 중");
                }
                // A selected missing device stays selected; only null follows the default.
                if (!IsCurrent(run) || WaitHandle.WaitAny(waitHandles, 1000) == 0)
                    break;
            }
        }
        finally
        {
            try { CloseSession(run); }
            finally { run.Finish(); }
        }
    }

    private void OpenSession(CaptureRun run, MMDevice device)
    {
        // Deliberately retain the endpoint's mix format and channel layout. No microphone,
        // output stream, endpoint volume changes, or resampling are used.
        using var enumerator = new MMDeviceEnumerator();
        var captureDevice = enumerator.GetDevice(device.ID);
        WasapiRecorder? recorder = null;
        CaptureSession? session = null;
        try
        {
            recorder = new WasapiRecorderBuilder()
                .WithDevice(captureDevice)
                .WithSharedMode()
                .WithLoopbackCapture()
                .WithEventSync()
                .WithBufferLength(20)
                .WithMmcssThreadPriority("Audio")
                .Build();
            session = new CaptureSession(captureDevice, device.ID, device.FriendlyName, recorder, new StereoSampleDecoder(recorder.WaveFormat));
            var capturedSession = session;
            session.DataHandler = (buffer, flags, _, _) => ReceivePacket(run, capturedSession, buffer, flags);
            session.StoppedHandler = (_, args) =>
            {
                capturedSession.Error = args.Exception;
                Volatile.Write(ref capturedSession.Ended, 1);
                if (IsCurrent(run)) run.Wake();
            };
            recorder.DataAvailable += session.DataHandler;
            recorder.RecordingStopped += session.StoppedHandler;
            Volatile.Write(ref run.Session, session);
            if (IsCurrent(run)) StreamReset?.Invoke();
            recorder.StartRecording();

            // Do not dispose a recorder while its capture thread is still starting: the
            // recorder marks itself Capturing after StartRecording has returned.
            SpinWait.SpinUntil(() => recorder.CaptureState != CaptureState.Starting, 1000);

            SetSnapshot(run, new CaptureSnapshot(session.DeviceId, session.DeviceName,
                session.Decoder.Description, recorder.CaptureState == CaptureState.Capturing));
        }
        catch
        {
            if (session is not null)
            {
                Interlocked.CompareExchange(ref run.Session, null, session);
                session.Recorder.DataAvailable -= session.DataHandler;
                session.Recorder.RecordingStopped -= session.StoppedHandler;
            }
            recorder?.Dispose();
            captureDevice.Dispose();
            throw;
        }
    }

    private void ReceivePacket(CaptureRun run, CaptureSession session, ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags)
    {
        if (!IsCurrent(run) || !ReferenceEquals(Volatile.Read(ref run.Session), session) || Volatile.Read(ref session.Ended) != 0)
            return;
        try
        {
            var samples = session.Decoder.Decode(buffer, (flags & AudioClientBufferFlags.Silent) != 0, out var hasSignal);
            if (samples.Length == 0) return;
            Interlocked.Add(ref run.TotalFrames, samples.Length / 2);
            if (hasSignal)
                Interlocked.Exchange(ref session.LastSignalTicks, DateTime.UtcNow.Ticks);
            if (IsCurrent(run) && ReferenceEquals(Volatile.Read(ref run.Session), session))
                StereoSamples?.Invoke(samples, session.Decoder.SampleRate);
        }
        catch (Exception error)
        {
            session.Error = error;
            Volatile.Write(ref session.Ended, 1);
            if (IsCurrent(run)) run.Wake();
        }
    }

    private static void CloseSession(CaptureRun run)
    {
        var session = Interlocked.Exchange(ref run.Session, null);
        if (session is null) return;
        session.Recorder.DataAvailable -= session.DataHandler;
        session.Recorder.RecordingStopped -= session.StoppedHandler;
        if (session.Recorder.CaptureState == CaptureState.Starting)
        {
            // If the driver's native Start is slow, calling Stop immediately can be
            // overwritten by NAudio's subsequent Capturing assignment. Detach callbacks
            // now and dispose only after startup resolves, without blocking the UI.
            _ = DisposeAfterStartupAsync(session);
            return;
        }
        DisposeSession(session);
    }

    private static async Task DisposeAfterStartupAsync(CaptureSession session)
    {
        while (session.Recorder.CaptureState == CaptureState.Starting)
            await Task.Delay(100).ConfigureAwait(false);
        DisposeSession(session);
    }

    private static void DisposeSession(CaptureSession session)
    {
        try { session.Recorder.Dispose(); }
        catch (Exception) { /* A removed endpoint can invalidate teardown. Callbacks are detached. */ }
        finally
        {
            try { session.Device.Dispose(); }
            catch (Exception) { }
        }
    }

    private void SetSnapshot(CaptureRun run, CaptureSnapshot snapshot)
    {
        if (IsCurrent(run)) Volatile.Write(ref _snapshot, snapshot);
    }

    private void Publish(CaptureRun run, string message)
    {
        if (!IsCurrent(run) || run.LastStatus == message) return;
        run.LastStatus = message;
        Notify(message);
    }

    private void Notify(string message)
    {
        // A status consumer must not be able to terminate the endpoint recovery worker.
        try { StatusChanged?.Invoke(message); }
        catch (Exception) { }
    }

    private static string DescribeError(Exception error) => error switch
    {
        NotSupportedException => error.Message,
        InvalidOperationException => error.Message,
        COMException com when com.HResult == unchecked((int)0x80070490) => "선택한 출력 장치를 찾을 수 없습니다",
        COMException com when com.HResult == unchecked((int)0x88890004) => "출력 장치 연결이 변경되었습니다",
        COMException com when com.HResult == unchecked((int)0x8889000A) => "출력 장치를 다른 프로그램이 독점 사용 중입니다",
        COMException com when com.HResult == unchecked((int)0x88890008) => "출력 장치의 오디오 형식을 지원하지 않습니다",
        COMException com => $"오디오 캡처를 시작할 수 없습니다 (0x{com.HResult:X8})",
        _ => $"오디오 캡처 오류: {error.Message}"
    };

    private sealed record CaptureSnapshot(string? DeviceId, string DeviceName, string FormatDescription, bool IsRunning)
    {
        internal static readonly CaptureSnapshot Stopped = new(null, "출력 장치 대기", "", false);
    }

    private sealed class CaptureRun(string? deviceId)
    {
        private readonly object _signalGate = new();
        private bool _finished;
        internal readonly string? DeviceId = deviceId;
        internal readonly ManualResetEvent StopRequested = new(false);
        internal readonly AutoResetEvent WakeRequested = new(false);
        internal Thread? Worker;
        internal CaptureSession? Session;
        internal long TotalFrames;
        internal string? LastStatus;

        internal void RequestStop()
        {
            lock (_signalGate)
            {
                if (_finished) return;
                StopRequested.Set();
                WakeRequested.Set();
            }
        }

        internal void Wake()
        {
            lock (_signalGate)
                if (!_finished) WakeRequested.Set();
        }

        internal void Finish()
        {
            lock (_signalGate)
            {
                _finished = true;
                StopRequested.Dispose();
                WakeRequested.Dispose();
            }
        }
    }

    private sealed class CaptureSession(MMDevice device, string deviceId, string deviceName, WasapiRecorder recorder, StereoSampleDecoder decoder)
    {
        internal readonly MMDevice Device = device;
        internal readonly string DeviceId = deviceId;
        internal readonly string DeviceName = deviceName;
        internal readonly WasapiRecorder Recorder = recorder;
        internal readonly StereoSampleDecoder Decoder = decoder;
        internal CaptureDataAvailableHandler? DataHandler;
        internal EventHandler<StoppedEventArgs>? StoppedHandler;
        internal Exception? Error;
        internal int Ended;
        internal long LastSignalTicks;
    }
}

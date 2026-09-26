using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace BlueSpectrum.UI;

// Deadline-based clock: no system-wide timer resolution change and no UI callback backlog.
public sealed class FrameClock : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly ManualResetEvent stop = new(false);
    private Thread? worker;
    private long intervalTicks = Stopwatch.Frequency / 60;
    private int pending;
    private volatile bool running;
    public event EventHandler? Tick;
    public TimeSpan Interval
    {
        get => TimeSpan.FromSeconds((double)Interlocked.Read(ref intervalTicks) / Stopwatch.Frequency);
        set => Interlocked.Exchange(ref intervalTicks, Math.Max(1, (long)(value.TotalSeconds * Stopwatch.Frequency)));
    }
    public FrameClock(Dispatcher dispatcher) => this.dispatcher = dispatcher;
    public void Start()
    {
        if (worker != null) return;
        var handle = CreateWaitableTimerExW(0, null, 2, 0x1F0003);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            handle = CreateWaitableTimerExW(0, null, 0, 0x1F0003);
        }
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        stop.Reset(); running = true;
        worker = new Thread(() => Run(handle)) { IsBackground = true, Name = "Spectrum frame clock" };
        worker.Start();
    }
    public void Stop()
    {
        running = false; stop.Set(); worker?.Join(); worker = null;
    }
    private void Run(SafeWaitHandle handle)
    {
        using var timer = new TimerWaitHandle(handle);
        WaitHandle[] waits = [stop, timer];
        long next = Stopwatch.GetTimestamp();
        while (!stop.WaitOne(0))
        {
            long interval = Interlocked.Read(ref intervalTicks);
            next += interval;
            long now = Stopwatch.GetTimestamp();
            if (next <= now) next = now + interval;
            long due = -Math.Max(1, (long)((next - now) * (10000000.0 / Stopwatch.Frequency)));
            if (!SetWaitableTimer(handle, ref due, 0, 0, 0, false) || WaitHandle.WaitAny(waits) == 0) break;
            if (dispatcher.HasShutdownStarted || Interlocked.CompareExchange(ref pending, 1, 0) != 0) continue;
            dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                try { if (running) Tick?.Invoke(this, EventArgs.Empty); }
                finally { Volatile.Write(ref pending, 0); }
            }));
        }
    }
    public void Dispose() { Stop(); stop.Dispose(); }
    private sealed class TimerWaitHandle : WaitHandle
    {
        public TimerWaitHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, nint callback, nint argument, bool resume);
}

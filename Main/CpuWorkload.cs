using System.Diagnostics;

internal sealed class CpuWorkload(ILogger logger) : IDisposable
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(100);
    private readonly object _state = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private long _lastClick = Stopwatch.GetTimestamp();
    private int _target = 10;
    private int _remaining;

    public int Workers { get; } = Environment.ProcessorCount;
    public int Target => Volatile.Read(ref _target);
    public static TimeSpan IdleLimit { get; } = TimeSpan.FromSeconds(60);
    public TimeSpan IdleRemaining => IdleLimit - Stopwatch.GetElapsedTime(Volatile.Read(ref _lastClick));
    public bool Stopping => _stop.IsCancellationRequested;
    public Task Completed => _done.Task;

    public void Start()
    {
        _remaining = Workers;
        var started = 0;
        try
        {
            for (; started < Workers; started++)
                new Thread(Work) { IsBackground = true, Name = $"PerfLab CPU {started + 1}" }.Start(started);
        }
        catch
        {
            Cancel();
            if (Interlocked.Add(ref _remaining, started - Workers) == 0) _done.TrySetResult();
            throw;
        }
    }

    public bool TryAddStep()
    {
        lock (_state)
        {
            if (!CanContinue()) return false;
            Volatile.Write(ref _lastClick, Stopwatch.GetTimestamp());
            // All workers use the same duty percentage: +10 points across Main’s available CPUs, not per worker added together.
            Volatile.Write(ref _target, Math.Min(100, Target + 10));
            return true;
        }
    }

    public void Cancel()
    {
        lock (_state) _stop.Cancel();
    }

    private bool CanContinue()
    {
        if (_stop.IsCancellationRequested) return false;
        if (IdleRemaining > TimeSpan.Zero) return true;
        lock (_state)
        {
            if (_stop.IsCancellationRequested) return false;
            if (IdleRemaining > TimeSpan.Zero) return true;
            _stop.Cancel();
            return false;
        }
    }

    public void Dispose() => _stop.Dispose();

    private void Work(object? state)
    {
        try
        {
            var stop = _stop.Token;
            // Stagger windows so the workers do not all sleep or wake at once.
            if (stop.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(Window.TotalMilliseconds * (int)state! / Workers))) return;
            while (CanContinue())
            {
                var start = _elapsed.Elapsed;
                // Spin for the target fraction of each window, then wait for the rest.
                var duty = TimeSpan.FromTicks(Window.Ticks * Target / 100);
                while (_elapsed.Elapsed - start < duty && CanContinue())
                    Thread.SpinWait(32);
                var wait = Window - (_elapsed.Elapsed - start);
                if (wait > TimeSpan.Zero && stop.WaitHandle.WaitOne(wait)) return;
            }
        }
        catch (Exception exception)
        {
            Cancel();
            logger.LogWarning(exception, "A lab CPU worker stopped early.");
        }
        finally
        {
            if (Interlocked.Decrement(ref _remaining) == 0) _done.TrySetResult();
        }
    }
}

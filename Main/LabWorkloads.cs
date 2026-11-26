using System.Diagnostics;

public sealed class LabWorkloads(ILogger<LabWorkloads> logger) : BackgroundService
{
    private readonly object _gate = new();
    private readonly List<IMemoryAllocation> _retained = new((int)(MemoryAllocation.CapBytes / MemoryAllocation.StepBytes));
    private readonly Process _process = Process.GetCurrentProcess();
    private CpuWorkload? _cpu;
    private PendingAllocation? _memory;
    private long _lastMemoryClick;
    private long _managedBytes, _nativeBytes;
    private string? _memoryNotice;
    private bool _shuttingDown, _disposed;
    private Task _cleanup = Task.CompletedTask;
    private long _sampleTime;
    private TimeSpan _sampleCpu;
    private double? _measuredCpu;

    internal WorkloadStart AddCpuStep()
    {
        lock (_gate)
        {
            CleanExpired();
            if (_shuttingDown || !_cleanup.IsCompleted || _cpu?.Stopping == true) return WorkloadStart.Busy;
            if (_cpu is not null) return _cpu.TryAddStep() ? WorkloadStart.Accepted : WorkloadStart.Busy;
            _cpu = new CpuWorkload(logger);
            _cpu.Start();
            return WorkloadStart.Accepted;
        }
    }

    internal WorkloadStart AddMemoryStep(bool native)
    {
        lock (_gate)
        {
            CleanExpired();
            if (_shuttingDown || !_cleanup.IsCompleted || _memory is not null) return WorkloadStart.Busy;
            if (!MemoryAllocation.Supported) return WorkloadStart.Unsupported;
            // The cap is shared by both memory types. Headroom is a separate check on what Windows can spare.
            if (_managedBytes + _nativeBytes + MemoryAllocation.StepBytes > MemoryAllocation.CapBytes) return WorkloadStart.AtCapacity;
            if (!MemoryAllocation.HasHeadroom(MemoryAllocation.StepBytes)) return WorkloadStart.InsufficientMemory;
            var pending = new PendingAllocation();
            _memory = pending;
            _memoryNotice = null;
            try
            {
                new Thread(() => Allocate(pending, native)) { IsBackground = true, Name = "PerfLab memory allocation" }.Start();
                _lastMemoryClick = Stopwatch.GetTimestamp();
            }
            catch
            {
                _memory = null;
                pending.Stop.Dispose();
                throw;
            }
            return WorkloadStart.Accepted;
        }
    }

    private void Allocate(PendingAllocation pending, bool native)
    {
        IMemoryAllocation? block = null;
        var token = pending.Stop.Token;
        string? failure = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!MemoryAllocation.HasHeadroom(MemoryAllocation.StepBytes)) throw new MemoryHeadroomException();
            block = native ? new NativeAllocation() : new ManagedAllocation();
            block.Touch(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (MemoryHeadroomException) { failure = "Allocation not retained: Windows could not confirm the 2 GiB physical and commit reserve."; }
        catch (Exception exception)
        {
            failure = "Allocation not retained: allocation failed. Check the Main logs.";
            logger.LogWarning(exception, "Lab memory allocation failed.");
        }
        finally
        {
            lock (_gate)
            {
                if (block is not null && failure is null && !token.IsCancellationRequested && MemoryIdleRemaining() > TimeSpan.Zero)
                {
                    // Transfer ownership to the retained list. Otherwise this worker disposes the block below.
                    _retained.Add(block);
                    block = null;
                    if (native) _nativeBytes += MemoryAllocation.StepBytes;
                    else _managedBytes += MemoryAllocation.StepBytes;
                    _memoryNotice = "Allocation complete: retained an additional 500,000,000 bytes.";
                }
                else if (failure is not null && !token.IsCancellationRequested) _memoryNotice = failure;
                block?.Dispose();
                _memory = null;
                pending.Stop.Dispose();
                pending.Done.SetResult();
            }
        }
    }

    public LabWorkloadStatus GetStatus()
    {
        lock (_gate)
        {
            CleanExpired();
            SampleCpu();
            var cpuRemaining = _cpu is { Stopping: false } ? _cpu.IdleRemaining.TotalSeconds : 0;
            var memoryRemaining = _retained.Count > 0 || _memory is not null ? MemoryIdleRemaining().TotalSeconds : 0;
            return new(Environment.ProcessId, _cpu?.Target ?? 0, _cpu?.Workers ?? 0, _cpu?.Stopping ?? false,
                Math.Clamp(cpuRemaining, 0, CpuWorkload.IdleLimit.TotalSeconds), _measuredCpu,
                _managedBytes, _nativeBytes, MemoryAllocation.CapBytes, _memory is not null,
                Math.Clamp(memoryRemaining, 0, MemoryAllocation.IdleLimit.TotalSeconds), !_cleanup.IsCompleted, _memoryNotice);
        }
    }

    private void SampleCpu()
    {
        var now = Stopwatch.GetTimestamp();
        if (_sampleTime != 0 && Stopwatch.GetElapsedTime(_sampleTime, now) < TimeSpan.FromMilliseconds(750)) return;
        try
        {
            var total = _process.TotalProcessorTime;
            // Divide used CPU time by elapsed time across all available CPUs to get a process-wide percentage.
            if (_sampleTime != 0)
                _measuredCpu = Math.Max(0, (total - _sampleCpu).TotalSeconds * 100
                    / (Stopwatch.GetElapsedTime(_sampleTime, now).TotalSeconds * Environment.ProcessorCount));
            _sampleCpu = total;
            _sampleTime = now;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            _measuredCpu = null;
            _sampleTime = 0;
        }
    }

    public Task StopWorkloadsAsync()
    {
        lock (_gate)
        {
            if (!_cleanup.IsCompleted) return _cleanup;
            _cpu?.Cancel();
            _memory?.Stop.Cancel();
            ReleaseMemory("Retained allocations released by Stop or shutdown.");
            _cleanup = Task.WhenAll(_cpu?.Completed ?? Task.CompletedTask, _memory?.Done.Task ?? Task.CompletedTask);
            return _cleanup;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            lock (_gate) CleanExpired();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate) _shuttingDown = true;
        try
        {
            await StopWorkloadsAsync().WaitAsync(cancellationToken);
            lock (_gate) CleanExpired();
        }
        finally { await base.StopAsync(cancellationToken); }
    }

    public override void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = _shuttingDown = true;
            _ = StopWorkloadsAsync();
            CleanExpired();
            _process.Dispose();
        }
        base.Dispose();
    }

    private void CleanExpired()
    {
        if (_cpu is { } cpu)
        {
            if (cpu.IdleRemaining <= TimeSpan.Zero && !cpu.Stopping) cpu.Cancel();
            if (cpu.Completed.IsCompleted)
            {
                cpu.Dispose();
                _cpu = null;
            }
        }
        if ((_retained.Count > 0 || _memory is not null) && MemoryIdleRemaining() <= TimeSpan.Zero)
        {
            _memory?.Stop.Cancel();
            ReleaseMemory("Retained allocations released after two minutes of memory inactivity.");
        }
    }

    private TimeSpan MemoryIdleRemaining() => MemoryAllocation.IdleLimit - Stopwatch.GetElapsedTime(_lastMemoryClick);

    // Stop, shutdown, and idle expiry all release retained blocks through the same path.
    private void ReleaseMemory(string notice)
    {
        foreach (var block in _retained) block.Dispose();
        _retained.Clear();
        _managedBytes = _nativeBytes = 0;
        _memoryNotice = notice;
    }

    private sealed class PendingAllocation
    {
        public CancellationTokenSource Stop { get; } = new();
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

internal enum WorkloadStart { Accepted, Busy, Unsupported, InsufficientMemory, AtCapacity }
public sealed record LabWorkloadStatus(int Pid, int TargetCpuPercent, int CpuWorkers, bool CpuStopping,
    double CpuSecondsRemaining, double? MeasuredProcessCpuPercent, long ManagedBytes, long NativeBytes, long MemoryCapBytes,
    bool MemoryAllocating, double MemorySecondsRemaining, bool Stopping, string? MemoryNotice)
{
    public long RetainedBytes => ManagedBytes + NativeBytes;
}

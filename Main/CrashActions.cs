using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public sealed partial class CrashActions(LabAccess access, ILogger<CrashActions> logger)
{
    private int _queued;

    internal bool IsQueued => Volatile.Read(ref _queued) != 0;

    internal static bool IsKnown(string? kind) => kind is "thread" or "stack" or "native-failfast";

    internal async Task<int> QueueAsync(HttpContext context, string? kind)
    {
        // Unlike a request exception, process termination requires separate opt-in on an isolated Windows lab host,
        // plus a valid POST with explicit confirmation. Only one request can queue termination.
        if (!access.CrashAllowed(context) || !IsKnown(kind)) return 404;
        if (context.Request.Headers["Sec-Fetch-Site"].Any(value => value != "same-origin" && value != "none")) return 400;
        if (!context.Request.HasFormContentType || (await context.Request.ReadFormAsync(context.RequestAborted))["confirmation"]
            != "TERMINATE LAB PROCESS") return 400;
        if (Interlocked.Exchange(ref _queued, 1) != 0) return 409;

        logger.LogWarning("Queuing intentional lab process termination: {Kind}; PID {Pid}", kind, Environment.ProcessId);
        // HTTP 202 acknowledges the queue, not the crash. The action runs after the response,
        // outside the request exception handler, and is intended to end the Main process.
        context.Response.OnCompleted(() =>
        {
            new Thread(() => Terminate(kind!)) { IsBackground = true, Name = $"PerfLab crash: {kind}" }.Start();
            return Task.CompletedTask;
        });
        return 202;
    }

    private static void Terminate(string kind)
    {
        switch (kind)
        {
            case "thread":
                throw new InvalidOperationException("Intentional unhandled exception on a dedicated lab thread.");
            case "stack":
                Overflow(1);
                break;
            case "native-failfast":
                RaiseFailFastException(0, 0, 0);
                Environment.FailFast("The Windows fail-fast call unexpectedly returned.");
                break;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static int Overflow(int depth)
    {
        // Consume a real stack frame and do work after the call so it cannot be a tail call.
        Span<int> frame = stackalloc int[128];
        frame[depth & 127] = depth;
        if (depth == int.MaxValue) return frame[depth & 127];
        var next = Overflow(depth + 1);
        return next + frame[depth & 127];
    }

    [LibraryImport("kernel32.dll")]
    private static partial void RaiseFailFastException(nint exceptionRecord, nint contextRecord, uint flags);
}

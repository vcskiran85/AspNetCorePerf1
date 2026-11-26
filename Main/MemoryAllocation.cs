using System.Runtime.InteropServices;

internal interface IMemoryAllocation : IDisposable
{
    void Touch(CancellationToken token);
}

internal static class MemoryAllocation
{
    public const int StepBytes = 500_000_000;
    public const long CapBytes = 10_000_000_000;
    public static TimeSpan IdleLimit { get; } = TimeSpan.FromMinutes(2);
    private const ulong Reserve = 2UL * 1024 * 1024 * 1024;

    public static bool Supported => OperatingSystem.IsWindows() && Environment.Is64BitProcess;

    public static bool HasHeadroom(ulong additionalBytes) => HasHeadroom(additionalBytes, additionalBytes);

    private static bool HasHeadroom(ulong additionalPhysical, ulong additionalCommit)
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        var size = (uint)Marshal.SizeOf<PerformanceInformation>();
        if (!GlobalMemoryStatusEx(ref status) || !GetPerformanceInfo(out var performance, size)) return false;
        // Keep 2 GiB free in both physical RAM and commit capacity (memory Windows can promise to back).
        ulong requiredCommit = Reserve + additionalCommit;
        ulong pageSize = (ulong)performance.PageSize;
        return status.AvailablePhysical >= Reserve + additionalPhysical && status.AvailableCommit >= requiredCommit
            && pageSize > 0 && performance.CommitLimit >= performance.CommitTotal
            && (ulong)(performance.CommitLimit - performance.CommitTotal) >= (requiredCommit + pageSize - 1) / pageSize;
    }

    public static void Touch(Span<byte> allocation, CancellationToken token)
    {
        // Write in batches to bring pages into use, checking cancellation and RAM headroom between batches.
        const int batch = 4 * 1024 * 1024;
        for (var offset = 0; offset < allocation.Length; offset += batch)
        {
            token.ThrowIfCancellationRequested();
            var count = Math.Min(batch, allocation.Length - offset);
            if (!HasHeadroom((ulong)count, 0)) throw new MemoryHeadroomException();
            allocation.Slice(offset, count).Fill(1);
        }
        if (!HasHeadroom(0)) throw new MemoryHeadroomException();
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);

    [DllImport("psapi.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(out PerformanceInformation information, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, CommitLimit, AvailableCommit, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable,
            SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }
}

internal sealed class MemoryHeadroomException : Exception { }

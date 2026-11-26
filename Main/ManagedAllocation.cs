internal sealed class ManagedAllocation : IMemoryAllocation
{
    private byte[]? _bytes = new byte[MemoryAllocation.StepBytes];

    public void Touch(CancellationToken token) => MemoryAllocation.Touch(_bytes.AsSpan(), token);
    // Drop the reference so the garbage collector can reclaim the array later.
    public void Dispose() => _bytes = null;
}

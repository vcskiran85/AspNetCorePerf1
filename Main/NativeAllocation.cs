using System.Runtime.InteropServices;

internal unsafe sealed class NativeAllocation : IMemoryAllocation
{
    private void* _pointer = NativeMemory.Alloc((nuint)MemoryAllocation.StepBytes);

    public void Touch(CancellationToken token) =>
        MemoryAllocation.Touch(new Span<byte>(_pointer, MemoryAllocation.StepBytes), token);

    // This buffer is outside the managed heap, so its owner must free it explicitly.
    public void Dispose()
    {
        NativeMemory.Free(_pointer);
        _pointer = null;
    }
}

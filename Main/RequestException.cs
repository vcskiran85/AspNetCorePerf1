internal static class RequestException
{
    public static void Throw() => throw new LabRequestException();
}

internal sealed class LabRequestException() : InvalidOperationException("Intentional Main request exception.") { }

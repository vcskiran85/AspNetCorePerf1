using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;

internal sealed record ReportResult(object? Result, int Passes, long HttpRequests = 0);
internal sealed record ComparisonOutcome(int Status, string? Error, double Seconds = 0, ReportResult? Report = null);

public sealed class ReportComparison(ILogger<ReportComparison> logger)
{
    private readonly SemaphoreSlim _comparisons = new(1, 1);

    internal async Task<ComparisonOutcome> RunAsync(Func<CancellationToken, Task<ReportResult>> run,
        bool useDeadline, CancellationToken cancellationToken)
    {
        if (!await _comparisons.WaitAsync(0, cancellationToken))
            return new(409, "Only one comparison can run at a time. Try again after it finishes.");
        try
        {
            // Keep request cancellation and, when enabled, add a 20-second deadline for the whole comparison.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (useDeadline) deadline.CancelAfter(TimeSpan.FromSeconds(20));
            var watch = Stopwatch.StartNew();
            var report = await run(deadline.Token);
            return new(200, null, watch.Elapsed.TotalSeconds, report);
        }
        catch (Exception exception) when (exception is SqlException or LocalDbUnavailableException)
        {
            logger.LogWarning(exception, "The SQL comparison failed.");
            return new(503, "SQL comparison unavailable. For LocalDB, run Setup-LocalDB.sql as your Windows user. Otherwise check the configured sample SQL database and the Main logs.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(504, "The request or its dependency did not finish within its deadline. Check the dependency and try again.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException)
        {
            logger.LogWarning(exception, "The WebApi comparison failed.");
            return new(502, "WebApi comparison unavailable. Start WebApi on port 5101 or check WebApi:BaseUrl and its logs.");
        }
        finally { _comparisons.Release(); }
    }

    internal static async Task<ReportResult> RunPassesAsync(Func<CancellationToken, Task<object?>> run,
        bool repeat, CancellationToken cancellationToken)
    {
        // Run at least once. The five-second repeat window is checked between completed passes, not mid-pass.
        var watch = Stopwatch.StartNew();
        var passes = 0;
        object? result;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            result = await run(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            passes++;
        } while (repeat && result is not null && watch.Elapsed < TimeSpan.FromSeconds(5));
        return new(result, passes);
    }
}

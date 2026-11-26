using System.Net.Http.Json;

internal static class HttpReport
{
    public static async Task<ReportResult> RunAsync(IHttpClientFactory clients, bool slow, bool repeat, CancellationToken token)
    {
        using var ordinary = clients.CreateClient("Reports");
        // Repeat only against a loopback API. The repeat client bypasses proxies and does not follow redirects.
        repeat = repeat && LabAccess.IsLoopbackHost(ordinary.BaseAddress!.Host);
        using var direct = repeat ? clients.CreateClient("LocalRepeatedReports") : null;
        var report = await ReportComparison.RunPassesAsync(ct => CallWebApiAsync(direct ?? ordinary, slow, ct), repeat, token);
        return report with { HttpRequests = (long)report.Passes * (slow ? ReportPage.PageCount : 1) };
    }

    private static async Task<object?> CallWebApiAsync(HttpClient client, bool slow, CancellationToken cancellationToken)
    {
        // Ask for totals in one request, or fetch twelve detail pages and calculate the same totals in Main.
        if (!slow)
        {
            var summary = await client.GetFromJsonAsync<ReportSummary>("api/report", cancellationToken)
                ?? throw new InvalidDataException("WebApi returned no summary.");
            return new { Requests = 1, summary.Count, summary.AmountCents };
        }
        var count = 0;
        long amountCents = 0;
        for (var page = 1; page <= ReportPage.PageCount; page++)
        {
            var details = await client.GetFromJsonAsync<ReportPage>($"api/report?page={page}", cancellationToken);
            var rows = details?.Rows ?? throw new InvalidDataException("WebApi returned no detail page.");
            count += rows.Length;
            amountCents += rows.Sum(row => (long)row.AmountCents);
        }
        return new { Requests = ReportPage.PageCount, Count = count, AmountCents = amountCents };
    }
}

internal sealed record ReportSummary(int Count, long AmountCents);
internal sealed record ReportPage(ReportRow[] Rows)
{
    public const int PageCount = 12;
}
internal sealed record ReportRow(int AmountCents);

using System.Net;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
// This separate app serves Main’s HTTP report. Build the synthetic data only on the first report request.
var reports = new Lazy<ReportData>();
var concurrentReports = new SemaphoreSlim(2, 2);

app.MapGet("/", () => Results.Ok(new { app = "WebApi", health = "/health", report = "/api/report" }));
app.MapGet("/health", () => Results.Ok(new { app = "WebApi", pid = Environment.ProcessId }));
app.MapGet("/api/report", async (int? page, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var localLab = !IsAzureAppService() && builder.Configuration.GetValue<bool>("Lab:Enabled")
        && (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Lab:IsolatedLocalHosting"))
        && !context.Request.Headers.ContainsKey("Forwarded")
        && !context.Request.Headers.ContainsKey("X-Forwarded-For")
        && !context.Request.Headers.ContainsKey("X-Forwarded-Host")
        && IsLoopback(context.Connection.RemoteIpAddress) && IsLoopback(context.Connection.LocalIpAddress)
        && IsLoopbackHost(context.Request.Host.Host);
    if (!localLab && !builder.Configuration.GetValue<bool>("Lab:EnablePrivateHostedReports")) return Results.NotFound();
    if (page is < 1 or > ReportData.PageCount) return Results.BadRequest($"page must be 1 through {ReportData.PageCount}.");
    if (!await concurrentReports.WaitAsync(0, context.RequestAborted)) return Results.StatusCode(429);
    try
    {
        context.RequestAborted.ThrowIfCancellationRequested();
        var data = reports.Value;
        context.RequestAborted.ThrowIfCancellationRequested();
        // A page number returns detail rows; omitting it returns totals calculated here in WebApi.
        IResult result = page is int index ? Results.Ok(new { rows = data.GetPage(index) }) : Results.Ok(data.GetSummary());
        // Write the response before releasing the concurrency slot so it also covers JSON serialization.
        await result.ExecuteAsync(context);
        return Results.Empty;
    }
    finally { concurrentReports.Release(); }
});

app.Run();

static bool IsAzureAppService() => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SKU"));
static bool IsLoopback(IPAddress? address) => address is not null
    && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
static bool IsLoopbackHost(string host) => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
    || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IsLoopback(address));

internal sealed class ReportData
{
    public const int PageCount = 12;
    private const int PageSize = 512;
    private readonly Sale[] _rows = Enumerable.Range(1, PageCount * PageSize)
        .Select(id => new Sale(id, 1 + id % 120, 100 + id * 37 % 100000,
            $"Synthetic receipt {id:D6}; " + new string((char)('A' + id % 26), 380)))
        .ToArray();

    public ArraySegment<Sale> GetPage(int page) => new(_rows, (page - 1) * PageSize, PageSize);
    public Summary GetSummary() => new(_rows.Length, _rows.Sum(row => (long)row.AmountCents));
}

internal sealed record Sale(int EventId, int CustomerId, int AmountCents, string Payload);
internal sealed record Summary(int Count, long AmountCents);

using Microsoft.AspNetCore.Mvc;

namespace PerfLab.Pages;

[LabOnly(reports: true)]
public sealed class HttpReportModel(LabAccess access, ReportComparison comparison, IHttpClientFactory clients) : ReportPageModel
{
    public Task<IActionResult> OnGetAsync([FromRoute] string? mode, CancellationToken token) => RunAsync(mode, token);
    public Task<IActionResult> OnPostAsync([FromRoute] string? mode, CancellationToken token) => RunAsync(mode, token);

    private Task<IActionResult> RunAsync(string? mode, CancellationToken token) => CompareAsync(mode, access, comparison,
        (slow, repeat, ct) => HttpReport.RunAsync(clients, slow, repeat, ct), alwaysUseDeadline: true, token);
}

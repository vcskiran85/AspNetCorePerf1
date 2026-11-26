using Microsoft.AspNetCore.Mvc;

namespace PerfLab.Pages;

[LabOnly(reports: true)]
public sealed class AppReportModel(LabAccess access, ReportComparison comparison) : ReportPageModel
{
    public Task<IActionResult> OnGetAsync([FromRoute] string? mode, CancellationToken token) => RunAsync(mode, token);
    public Task<IActionResult> OnPostAsync([FromRoute] string? mode, CancellationToken token) => RunAsync(mode, token);

    private Task<IActionResult> RunAsync(string? mode, CancellationToken token) => CompareAsync(mode, access, comparison,
        (slow, repeat, ct) => ReportComparison.RunPassesAsync(passToken => Task.FromResult<object?>(AppReport.Calculate(slow, passToken)), repeat, ct),
        alwaysUseDeadline: false, token);
}

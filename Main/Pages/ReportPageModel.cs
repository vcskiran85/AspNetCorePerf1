using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PerfLab.Pages;

public abstract class ReportPageModel : PageModel
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public bool Slow { get; private set; }
    public string Mode => Slow ? "slow" : "fast";
    private ComparisonOutcome? _outcome;
    public string? Error => _outcome?.Error;
    public double ElapsedSeconds => _outcome?.Seconds ?? 0;
    public int Passes => _outcome?.Report?.Passes ?? 0;
    public long HttpRequests => _outcome?.Report?.HttpRequests ?? 0;
    public string? ResultJson => _outcome?.Report?.Result is { } result ? JsonSerializer.Serialize(result, Indented) : null;

    internal async Task<IActionResult> CompareAsync(string? mode, LabAccess access, ReportComparison comparison,
        Func<bool, bool, CancellationToken, Task<ReportResult>> run, bool alwaysUseDeadline, CancellationToken token)
    {
        Slow = string.Equals(mode, "Slow", StringComparison.OrdinalIgnoreCase);
        if (!Slow && !string.Equals(mode, "Fast", StringComparison.OrdinalIgnoreCase)) return NotFound();
        if (HttpMethods.IsHead(Request.Method)) return new EmptyResult();
        var local = access.LabAllowed(HttpContext);
        if (local && HttpMethods.IsGet(Request.Method)
            && (Request.Headers["Sec-Fetch-Site"].Any(value => value != "same-origin" && value != "none")
                || Request.Headers["Sec-Purpose"].Any(value => value?.Contains("prefetch", StringComparison.OrdinalIgnoreCase) == true)
                || Request.Headers["Purpose"].Any(value => value?.Equals("prefetch", StringComparison.OrdinalIgnoreCase) == true)))
            return StatusCode(403);

        // Only the local slow mode requests repeats. HTTP also uses the overall deadline outside the local lab.
        var outcome = await comparison.RunAsync(ct => run(Slow, local && Slow, ct), local || alwaysUseDeadline, token);
        _outcome = outcome;
        Response.StatusCode = outcome.Status;
        return Page();
    }
}

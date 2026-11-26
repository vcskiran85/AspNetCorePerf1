using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PerfLab.Pages;

[LabOnly]
public sealed class CrashModel(LabAccess access, CrashActions crash) : PageModel
{
    public bool Enabled => access.CrashAllowed(HttpContext);
    public bool Queued => crash.IsQueued;
    public string? Outcome { get; private set; }

    public IActionResult OnGet([FromRoute] string? kind) => kind is null ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync([FromRoute] string? kind)
    {
        Response.StatusCode = await crash.QueueAsync(HttpContext, kind);
        if (Response.StatusCode == 404) return NotFound();
        Outcome = Response.StatusCode switch
        {
            202 => $"Termination queued: {kind}; PID {Environment.ProcessId}. Use Health to check the PID after restarting Main.",
            409 => "Termination is already queued for this worker.",
            _ => "Submit the form from this page with the exact confirmation phrase. No crash was queued."
        };
        return Page();
    }
}

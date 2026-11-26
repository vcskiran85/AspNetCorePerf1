using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PerfLab.Pages;

public abstract class WorkloadStepModel : PageModel
{
    public string? Outcome { get; private set; }

    // GET only displays the page. Each derived page handles POST to request one workload step.
    public void OnGet() { }

    internal PageResult Show(WorkloadStart result, string accepted)
    {
        (Outcome, Response.StatusCode) = result switch
        {
            WorkloadStart.Accepted => (accepted, 202),
            WorkloadStart.Busy => ("An allocation or cleanup is in progress. No additional step was queued.", 409),
            WorkloadStart.AtCapacity => ("The shared 10,000,000,000-byte cap has been reached. No bytes were added.", 409),
            WorkloadStart.Unsupported => ("Memory allocation requires a 64-bit Main process on Windows.", 503),
            _ => ("Windows could not confirm enough physical and commit headroom to leave at least 2 GiB. No bytes were added.", 503)
        };
        return Page();
    }
}

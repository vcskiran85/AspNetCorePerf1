using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PerfLab.Pages;

[LabOnly]
public sealed class ExceptionModel : PageModel
{
    public void OnGet() { }

    // This stays inside one request: the middleware returns HTTP 500, and Main keeps running.
    public void OnPost() => RequestException.Throw();
}

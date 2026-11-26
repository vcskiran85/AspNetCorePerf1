using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PerfLab.Pages;

// Exception middleware re-executes using the original HTTP method; this page never acts on input.
[IgnoreAntiforgeryToken]
public sealed class RequestFailedModel : PageModel
{
    public bool Intentional { get; private set; }

    public IActionResult OnGet() => ShowFailure();
    public IActionResult OnPost() => ShowFailure();

    private IActionResult ShowFailure()
    {
        var error = HttpContext.Features.Get<IExceptionHandlerFeature>();
        if (error is null) return NotFound();
        Intentional = error.Error is LabRequestException;
        Response.StatusCode = 500;
        return Page();
    }
}

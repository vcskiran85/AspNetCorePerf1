using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PerfLab.Pages;

[LabOnly]
public sealed class StopModel(LabWorkloads work) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Status");

    public async Task<IActionResult> OnPostAsync()
    {
        await work.StopWorkloadsAsync();
        return Page();
    }
}

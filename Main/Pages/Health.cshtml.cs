using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PerfLab.Pages;

public sealed class HealthModel : PageModel
{
    public int Pid => Environment.ProcessId;
}

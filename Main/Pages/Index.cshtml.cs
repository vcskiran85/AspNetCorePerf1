using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PerfLab.Pages;

public sealed class IndexModel(LabAccess access) : PageModel
{
    public bool Local => access.LabAllowed(HttpContext);
    public bool Hosted => access.HostedReportsAllowed();
}

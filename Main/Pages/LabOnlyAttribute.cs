using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PerfLab.Pages;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class LabOnlyAttribute(bool reports = false) : Attribute, IAuthorizationFilter, IOrderedFilter
{
    // Deny non-lab requests before MVC attempts to read or validate any POST form.
    public int Order => 0;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var access = context.HttpContext.RequestServices.GetRequiredService<LabAccess>();
        if (!access.LabAllowed(context.HttpContext) && !(reports && access.HostedReportsAllowed()))
            context.Result = new NotFoundResult();
    }
}

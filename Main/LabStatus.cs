internal static class LabStatus
{
    public static void Map(WebApplication app)
    {
        // Health answers whether Main is responding and reports its PID, even outside lab mode.
        app.MapGet("/health", () => Results.Ok(new { app = "Main", pid = Environment.ProcessId })).WithName("HealthJson");
        app.MapGet("/Status/Json", Status).WithName("StatusJson");
        app.MapGet("/lab/status", Status);
    }

    // The dashboard polls this lab-only snapshot; reading it does not reset the idle timers.
    private static IResult Status(HttpContext context, LabAccess access, LabWorkloads work) =>
        access.LabAllowed(context) ? Results.Ok(work.GetStatus()) : Results.NotFound();
}

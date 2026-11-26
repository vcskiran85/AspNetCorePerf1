using Microsoft.AspNetCore.Mvc.ApplicationModels;

// Register Razor Pages and shared services before building the HTTP app.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages(options =>
{
    AddAlias("/Health", "/health-view");
    AddAlias("/Status", "/workload-status");
    AddAlias("/Stop", "/lab/stop");
    AddAlias("/HighCpu", "/lab/cpu");
    AddAlias("/ManagedMemory", "/HighMemeoryManaged");
    AddAlias("/ManagedMemory", "/lab/managed-memory");
    AddAlias("/NativeMemory", "/HighMemeoryNative");
    AddAlias("/NativeMemory", "/lab/native-memory");
    AddAlias("/Exception", "/lab/exception");
    AddAlias("/Crash", "/lab/crash/{kind}");
    AddAlias("/AppReport", "/lab/app/{mode}");
    AddAlias("/SqlReport", "/lab/sql/{mode}");
    AddAlias("/HttpReport", "/lab/http/{mode}");

    // Old URLs still work, but Tag Helpers generate links to the main page routes.
    void AddAlias(string page, string route) => options.Conventions.AddPageRouteModelConvention(page, model =>
        model.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel { Template = route, SuppressLinkGeneration = true }
        }));
});
builder.Services.AddSingleton<LabAccess>();
builder.Services.AddSingleton<LabWorkloads>();
builder.Services.AddSingleton<ReportComparison>();
builder.Services.AddSingleton<CrashActions>();
// The host and page handlers share one workload service, so status and cleanup see the same state.
builder.Services.AddHostedService<LabWorkloads>(services => services.GetRequiredService<LabWorkloads>());
builder.Services.AddHttpClient("Reports", ConfigureReportClient);
builder.Services.AddHttpClient("LocalRepeatedReports", ConfigureReportClient)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
var app = builder.Build();

// Handle exceptions from the request pipeline before routing to pages and JSON endpoints.
app.UseExceptionHandler("/RequestFailed");
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; frame-ancestors 'none'; form-action 'self'; base-uri 'none'";
    await next(context);
});
app.UseRouting();
app.MapRazorPages();
LabStatus.Map(app);
app.Run();

void ConfigureReportClient(HttpClient client)
{
    client.BaseAddress = new Uri(builder.Configuration["WebApi:BaseUrl"]!);
    // Each outbound request has its own timeout in addition to the comparison deadline.
    client.Timeout = TimeSpan.FromSeconds(15);
}

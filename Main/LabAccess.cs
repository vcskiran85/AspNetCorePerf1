using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;

public sealed class LabAccess(IConfiguration configuration, IWebHostEnvironment environment)
{
    public bool LabAllowed(HttpContext context) => !HostedReportsAllowed() && !IsAzureAppService()
        && configuration.GetValue<bool>("Lab:Enabled")
        && (environment.IsDevelopment() || configuration.GetValue<bool>("Lab:IsolatedLocalHosting"))
        && !context.Request.Headers.ContainsKey("Forwarded")
        && !context.Request.Headers.ContainsKey("X-Forwarded-For")
        && !context.Request.Headers.ContainsKey("X-Forwarded-Host")
        && IsLoopback(context.Connection.RemoteIpAddress) && IsLoopback(context.Connection.LocalIpAddress)
        && IsLoopbackHost(context.Request.Host.Host);

    public bool HostedReportsAllowed() => configuration.GetValue<bool>("Lab:EnablePrivateHostedReports");

    public bool CrashAllowed(HttpContext context)
    {
        if (!OperatingSystem.IsWindows() || !LabAllowed(context)
            || !configuration.GetValue<bool>("Lab:EnableCrash")) return false;

        using var process = Process.GetCurrentProcess();
        if (string.Equals(process.ProcessName, "w3wp", StringComparison.OrdinalIgnoreCase))
            return configuration.GetValue<bool>("Lab:IsolatedLocalHosting")
                && string.Equals(Environment.GetEnvironmentVariable("APP_POOL_ID"), "AspNetCorePerf1-CrashLab", StringComparison.OrdinalIgnoreCase);

        return environment.IsDevelopment()
            && context.Connection.LocalPort == 5100 && context.Request.Host.Port == 5100
            && context.RequestServices.GetRequiredService<IServer>().GetType().Assembly == typeof(KestrelServer).Assembly;
    }

    private static bool IsAzureAppService() => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SKU"));
    private static bool IsLoopback(IPAddress? address) => address is not null
        && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
    public static bool IsLoopbackHost(string host) => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IsLoopback(address));
}

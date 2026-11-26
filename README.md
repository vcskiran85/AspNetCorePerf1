# ASP.NET Core 10 performance lab

`Main` is the menu and comparison client; `WebApi` is a synthetic report service. Both are independently runnable ASP.NET Core 10 web projects. Use an isolated workstation or disposable Windows VM only. Do not expose the lab or its crash switches via a proxy, tunnel, shared worker, or public binding.

## Run locally in Visual Studio

1. Install the .NET 10 SDK and a Visual Studio release that supports .NET 10 (with **ASP.NET and web development**). For SQL examples, also install SQL Server Express LocalDB.
2. Open `AspNetCorePerf1.sln`. Select **Main + WebApi (Kestrel)** from the launch profile dropdown and use Ctrl+F5 to launch both. Alternatively select either project as the startup project and choose its named profile. With only Main running, the HTTP experiments report that WebApi is unavailable; with only WebApi running, open its report directly.
3. Browse [Main](http://127.0.0.1:5100/) or [WebApi](http://127.0.0.1:5101/api/report). Both `/health` endpoints still return JSON with each process ID. Main also has a human-readable **Health** page in its top menu. The profiles use `Development` and loopback HTTP; non-crash lab actions are enabled automatically.
4. For SQL, run the entire `Setup-LocalDB.sql` file against `(localdb)\MSSQLLocalDB` as your Windows user with SSMS or `sqlcmd`. It creates/seeds the dedicated `AspNetCorePerf1` database without resetting an existing table. Run Main under the same Windows identity. For dedicated hosted SQL Server, provision the sample database separately, run `Seed-SalesEvents.sql` against it, and override `ConnectionStrings__SalesEvents`; do not commit credentials.

Equivalent CLI commands, in separate shells at the solution root:

```powershell
dotnet run --project WebApi --launch-profile WebApi
dotnet run --project Main --launch-profile Main
```

## Main scenarios

Main uses server-rendered ASP.NET Core Razor Pages (`Main/Pages`), with the menu in a shared top layout and scenario results on the right. A menu click runs the selected demo directly, except **Crash**, which opens a confirmation page. With JavaScript off, every link and form still works and a page load renders a workload snapshot. With JavaScript on, a loading overlay indicates pending navigation and only the live workload counters are polled; results are never replaced by asynchronous navigation. Use **Workload status** to get a fresh snapshot without resubmitting a POST.

| Top menu action | What it runs / what to observe |
| --- | --- |
| Managed memory | A local POST queues exactly **500,000,000 bytes** in a managed array and touches the allocation. Observe GC heap and large-object-heap counters. |
| Native memory | A local POST queues exactly **500,000,000 bytes** via `NativeMemory.Alloc` and touches the allocation. Compare private bytes/working set with the managed heap. |
| CPU +10 points | A local POST adds **10 percentage points** to the aggregate target per click, up to 100%, across the logical processors available to Main. The measured process CPU sample may differ. The CPU workers stop after 60 seconds without a CPU step. |
| Exception | A local POST throws a real request-scoped exception. Main logs it and returns a dedicated Razor HTTP 500 result. Check **Health**: the PID stays the same. |
| App slow | Scans sales once per customer. The matching result links to the fast variant, which aggregates sales into indexed customer totals in one pass. |
| SQL slow | Runs the scanned lookup against `dbo.SalesEvents` using `CONVERT(varchar(12), EventId) = '125001'`. The matching fast GET uses `EventId = @EventId` with an `int` parameter, allowing a primary-key seek. Compare actual execution plans and logical reads in your SQL client. |
| HTTP slow | Sends twelve sequential WebApi GETs per pass to aggregate detail pages. The matching fast GET uses one `GET /api/report` for the same count and amount. |
| Crash | Opens a dedicated confirmation page. A GET never terminates the worker; only a valid local POST can do so when enabled. |
| Workload status / Stop workloads | See CPU, allocation and timeout metrics. Stop cancels CPU and pending memory work, releases retained allocations, and waits for cleanup. Workload status JSON remains at `/Status/Json` and `/lab/status`. |
| Health | A distinct Razor page checks Main responsiveness and PID. `GET /health` remains JSON. |

CPU and memory can run concurrently. Managed and native memory share a **10,000,000,000-byte** maximum; one memory allocation can be in flight at a time. Memory requires **64-bit Windows**, and Main must verify a **2 GiB physical and commit reserve** before and during allocation. Stop or two minutes without a memory step releases retained allocations. Main does not force a GC, so heap and working-set counters can lag cleanup. A pending step can fail if headroom changes; see the live status notice.

Memory, CPU, Exception, Stop, and crash execution use antiforgery-protected Razor POST forms and are guarded server-side. Read-only App/SQL/HTTP links use GET and share the comparison guard and concurrency limit; local report GETs reject cross-origin fetch and prefetch metadata when provided.

For a successful local slow comparison, Main starts real passes until at least **5 seconds** of work have elapsed; the final pass can extend past that window. There are no artificial sleeps. The SQL slow pass runs the same scan again on the open connection; the HTTP slow pass sends another serial batch of twelve requests. Results show elapsed time, completed passes, and total GETs for HTTP; the final pass supplies the report values. Fast variants still run once. To keep repeated work local, SQL repetition is limited to LocalDB and recognized loopback SQL sources (such as `.`, `(local)`, `localhost`, or `127.0.0.1` with an optional instance or port); HTTP repeats only against a loopback `WebApi__BaseUrl`, without proxies or redirects. Other configured dependency hosts run one pass, as do all private hosted comparisons.

Warm up each variant before comparing elapsed times (first requests can include JIT, data initialization, SQL connection setup, and HTTP connection establishment). Main admits one comparison at a time and observes request cancellation. Local comparisons and hosted HTTP comparisons have a 20-second deadline. SQL commands have a 10-second timeout; each outbound HTTP request has a 15-second timeout. The default SQL connection string sets a 5-second connect timeout.

## Crash demonstrations: isolated Windows hosts only

Crash execution is off by default; the local Crash page explains when it is disabled. To demonstrate termination of **only the Main Kestrel process**, launch the Windows Development profile on `http://127.0.0.1:5100` with `Lab__EnableCrash=true` in that launch environment (for example, launch Main from a dedicated PowerShell window using the CLI commands above after setting `$env:Lab__EnableCrash = 'true'`). The WebApi process is separate. Restart Main after each termination. Remove the environment override afterward. Do not bind the lab beyond loopback or use a tunnel/proxy.

To demonstrate termination of an **actual full Windows IIS worker**, use the ASP.NET Core 10 Hosting Bundle and two dedicated app pools, one per project. Name Main's dedicated pool **`AspNetCorePerf1-CrashLab`**. The checked-in `web.config` selects **in-process** IIS, so the application PID in full IIS is the `w3wp.exe` PID. If deploying to your isolated VM later, use separate site roots and bind only to `127.0.0.1` with ports 5100 (Main) and 5101 (WebApi); do not use a public IP, wildcard binding, proxy or tunnel. Run comparisons before enabling crash actions; do not attach a debugger during the crash demonstrations.

Published defaults are off. For your isolated IIS test only, set each application's `Lab__Enabled=true` and `Lab__IsolatedLocalHosting=true` in its own IIS/ANCM environment. Set `Lab__EnableCrash=true` **only for Main** when ready to terminate the dedicated worker. Do not change the checked-in defaults. Recycle the corresponding dedicated app pool after changing settings. Keep the environment as `Production`. For SQL under IIS, use a dedicated SQL database accessible to the app pool identity (or a suitably scoped connection string); LocalDB created by your developer account will not normally be accessible to IIS.

Only direct loopback requests to a loopback host can open the crash controls. Crash requires a supported Windows host and the separate crash switch; full IIS additionally requires both lab opt-ins and the exact pool name above. The endpoint accepts only POST with a valid antiforgery cookie/token pair from the Crash page, rejects cross-site fetch metadata and forwarding headers when present, and requires the exact phrase `TERMINATE LAB PROCESS` for each action. Note `/health`'s PID before submitting:

- **Unhandled background exception:** an uncaught exception on a dedicated CLR thread terminates the worker, unlike the request exception.
- **Stack overflow:** non-tail recursion consumes real stack frames until the runtime terminates the process.
- **Windows fail-fast:** calls `RaiseFailFastException`; this simulates abrupt native-style termination for diagnostics. It does **not** write outside any allocation or corrupt a native heap, and it does not produce a heap-corruption signature.

The crash is queued after the response completes; if delivery is interrupted, the browser may instead show a connection failure. Verify termination in the Kestrel console or Windows Event Viewer and compare Main's PID after restarting it or after the next successful IIS `/health`. IIS might return a transient error or stop restarting the pool after repeated failures (Rapid-Fail Protection). Do not disable that protection. Stop after each demonstration, turn off the crash switch and recycle the pool. Leave all lab switches off when done.

## Private hosted comparisons (no crash or background workloads)

For an access-restricted deployment, first make **both** apps private (for example, App Service private endpoints or access restrictions with public access denied) and ensure Main can reach the private WebApi hostname. Then set `Lab__EnablePrivateHostedReports=true` on **both** apps and configure Main's `WebApi__BaseUrl` to the private API base URL, ending in `/`. Leave `Lab__Enabled`, `Lab__IsolatedLocalHosting`, and `Lab__EnableCrash` off. This setting serves only the app/SQL/HTTP comparison menu and GET comparisons in Main and `GET /api/report` in WebApi; `/health` remains available. The hosted slow variants retain their original single pass; they are not stretched to five seconds. The switch is not authentication: the private ingress configuration is mandatory. The request exception, background workload controls, and process termination are not available in hosted report mode. Crash is never supported on Azure App Service.

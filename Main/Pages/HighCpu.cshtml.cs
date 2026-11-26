using Microsoft.AspNetCore.Mvc;

namespace PerfLab.Pages;

[LabOnly]
public sealed class HighCpuModel(LabWorkloads work) : WorkloadStepModel
{
    public IActionResult OnPost() => Show(work.AddCpuStep(),
        "CPU step accepted: +10 percentage points, capped at 100%. See the current target in Live workloads.");
}

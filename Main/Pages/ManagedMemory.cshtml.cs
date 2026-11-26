using Microsoft.AspNetCore.Mvc;

namespace PerfLab.Pages;

[LabOnly]
public sealed class ManagedMemoryModel(LabWorkloads work) : WorkloadStepModel
{
    public IActionResult OnPost() => Show(work.AddMemoryStep(native: false),
        "Managed allocation queued: 500,000,000 bytes. Check Live workloads for completion.");
}

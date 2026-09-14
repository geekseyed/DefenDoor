using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using ISCM.Domain.ValueObjects; // ← اضافه شود

namespace ISCM.Application.Interfaces;

public interface IScanService
{
    int TotalCheckCount { get; }
    // تغییر IProgress<string> به IProgress<ScanProgressUpdate>
    Task<ScanResult> RunScanAsync(ScanMode mode = ScanMode.Full, IProgress<ScanProgressUpdate>? progress = null);
    Task<Finding> RescanCheckAsync(string checkId);
    Task<Finding> RescanSubControlAsync(string checkId, string subControlId);
}
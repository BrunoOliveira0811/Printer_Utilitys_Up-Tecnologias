namespace PrinterScanner.App.Models;

public sealed class ScanRequest
{
    public ScanMode Mode { get; init; }
    public string? StartIp { get; init; }
    public string? EndIp { get; init; }
    public string? CidrNotation { get; init; }
    public string? PreferredSubnetMask { get; init; }
    public string? SelectedLocalIP { get; init; }
    public IReadOnlyList<string> Communities { get; init; } = Array.Empty<string>();
    public int TimeoutMilliseconds { get; init; } = 1500;
    public int MaxConcurrency { get; init; }
}

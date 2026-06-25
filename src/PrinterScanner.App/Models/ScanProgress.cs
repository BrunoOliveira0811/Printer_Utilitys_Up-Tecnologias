namespace PrinterScanner.App.Models;

public sealed class ScanProgress
{
    public int ProcessedCount { get; init; }
    public int TotalCount { get; init; }
    public string CurrentIp { get; init; } = string.Empty;
    public PrinterDevice? Device { get; init; }
    public double Percent => TotalCount == 0 ? 0 : (double)ProcessedCount / TotalCount * 100d;
}

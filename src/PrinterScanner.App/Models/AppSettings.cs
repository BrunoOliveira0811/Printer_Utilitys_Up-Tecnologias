namespace PrinterScanner.App.Models;

public sealed class AppSettings
{
    public List<string> SnmpCommunities { get; set; } = new() { "public", "private" };
    public int TimeoutMilliseconds { get; set; } = 1500;
    public int MaxConcurrentScans { get; set; } = 0;
    public bool DarkModeEnabled { get; set; } = false;
}

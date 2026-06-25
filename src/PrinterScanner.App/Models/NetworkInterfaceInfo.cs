namespace PrinterScanner.App.Models;

public sealed class NetworkInterfaceInfo
{
    public string InterfaceName { get; init; } = string.Empty;
    public string IpAddress     { get; init; } = string.Empty;
    public string SubnetMask    { get; init; } = string.Empty;

    public string DisplayName => $"{InterfaceName}  ({IpAddress})";

    public override string ToString() => DisplayName;
}

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PrinterScanner.App.Models;

public sealed class PrinterDevice : INotifyPropertyChanged
{
    private string deviceName = "Dispositivo de Impressao Desconhecido";
    private string ipAddress = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string IpAddress
    {
        get => ipAddress;
        set
        {
            if (ipAddress == value) return;
            ipAddress = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IpAddress)));
        }
    }
    public string? SubnetMask { get; set; }
    public string? Gateway { get; set; }
    public string DhcpStatus { get; set; } = "Nao Identificado";
    public string MacAddress { get; set; } = string.Empty;

    public string DeviceName
    {
        get => deviceName;
        set
        {
            if (deviceName == value) return;
            deviceName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeviceName)));
        }
    }

    public string? DnsName { get; set; }
    public string? SysDescription { get; set; }
    public bool SnmpResponded { get; set; }
    public bool Port9100Open { get; set; }
    public bool Port515Open { get; set; }
    public bool Port631Open { get; set; }
    public bool IsLikelyPrinter { get; set; }
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.Now;
    public string DisplaySubnetMask => string.IsNullOrWhiteSpace(SubnetMask) ? "-" : SubnetMask;
    public string DisplayGateway => string.IsNullOrWhiteSpace(Gateway) ? "-" : Gateway;
}

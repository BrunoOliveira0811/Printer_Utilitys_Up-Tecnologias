using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PrinterScanner.App.Models;

public sealed class PrinterDevice : INotifyPropertyChanged
{
    private string deviceName = "Dispositivo de Impressao Desconhecido";
    private string ipAddress = string.Empty;
    private string? sysDescription;

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

    private bool workOffline;
    private bool queuePaused;

    public string? DnsName { get; set; }

    public string? SysDescription
    {
        get => sysDescription;
        set
        {
            if (sysDescription == value) return;
            sysDescription = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SysDescription)));
        }
    }
    public bool SnmpResponded { get; set; }
    public bool Port9100Open { get; set; }
    public bool Port515Open { get; set; }
    public bool Port631Open { get; set; }
    public bool IsLikelyPrinter { get; set; }
    public bool IsUsbDevice { get; set; }
    public bool IsInstalled { get; set; }
    public string InstalledPrinterName { get; set; } = string.Empty;
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.Now;

    public bool WorkOffline
    {
        get => workOffline;
        set
        {
            if (workOffline == value) return;
            workOffline = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WorkOffline)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WorkOfflineDisplay)));
        }
    }

    public bool QueuePaused
    {
        get => queuePaused;
        set
        {
            if (queuePaused == value) return;
            queuePaused = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QueuePaused)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QueuePausedDisplay)));
        }
    }

    public string DisplaySubnetMask   => string.IsNullOrWhiteSpace(SubnetMask) ? "-" : SubnetMask;
    public string DisplayGateway      => string.IsNullOrWhiteSpace(Gateway) ? "-" : Gateway;
    public string ConnectionType      => IsUsbDevice ? "USB" : "Rede";
    public string InstalledDisplay    => IsInstalled ? "✓" : string.Empty;
    public string WorkOfflineDisplay  => WorkOffline  ? "Offline"  : string.Empty;
    public string QueuePausedDisplay  => QueuePaused  ? "Pausada"  : string.Empty;
}

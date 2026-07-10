using System.Net;
using PrinterScanner.App.Infrastructure;
using PrinterScanner.App.Services;

namespace PrinterScanner.App.Views;

public sealed class ProtocolOption
{
    public PrinterProtocol Protocol { get; init; }
    public string          DisplayName { get; init; } = "";
    public override string ToString() => DisplayName;
}

public sealed class ChangeIpViewModel : ObservableObject
{
    private readonly PrinterIpService printerIpService;
    private string newIp;
    private string newMask;
    private string newGateway;
    private string statusMessage = string.Empty;
    private double progressValue;
    private bool isBusy;
    private bool hasStatus;
    private ProtocolOption selectedProtocol;
    private CancellationTokenSource? cts;

    public static IReadOnlyList<ProtocolOption> AvailableProtocols { get; } = new List<ProtocolOption>
    {
        new() { Protocol = PrinterProtocol.Auto,      DisplayName = "Automatico (tenta todos)"        },
        new() { Protocol = PrinterProtocol.Rongta,    DisplayName = "Rongta / Jetway JP-800"          },
        new() { Protocol = PrinterProtocol.TectoyQ4,  DisplayName = "Tectoy Q4 / i7"                  },
        new() { Protocol = PrinterProtocol.EscPosGsN, DisplayName = "ESC/POS GS(N) — Epson / Elgin"  },
        new() { Protocol = PrinterProtocol.Http,      DisplayName = "HTTP (interface web)"             },
    };

    public ChangeIpViewModel(
        string currentIp, string currentMask, string currentGateway,
        string macAddress, PrinterIpService printerIpService,
        string deviceName = "", string sysDescription = "")
    {
        this.printerIpService = printerIpService;
        MacAddress = macAddress;
        CurrentIp  = currentIp;
        newIp      = currentIp;
        newMask    = string.IsNullOrWhiteSpace(currentMask)    ? "255.255.255.0" : currentMask;
        newGateway = string.IsNullOrWhiteSpace(currentGateway) ? GuessGateway(currentIp) : currentGateway;

        var detected = DetectProtocol(deviceName, sysDescription);
        selectedProtocol = AvailableProtocols.First(p => p.Protocol == detected);
    }

    private static PrinterProtocol DetectProtocol(string name, string desc)
    {
        var s = (name + " " + desc).ToLowerInvariant();
        if (s.Contains("tectoy") || s.Contains("q4")
            || s.Contains(" i7") || s.StartsWith("i7"))    return PrinterProtocol.TectoyQ4;
        if (s.Contains("jetway") || s.Contains("jp-800")
            || s.Contains("rongta"))                        return PrinterProtocol.Rongta;
        if (s.Contains("epson")  || s.Contains("elgin")
            || s.Contains("tm-"))                           return PrinterProtocol.EscPosGsN;
        return PrinterProtocol.Auto;
    }

    public string CurrentIp  { get; }
    public string MacAddress { get; }

    public ProtocolOption SelectedProtocol
    {
        get => selectedProtocol;
        set => SetProperty(ref selectedProtocol, value);
    }

    public string NewIp
    {
        get => newIp;
        set { SetProperty(ref newIp, value); RaisePropertyChanged(nameof(CanApply)); }
    }

    public string NewMask
    {
        get => newMask;
        set => SetProperty(ref newMask, value);
    }

    public string NewGateway
    {
        get => newGateway;
        set => SetProperty(ref newGateway, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set { SetProperty(ref statusMessage, value); HasStatus = !string.IsNullOrEmpty(value); }
    }

    public double ProgressValue
    {
        get => progressValue;
        private set => SetProperty(ref progressValue, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set { SetProperty(ref isBusy, value); RaisePropertyChanged(nameof(CanApply)); }
    }

    public bool HasStatus
    {
        get => hasStatus;
        private set => SetProperty(ref hasStatus, value);
    }

    public bool CanApply => !IsBusy && IPAddress.TryParse(NewIp, out _) && NewIp != CurrentIp;

    public void RequestCancel() => cts?.Cancel();

    public async Task<bool> ApplyAsync()
    {
        using var localCts = new CancellationTokenSource();
        cts = localCts;

        IsBusy = true;
        ProgressValue = 10;
        StatusMessage = "Enviando configuracao para a impressora...";

        var progress = new Progress<string>(msg => StatusMessage = msg);
        var progressPct = new Progress<double>(v => ProgressValue = v);

        try
        {
            var success = await printerIpService.ChangeIpAsync(CurrentIp, NewIp, NewMask, NewGateway, MacAddress, progress, progressPct, selectedProtocol.Protocol, localCts.Token);
            ProgressValue = success ? 100 : 0;
            return success;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operacao cancelada.";
            ProgressValue = 0;
            return false;
        }
        finally
        {
            IsBusy = false;
            cts = null;
        }
    }

    private static string GuessGateway(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return string.Empty;
        var parts = addr.ToString().Split('.');
        if (parts.Length != 4) return string.Empty;
        return $"{parts[0]}.{parts[1]}.{parts[2]}.1";
    }
}

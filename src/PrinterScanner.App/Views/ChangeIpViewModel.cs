using System.Net;
using PrinterScanner.App.Infrastructure;
using PrinterScanner.App.Services;

namespace PrinterScanner.App.Views;

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

    public ChangeIpViewModel(string currentIp, string currentMask, string currentGateway, PrinterIpService printerIpService)
    {
        this.printerIpService = printerIpService;
        CurrentIp  = currentIp;
        newIp      = currentIp;
        newMask    = string.IsNullOrWhiteSpace(currentMask)    ? "255.255.255.0" : currentMask;
        newGateway = string.IsNullOrWhiteSpace(currentGateway) ? GuessGateway(currentIp) : currentGateway;
    }

    public string CurrentIp { get; }

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

    public async Task<bool> ApplyAsync()
    {
        IsBusy = true;
        ProgressValue = 10;
        StatusMessage = "Enviando configuracao para a impressora...";

        var progress = new Progress<string>(msg => StatusMessage = msg);
        var progressPct = new Progress<double>(v => ProgressValue = v);

        var success = await printerIpService.ChangeIpAsync(CurrentIp, NewIp, NewMask, NewGateway, progress, progressPct);

        ProgressValue = success ? 100 : 0;
        IsBusy = false;
        return success;
    }

    private static string GuessGateway(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return string.Empty;
        var parts = addr.ToString().Split('.');
        if (parts.Length != 4) return string.Empty;
        return $"{parts[0]}.{parts[1]}.{parts[2]}.1";
    }
}

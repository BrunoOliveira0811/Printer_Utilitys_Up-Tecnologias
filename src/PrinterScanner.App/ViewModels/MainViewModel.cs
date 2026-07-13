using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using PrinterScanner.App.Infrastructure;
using PrinterScanner.App.Models;
using PrinterScanner.App.Services;
using PrinterScanner.App.Views;

namespace PrinterScanner.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsService settingsService;
    private readonly IpRangeService ipRangeService;
    private readonly NetworkScannerService networkScannerService;
    private readonly FileLogService logService;
    private readonly PrinterIpService printerIpService;
    private readonly DriverService driverService;
    private readonly UtilitariosService utilitariosService;
    private readonly RelayCommand startScanCommand;
    private readonly RelayCommand cancelScanCommand;
    private readonly RelayCommand clearResultsCommand;
    private readonly RelayCommand changeIpCommand;
    private readonly RelayCommand installDriverCommand;
    private readonly RelayCommand refreshInterfacesCommand;
    private readonly RelayCommand openPrinterControlCommand;
    private readonly RelayCommand openUtilitariosCommand;
    private readonly RelayCommand saveReportCommand;
    private readonly RelayCommand toggleDarkModeCommand;
    private readonly RelayCommand broadcastScanCommand;
    private readonly RelayCommand clearQueueCommand;
    private readonly RelayCommand clearSpoolerCommand;
    private readonly RelayCommand pauseQueueCommand;
    private readonly RelayCommand sendTestPrintCommand;
    private readonly RelayCommand changeUsbPortCommand;
    private readonly RelayCommand sharePrinterCommand;
    private readonly RelayCommand toggleWorkOfflineCommand;
    private readonly RelayCommand resumePrintQueueCommand;
    private readonly DeviceNameService deviceNameService;
    private readonly PrinterWindowsService printerWindowsService;
    private bool isDarkMode;
    private PrinterDevice? selectedDevice;
    private NetworkInterfaceInfo? selectedNetworkInterface;
    private readonly ObservableCollection<PrinterDevice> devices = new();
    private readonly ObservableCollection<NetworkInterfaceInfo> availableInterfaces = new();
    private CancellationTokenSource? scanCancellationSource;
    private AppSettings currentSettings = new();
    private ScanMode selectedScanMode = ScanMode.SubredeAtual;
    private string startIp = string.Empty;
    private string endIp = string.Empty;
    private string cidrNotation = string.Empty;
    private string snmpCommunitiesText = "public";
    private string searchText = string.Empty;
    private string statusMessage = "Pronto para iniciar a varredura.";
    private double progressValue;
    private string progressText = "0%";
    private bool isScanning;

    public MainViewModel(
        SettingsService settingsService,
        IpRangeService ipRangeService,
        NetworkScannerService networkScannerService,
        FileLogService logService,
        PrinterIpService printerIpService,
        DriverService driverService,
        DeviceNameService deviceNameService,
        PrinterWindowsService printerWindowsService,
        UtilitariosService utilitariosService)
    {
        this.settingsService = settingsService;
        this.ipRangeService = ipRangeService;
        this.networkScannerService = networkScannerService;
        this.logService = logService;
        this.printerIpService      = printerIpService;
        this.driverService         = driverService;
        this.deviceNameService     = deviceNameService;
        this.printerWindowsService = printerWindowsService;
        this.utilitariosService    = utilitariosService;

        DevicesView = CollectionViewSource.GetDefaultView(devices);
        DevicesView.Filter = FilterDevice;

        startScanCommand         = new RelayCommand(_ => _ = StartScanAsync(), _ => !IsScanning);
        cancelScanCommand        = new RelayCommand(_ => CancelScan(), _ => IsScanning);
        clearResultsCommand      = new RelayCommand(_ => ClearResults(), _ => devices.Count > 0 && !IsScanning);
        changeIpCommand          = new RelayCommand(_ => OpenChangeIpDialog(), _ => selectedDevice is not null && !selectedDevice.IsUsbDevice && !IsScanning);
        installDriverCommand     = new RelayCommand(_ => _ = InstallDriverAsync(), _ => CanInstallDriver());
        refreshInterfacesCommand    = new RelayCommand(_ => LoadInterfaces(), _ => !IsScanning);
        openPrinterControlCommand   = new RelayCommand(_ => OpenPrinterControl());
        openUtilitariosCommand      = new RelayCommand(_ => OpenUtilitarios());
        saveReportCommand           = new RelayCommand(_ => _ = SaveReportAsync(), _ => devices.Count > 0 && !IsScanning);
        toggleDarkModeCommand       = new RelayCommand(_ => ToggleDarkMode());
        broadcastScanCommand        = new RelayCommand(_ => _ = BroadcastScanAsync(), _ => !IsScanning);
        clearQueueCommand           = new RelayCommand(_ => _ = ClearQueueAsync(), _ => !IsScanning);
        clearSpoolerCommand         = new RelayCommand(_ => _ = ClearSpoolerAsync(), _ => !IsScanning);
        pauseQueueCommand           = new RelayCommand(_ => _ = TogglePauseQueueAsync(), _ => selectedDevice is not null && !IsScanning);
        sendTestPrintCommand        = new RelayCommand(_ => _ = SendTestPrintAsync(), _ => selectedDevice is not null && !IsScanning);
        changeUsbPortCommand        = new RelayCommand(_ => _ = ChangeUsbPortAsync(), _ => selectedDevice?.IsUsbDevice == true && !IsScanning);
        sharePrinterCommand         = new RelayCommand(_ => _ = SharePrinterAsync(), _ => selectedDevice is not null && !IsScanning);
        toggleWorkOfflineCommand    = new RelayCommand(_ => _ = ToggleWorkOfflineAsync(), _ => !string.IsNullOrEmpty(selectedDevice?.InstalledPrinterName) && !IsScanning);
        resumePrintQueueCommand     = new RelayCommand(_ => _ = ResumePrintQueueSelectedAsync(), _ => selectedDevice?.QueuePaused == true && !string.IsNullOrEmpty(selectedDevice?.InstalledPrinterName) && !IsScanning);
    }

    public ICollectionView DevicesView { get; }
    public ObservableCollection<string> BuscaAmpliadaLog { get; } = new();
    public IEnumerable<NetworkInterfaceInfo> AvailableInterfaces => availableInterfaces;

    public bool HasAnyOfflinePrinter => devices.Any(d => d.WorkOffline);
    public bool HasAnyPausedQueue    => devices.Any(d => d.QueuePaused);

    public RelayCommand StartScanCommand => startScanCommand;

    public RelayCommand CancelScanCommand => cancelScanCommand;

    public RelayCommand ClearResultsCommand => clearResultsCommand;

    public RelayCommand ChangeIpCommand => changeIpCommand;

    public RelayCommand InstallDriverCommand     => installDriverCommand;
    public RelayCommand RefreshInterfacesCommand  => refreshInterfacesCommand;
    public RelayCommand OpenPrinterControlCommand => openPrinterControlCommand;
    public RelayCommand OpenUtilitariosCommand    => openUtilitariosCommand;
    public RelayCommand SaveReportCommand         => saveReportCommand;
    public RelayCommand ToggleDarkModeCommand     => toggleDarkModeCommand;
    public RelayCommand BroadcastScanCommand    => broadcastScanCommand;
    public RelayCommand ClearQueueCommand       => clearQueueCommand;
    public RelayCommand ClearSpoolerCommand     => clearSpoolerCommand;
    public RelayCommand PauseQueueCommand       => pauseQueueCommand;
    public RelayCommand SendTestPrintCommand    => sendTestPrintCommand;
    public RelayCommand ChangeUsbPortCommand        => changeUsbPortCommand;
    public RelayCommand SharePrinterCommand         => sharePrinterCommand;
    public RelayCommand ToggleWorkOfflineCommand    => toggleWorkOfflineCommand;
    public RelayCommand ResumePrintQueueCommand     => resumePrintQueueCommand;

    public bool IsDarkMode
    {
        get => isDarkMode;
        private set
        {
            if (SetProperty(ref isDarkMode, value))
                RaisePropertyChanged(nameof(DarkModeButtonText));
        }
    }

    public string DarkModeButtonText => isDarkMode ? "Modo Claro" : "Modo Escuro";

    public PrinterDevice? SelectedDevice
    {
        get => selectedDevice;
        set
        {
            SetProperty(ref selectedDevice, value);
            changeIpCommand.RaiseCanExecuteChanged();
            installDriverCommand.RaiseCanExecuteChanged();
            pauseQueueCommand.RaiseCanExecuteChanged();
            sendTestPrintCommand.RaiseCanExecuteChanged();
            changeUsbPortCommand.RaiseCanExecuteChanged();
            sharePrinterCommand.RaiseCanExecuteChanged();
            toggleWorkOfflineCommand.RaiseCanExecuteChanged();
            resumePrintQueueCommand.RaiseCanExecuteChanged();
        }
    }

    public NetworkInterfaceInfo? SelectedNetworkInterface
    {
        get => selectedNetworkInterface;
        set => SetProperty(ref selectedNetworkInterface, value);
    }

    public IEnumerable<ScanMode> ScanModes => Enum.GetValues<ScanMode>();

    public ScanMode SelectedScanMode
    {
        get => selectedScanMode;
        set => SetProperty(ref selectedScanMode, value);
    }

    public string StartIp
    {
        get => startIp;
        set => SetProperty(ref startIp, value);
    }

    public string EndIp
    {
        get => endIp;
        set => SetProperty(ref endIp, value);
    }

    public string CidrNotation
    {
        get => cidrNotation;
        set => SetProperty(ref cidrNotation, value);
    }

    public string SnmpCommunitiesText
    {
        get => snmpCommunitiesText;
        set => SetProperty(ref snmpCommunitiesText, value);
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                DevicesView.Refresh();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public double ProgressValue
    {
        get => progressValue;
        private set => SetProperty(ref progressValue, value);
    }

    public string ProgressText
    {
        get => progressText;
        private set => SetProperty(ref progressText, value);
    }

    public bool IsScanning
    {
        get => isScanning;
        private set
        {
            if (SetProperty(ref isScanning, value))
            {
                UpdateCommands();
            }
        }
    }

    public async Task InitializeAsync()
    {
        currentSettings = await settingsService.LoadSettingsAsync();
        SnmpCommunitiesText = string.Join("; ", currentSettings.SnmpCommunities);
        await deviceNameService.LoadAsync();
        ApplyTheme(currentSettings.DarkModeEnabled);
        IsDarkMode = currentSettings.DarkModeEnabled;

        ProgressValue = 0;
        ProgressText = "0%";
        StatusMessage = "Aplicativo pronto. Clique em Iniciar para buscar impressoras.";

        LoadInterfaces();
        RefreshExportState();
        await ScanInstalledPrintersAsync();
    }

    public async Task StartScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        if (!ipRangeService.HasActiveNetworkInterface())
        {
            MessageBox.Show(
                "Nenhuma interface de rede ativa foi encontrada. Conecte-se por Wi-Fi ou Ethernet e tente novamente.",
                "Rede indisponivel",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsScanning = true;
            scanCancellationSource = new CancellationTokenSource();
            ProgressValue = 0;
            ProgressText = "0%";
            StatusMessage = "Preparando varredura...";
            devices.Clear();

            currentSettings.SnmpCommunities = ParseCommunities(SnmpCommunitiesText);
            await settingsService.SaveSettingsAsync(currentSettings);

            var request = BuildRequest();
            var progress = new Progress<ScanProgress>(info =>
            {
                ProgressValue = info.Percent;
                ProgressText = $"{Math.Round(info.Percent, 0)}%";
                StatusMessage = $"Varrendo {info.CurrentIp} ({info.ProcessedCount}/{info.TotalCount})";
            });

            await networkScannerService.ScanAsync(
                request,
                progress,
                device => Application.Current.Dispatcher.Invoke(() => UpsertDevice(device)),
                scanCancellationSource.Token);

            await ScanInstalledPrintersAsync();
            StatusMessage = $"Varredura concluida. {devices.Count} impressora(s) encontrada(s).";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Varredura cancelada pelo usuario.";
        }
        catch (InvalidOperationException ex)
        {
            logService.LogError("Validacao da varredura falhou.", ex);
            StatusMessage = ex.Message;
            MessageBox.Show(
                ex.Message,
                "Validacao da varredura",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            logService.LogError("Erro ao iniciar a varredura.", ex);
            StatusMessage = "A varredura falhou. Consulte o log local para detalhes.";
            MessageBox.Show(
                "Ocorreu um erro durante a varredura. Consulte o log local para detalhes.",
                "Falha na varredura",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
            scanCancellationSource?.Dispose();
            scanCancellationSource = null;
            RefreshExportState();
        }
    }

    private void AddBuscaLog(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Application.Current.Dispatcher.Invoke(() => BuscaAmpliadaLog.Add(line));
        StatusMessage = msg;
    }

    public async Task BroadcastScanAsync()
    {
        if (IsScanning) return;

        try
        {
            Application.Current.Dispatcher.Invoke(() => BuscaAmpliadaLog.Clear());
            IsScanning = true;
            scanCancellationSource = new CancellationTokenSource();
            ProgressValue = 0;
            ProgressText = "0%";
            AddBuscaLog("Iniciando busca ampliada (ARP + broadcast UDP)...");

            currentSettings.SnmpCommunities = ParseCommunities(SnmpCommunitiesText);
            var communities = currentSettings.SnmpCommunities;

            var statusProg = new Progress<string>(msg => AddBuscaLog(msg));
            var progressProg = new Progress<double>(pct =>
            {
                ProgressValue = pct;
                ProgressText  = $"{Math.Round(pct, 0)}%";
            });

            Task RunDiscover() => networkScannerService.BroadcastDiscoverAsync(
                communities,
                statusProg,
                progressProg,
                device => Application.Current.Dispatcher.Invoke(() => UpsertDevice(device)),
                scanCancellationSource.Token);

            // Remove IPs .253 que possam ter ficado de execuções anteriores
            var orphanCheck = System.Net.NetworkInformation.NetworkInterface
                .GetAllNetworkInterfaces()
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Any(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                          && u.Address.ToString().EndsWith(".253"));
            if (orphanCheck)
            {
                AddBuscaLog("Limpando IPs temporarios de execucoes anteriores...");
                await printerWindowsService.CleanupOrphanedSubnetIpsAsync();
            }

            // Detecta sub-redes adjacentes sem rota local e adiciona IPs temporários
            var unreachable = NetworkScannerService.GetUnreachableAdjacentSubnetPrefixes();
            if (unreachable.Count > 0)
            {
                AddBuscaLog($"Detectadas {unreachable.Count} sub-rede(s) sem rota local. Adicionando IPs temporarios...");
                await printerWindowsService.RunWithTemporarySubnetIpsAsync(
                    unreachable,
                    RunDiscover,
                    msg => AddBuscaLog(msg));
            }
            else
            {
                AddBuscaLog("Nenhuma sub-rede adjacente sem rota. Iniciando descoberta...");
                await RunDiscover();
            }

            await ScanInstalledPrintersAsync();
            ProgressValue = 100;
            ProgressText = "100%";
            AddBuscaLog($"Busca ampliada concluida. {devices.Count} impressora(s) na lista.");
        }
        catch (OperationCanceledException)
        {
            AddBuscaLog("Busca ampliada cancelada. Aguardando restauracao do DHCP...");
        }
        catch (Exception ex)
        {
            logService.LogError("Erro na busca ampliada.", ex);
            AddBuscaLog("Erro na busca ampliada. Consulte o log.");
        }
        finally
        {
            IsScanning = false;
            scanCancellationSource?.Dispose();
            scanCancellationSource = null;
            RefreshExportState();
        }
    }

    private void CancelScan()
    {
        scanCancellationSource?.Cancel();
    }

    private void ClearResults()
    {
        foreach (var d in devices) d.PropertyChanged -= OnDevicePropertyChanged;
        devices.Clear();
        SearchText = string.Empty;
        ProgressValue = 0;
        ProgressText = "0%";
        StatusMessage = "Aplicativo pronto. Clique em Iniciar para buscar impressoras.";
        RefreshExportState();
    }

    private ScanRequest BuildRequest()
    {
        string? preferredMask = null;
        string? selectedLocalIp = null;

        if (SelectedScanMode == ScanMode.SubredeAtual)
        {
            selectedLocalIp = selectedNetworkInterface?.IpAddress;
            preferredMask   = ipRangeService.GetCurrentSubnet(selectedLocalIp).SubnetMask.ToString();
        }

        return SelectedScanMode switch
        {
            ScanMode.SubredeAtual => new ScanRequest
            {
                Mode = SelectedScanMode,
                SelectedLocalIP = selectedLocalIp,
                Communities = currentSettings.SnmpCommunities,
                TimeoutMilliseconds = currentSettings.TimeoutMilliseconds,
                MaxConcurrency = currentSettings.MaxConcurrentScans,
                PreferredSubnetMask = preferredMask
            },
            ScanMode.FaixaManual => BuildManualRequest(preferredMask),
            ScanMode.Cidr => BuildCidrRequest(preferredMask),
            _ => throw new InvalidOperationException("Modo de varredura invalido.")
        };
    }

    private ScanRequest BuildManualRequest(string? preferredMask)
    {
        if (string.IsNullOrWhiteSpace(StartIp) || string.IsNullOrWhiteSpace(EndIp))
        {
            throw new InvalidOperationException("Informe o IP inicial e o IP final para a varredura manual.");
        }

        ipRangeService.ExpandManualRange(StartIp, EndIp);

        return new ScanRequest
        {
            Mode = SelectedScanMode,
            StartIp = StartIp,
            EndIp = EndIp,
            Communities = currentSettings.SnmpCommunities,
            TimeoutMilliseconds = currentSettings.TimeoutMilliseconds,
            MaxConcurrency = currentSettings.MaxConcurrentScans,
            PreferredSubnetMask = preferredMask
        };
    }

    private ScanRequest BuildCidrRequest(string? preferredMask)
    {
        if (string.IsNullOrWhiteSpace(CidrNotation))
        {
            throw new InvalidOperationException("Informe a rede no formato CIDR.");
        }

        ipRangeService.ExpandCidr(CidrNotation);

        return new ScanRequest
        {
            Mode = SelectedScanMode,
            CidrNotation = CidrNotation,
            Communities = currentSettings.SnmpCommunities,
            TimeoutMilliseconds = currentSettings.TimeoutMilliseconds,
            MaxConcurrency = currentSettings.MaxConcurrentScans,
            PreferredSubnetMask = preferredMask
        };
    }

    private List<string> ParseCommunities(string text)
    {
        var items = text.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (items.Count == 0)
        {
            items.Add("public");
        }

        return items;
    }

    private void UpsertDevice(PrinterDevice device)
    {
        // Aplica nome e descrição salvos pelo MAC antes de subscrever para não disparar re-save
        if (!string.IsNullOrWhiteSpace(device.MacAddress))
        {
            var savedName = deviceNameService.GetName(device.MacAddress);
            if (savedName is not null) device.DeviceName = savedName;

            var savedDesc = deviceNameService.GetDescription(device.MacAddress);
            if (savedDesc is not null) device.SysDescription = savedDesc;
        }

        var existing = devices.FirstOrDefault(current =>
            (!string.IsNullOrWhiteSpace(device.MacAddress) &&
             string.Equals(current.MacAddress, device.MacAddress, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(current.IpAddress, device.IpAddress, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            device.PropertyChanged += OnDevicePropertyChanged;
            devices.Add(device);
        }
        else
        {
            existing.PropertyChanged -= OnDevicePropertyChanged;
            var index = devices.IndexOf(existing);
            device.PropertyChanged += OnDevicePropertyChanged;
            devices[index] = device;
        }

        ResortDevices();
        RefreshExportState();
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PrinterDevice device) return;

        if (string.IsNullOrWhiteSpace(device.MacAddress)) return;

        if (e.PropertyName == nameof(PrinterDevice.DeviceName))
            _ = PersistDeviceNameAsync(device.MacAddress, device.DeviceName, device.IpAddress);

        if (e.PropertyName == nameof(PrinterDevice.SysDescription))
            _ = deviceNameService.SetDescriptionAsync(device.MacAddress, device.SysDescription ?? string.Empty);

        if (e.PropertyName is nameof(PrinterDevice.WorkOffline) or nameof(PrinterDevice.QueuePaused))
        {
            toggleWorkOfflineCommand.RaiseCanExecuteChanged();
            resumePrintQueueCommand.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(HasAnyOfflinePrinter));
            RaisePropertyChanged(nameof(HasAnyPausedQueue));
        }
    }

    private async Task PersistDeviceNameAsync(string mac, string name, string ip)
    {
        await deviceNameService.SetNameAsync(mac, name);
        await printerWindowsService.RenameInstalledPrinterAsync(ip, name);
    }

    private void ResortDevices()
    {
        var sorted = SortDevices(devices);
        foreach (var d in devices) d.PropertyChanged -= OnDevicePropertyChanged;
        devices.Clear();
        foreach (var d in sorted)
        {
            d.PropertyChanged += OnDevicePropertyChanged;
            devices.Add(d);
        }
    }

    private static List<PrinterDevice> SortDevices(IEnumerable<PrinterDevice> source)
    {
        return source
            .OrderBy(item => ParseIpForSorting(item.IpAddress))
            .ToList();
    }

    private static uint ParseIpForSorting(string ipAddress)
    {
        try
        {
            return IpRangeService.ToUInt32(IpRangeService.ParseIpv4(ipAddress));
        }
        catch
        {
            return uint.MaxValue;
        }
    }

    private bool FilterDevice(object item)
    {
        if (item is not PrinterDevice device)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var term = SearchText.Trim();
        return (device.IpAddress?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (device.DeviceName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (device.MacAddress?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (device.SysDescription?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void RefreshExportState()
    {
        DevicesView.Refresh();
        UpdateCommands();
        RaisePropertyChanged(nameof(HasAnyOfflinePrinter));
        RaisePropertyChanged(nameof(HasAnyPausedQueue));
    }

    private void LoadInterfaces()
    {
        var interfaces = ipRangeService.GetActiveInterfaces();
        availableInterfaces.Clear();
        foreach (var iface in interfaces)
            availableInterfaces.Add(iface);

        // Mantém seleção anterior se ainda existir; senão escolhe a primeira
        if (selectedNetworkInterface is not null)
        {
            var match = availableInterfaces.FirstOrDefault(i => i.IpAddress == selectedNetworkInterface.IpAddress);
            SelectedNetworkInterface = match ?? availableInterfaces.FirstOrDefault();
        }
        else
        {
            SelectedNetworkInterface = availableInterfaces.FirstOrDefault();
        }
    }

    private void UpdateCommands()
    {
        startScanCommand.RaiseCanExecuteChanged();
        cancelScanCommand.RaiseCanExecuteChanged();
        clearResultsCommand.RaiseCanExecuteChanged();
        changeIpCommand.RaiseCanExecuteChanged();
        installDriverCommand.RaiseCanExecuteChanged();
        refreshInterfacesCommand.RaiseCanExecuteChanged();
        saveReportCommand.RaiseCanExecuteChanged();
        clearQueueCommand.RaiseCanExecuteChanged();
        clearSpoolerCommand.RaiseCanExecuteChanged();
        pauseQueueCommand.RaiseCanExecuteChanged();
        sendTestPrintCommand.RaiseCanExecuteChanged();
        changeUsbPortCommand.RaiseCanExecuteChanged();
        sharePrinterCommand.RaiseCanExecuteChanged();
        toggleWorkOfflineCommand.RaiseCanExecuteChanged();
        resumePrintQueueCommand.RaiseCanExecuteChanged();
    }

    private bool CanInstallDriver() => !IsScanning;

    private async Task InstallDriverAsync()
    {
        var confirm = MessageBox.Show(
            "Iniciar a instalacao do driver de impressora?\n\nO instalador solicitara permissao de administrador.",
            "Instalar driver",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        StatusMessage = "Iniciando instalacao do driver...";
        var (success, message) = await driverService.InstallEmbeddedDriverAsync();

        MessageBox.Show(
            message,
            success ? "Instalacao concluida" : "Erro na instalacao",
            MessageBoxButton.OK,
            success ? MessageBoxImage.Information : MessageBoxImage.Error);

        StatusMessage = success ? "Driver instalado com sucesso." : $"Falha: {message}";
    }

    private async Task SaveReportAsync()
    {
        var fileName = $"impressoras_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt";
        var filePath = Path.Combine(@"C:\", fileName);

        var sb = new StringBuilder();
        sb.AppendLine("RELATORIO DE VARREDURA DE IMPRESSORAS");
        sb.AppendLine($"Data/Hora : {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"Total     : {devices.Count} impressora(s) encontrada(s)");
        sb.AppendLine(new string('=', 120));
        sb.AppendLine();
        sb.AppendFormat(
            "{0,-17} {1,-30} {2,-20} {3,-16} {4,-16} {5,-14} {6,-5} {7,-5} {8,-5} {9,-5} {10}",
            "IP", "Nome", "MAC", "Mascara", "Gateway", "DHCP", "SNMP", "9100", "515", "631", "Descricao");
        sb.AppendLine();
        sb.AppendLine(new string('-', 120));

        foreach (var d in devices)
        {
            sb.AppendFormat(
                "{0,-17} {1,-30} {2,-20} {3,-16} {4,-16} {5,-14} {6,-5} {7,-5} {8,-5} {9,-5} {10}",
                d.IpAddress,
                Clip(d.DeviceName, 28),
                d.MacAddress,
                d.DisplaySubnetMask,
                d.DisplayGateway,
                d.DhcpStatus,
                d.SnmpResponded ? "Sim" : "Nao",
                d.Port9100Open  ? "Sim" : "Nao",
                d.Port515Open   ? "Sim" : "Nao",
                d.Port631Open   ? "Sim" : "Nao",
                d.SysDescription ?? string.Empty);
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
        StatusMessage = $"Relatorio salvo: {filePath}";
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    private async Task ClearQueueAsync()
    {
        var ip = selectedDevice?.IpAddress;
        var target = ip is not null ? $"impressora {ip}" : "todas as impressoras";

        var confirm = MessageBox.Show(
            $"Deseja limpar a fila de impressão de {target}?\n\nTodos os trabalhos pendentes serão cancelados.",
            "Limpar Fila",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        StatusMessage = "Limpando fila de impressão...";
        await printerWindowsService.ClearPrintQueueAsync(ip);
        StatusMessage = $"Fila limpa: {target}.";
    }

    private async Task ClearSpoolerAsync()
    {
        var confirm = MessageBox.Show(
            "Deseja limpar o Spooler de impressão?\n\n" +
            "Isso vai parar o serviço Spooler, apagar todos os arquivos de fila e reiniciá-lo.\n" +
            "Uma janela de confirmação de administrador (UAC) será exibida.",
            "Limpar Spooler",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        StatusMessage = "Aguardando limpeza do Spooler...";
        await printerWindowsService.ClearSpoolerAsync();
        StatusMessage = "Spooler reiniciado.";
    }

    private async Task TogglePauseQueueAsync()
    {
        if (selectedDevice is null) return;

        StatusMessage = $"Alternando fila de {selectedDevice.IpAddress}...";
        var result = await printerWindowsService.TogglePauseQueueAsync(selectedDevice.IpAddress);

        StatusMessage = result switch
        {
            true  => $"Fila pausada: {selectedDevice.IpAddress}.",
            false => $"Fila retomada: {selectedDevice.IpAddress}.",
            null  => $"Impressora {selectedDevice.IpAddress} não encontrada nas filas instaladas."
        };
    }

    private async Task SendTestPrintAsync()
    {
        if (selectedDevice is null) return;

        var confirm = MessageBox.Show(
            $"Enviar página de teste para {selectedDevice.DeviceName} ({selectedDevice.IpAddress})?",
            "Impressão de Teste",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        StatusMessage = $"Enviando página de teste para {selectedDevice.IpAddress}...";

        // Tenta ESC/POS direto via TCP 9100 (não requer driver instalado)
        var ok = await printerIpService.SendDirectTestPageAsync(
            selectedDevice.IpAddress,
            selectedDevice.DeviceName ?? "",
            selectedDevice.SubnetMask ?? "",
            selectedDevice.Gateway ?? "");

        if (ok)
        {
            StatusMessage = $"Impressão de teste enviada para {selectedDevice.IpAddress}.";
        }
        else
        {
            // Fallback: página de teste via Windows (requer driver instalado)
            await printerWindowsService.SendTestPageAsync(selectedDevice.IpAddress);
            StatusMessage = $"Impressão de teste enviada para {selectedDevice.IpAddress} (via Windows).";
        }
    }

    private async Task SharePrinterAsync()
    {
        if (selectedDevice is null) return;

        StatusMessage = $"Alterando compartilhamento de {selectedDevice.IpAddress}...";
        var result = await printerWindowsService.ToggleSharePrinterAsync(selectedDevice.IpAddress);

        if (result is null)
        {
            // Driver não instalado — abre diálogo de aviso com atalho para Utilitários
            var dlg = new Views.DriverRequiredDialog
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner
            };
            StatusMessage = $"Impressora {selectedDevice.IpAddress} nao encontrada — driver necessario.";
            if (dlg.ShowDialog() == true)
                await InstallDriverAsync();
            return;
        }

        StatusMessage = result == true
            ? $"Impressora {selectedDevice.IpAddress} compartilhada na rede."
            : $"Compartilhamento removido de {selectedDevice.IpAddress}.";
    }

    // ── Impressoras instaladas (USB + rede) ───────────────────────────────────
    private async Task ScanInstalledPrintersAsync()
    {
        // USB: substituição direta (porta USB é chave única)
        var usbPrinters = await printerWindowsService.GetUsbPrintersAsync();
        foreach (var d in usbPrinters)
            Application.Current.Dispatcher.Invoke(() => UpsertDevice(d));

        // Rede instalada: merge — se IP já existe na lista, enriquece sem substituir
        var netPrinters = await printerWindowsService.GetInstalledNetworkPrintersAsync();
        foreach (var d in netPrinters)
            Application.Current.Dispatcher.Invoke(() => UpsertInstalledNetworkDevice(d));
    }

    private void UpsertInstalledNetworkDevice(PrinterDevice installed)
    {
        var existing = devices.FirstOrDefault(d =>
            string.Equals(d.IpAddress, installed.IpAddress, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            // Apenas enriquece os campos de instalação; preserva dados do scan de rede
            existing.IsInstalled          = true;
            existing.InstalledPrinterName = installed.InstalledPrinterName;
            existing.WorkOffline          = installed.WorkOffline;
            existing.QueuePaused          = installed.QueuePaused;
            if (string.IsNullOrWhiteSpace(existing.SysDescription) && !string.IsNullOrWhiteSpace(installed.SysDescription))
                existing.SysDescription = installed.SysDescription;
        }
        else
        {
            // IP não encontrado pelo scan — adiciona como entrada nova
            installed.PropertyChanged += OnDevicePropertyChanged;
            devices.Add(installed);
            ResortDevices();
            RefreshExportState();
        }
    }

    private async Task ToggleWorkOfflineAsync()
    {
        if (selectedDevice is null || string.IsNullOrEmpty(selectedDevice.InstalledPrinterName)) return;

        bool newState = !selectedDevice.WorkOffline;
        StatusMessage = $"Alterando modo offline de {selectedDevice.DeviceName}...";
        var ok = await printerWindowsService.SetWorkOfflineAsync(selectedDevice.InstalledPrinterName, newState);
        if (ok)
        {
            selectedDevice.WorkOffline = newState;
            StatusMessage = newState
                ? $"{selectedDevice.DeviceName} definida como offline."
                : $"{selectedDevice.DeviceName} voltou ao modo online.";
        }
        else
        {
            StatusMessage = $"Nao foi possivel alterar o modo offline de {selectedDevice.DeviceName}.";
        }
    }

    private async Task ResumePrintQueueSelectedAsync()
    {
        if (selectedDevice is null || !selectedDevice.QueuePaused) return;

        StatusMessage = $"Retomando fila de {selectedDevice.DeviceName}...";
        if (!string.IsNullOrEmpty(selectedDevice.InstalledPrinterName))
            await printerWindowsService.ResumePrintQueueByNameAsync(selectedDevice.InstalledPrinterName);
        else
            await printerWindowsService.TogglePauseQueueAsync(selectedDevice.IpAddress);

        selectedDevice.QueuePaused = false;
        StatusMessage = $"Fila retomada: {selectedDevice.DeviceName}.";
    }

    private async Task ChangeUsbPortAsync()
    {
        if (selectedDevice?.IsUsbDevice != true) return;

        var dlg = new Views.ChangeUsbPortDialog(
            selectedDevice.DeviceName,
            selectedDevice.IpAddress,
            printerWindowsService)
        {
            Owner = Application.Current.MainWindow,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner
        };

        if (dlg.ShowDialog() == true)
        {
            // Atualiza a porta exibida na lista e re-faz scan USB
            StatusMessage = "Porta USB alterada. Atualizando lista...";
            await ScanInstalledPrintersAsync();
            StatusMessage = "Lista USB atualizada.";
        }
    }

    private void ToggleDarkMode()
    {
        IsDarkMode = !IsDarkMode;
        ApplyTheme(IsDarkMode);
        currentSettings.DarkModeEnabled = IsDarkMode;
        _ = settingsService.SaveSettingsAsync(currentSettings);
    }

    private static void ApplyTheme(bool dark)
    {
        var theme = dark ? "Dark" : "Light";
        var dict  = new System.Windows.ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Resources/Themes/{theme}.xaml")
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(dict);
    }

    private void OpenUtilitarios()
    {
        var vm     = new UtilitariosViewModel(utilitariosService);
        var dialog = new UtilitariosWindow(vm) { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    private static void OpenPrinterControl()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "explorer.exe",
            Arguments       = "shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}",
            UseShellExecute = true
        });
    }

    private void OpenChangeIpDialog()
    {
        if (selectedDevice is null) return;

        var dialog = new ChangeIpDialog(
            selectedDevice.IpAddress,
            selectedDevice.SubnetMask ?? string.Empty,
            selectedDevice.Gateway ?? string.Empty,
            selectedDevice.MacAddress ?? string.Empty,
            printerIpService,
            selectedDevice.DeviceName ?? string.Empty,
            selectedDevice.SysDescription ?? string.Empty)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            var vm = (ChangeIpViewModel)dialog.DataContext;
            selectedDevice.IpAddress = vm.NewIp;
            RefreshExportState();
        }
    }
}

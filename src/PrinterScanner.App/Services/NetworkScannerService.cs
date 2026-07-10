using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using PrinterScanner.App.Models;

namespace PrinterScanner.App.Services;

public sealed class NetworkScannerService
{
    private static readonly Regex MacRegex       = new(@"([0-9a-fA-F]{2}[:-]){5}([0-9a-fA-F]{2})", RegexOptions.Compiled);
    private static readonly Regex HtmlTitleRegex = new(@"<title[^>]*>\s*([^<]{2,80}?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HttpClient HttpProbe  = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(3) }) { Timeout = TimeSpan.FromSeconds(4) };
    private readonly IpRangeService ipRangeService;
    private readonly FileLogService logService;

    public NetworkScannerService(IpRangeService ipRangeService, FileLogService logService)
    {
        this.ipRangeService = ipRangeService;
        this.logService = logService;
    }

    public async Task ScanAsync(
        ScanRequest request,
        IProgress<ScanProgress> progress,
        Action<PrinterDevice> onDeviceDiscovered,
        CancellationToken cancellationToken)
    {
        var targets = ResolveTargets(request);
        var total = targets.Count;
        var processed = 0;
        var maxConcurrency = request.MaxConcurrency > 0
            ? request.MaxConcurrency
            : Math.Clamp(Environment.ProcessorCount * 8, 8, 64);

        using var throttler = new SemaphoreSlim(maxConcurrency);
        var tasks = targets.Select(async targetIp =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                var device = await ProbeAddressAsync(targetIp, request, cancellationToken);
                if (device is not null && device.IsLikelyPrinter)
                {
                    onDeviceDiscovered(device);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logService.LogError($"Falha ao varrer {targetIp}.", ex);
            }
            finally
            {
                var current = Interlocked.Increment(ref processed);
                progress.Report(new ScanProgress
                {
                    ProcessedCount = current,
                    TotalCount = total,
                    CurrentIp = targetIp
                });
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    public IReadOnlyList<string> ResolveTargets(ScanRequest request)
    {
        return request.Mode switch
        {
            ScanMode.SubredeAtual => ipRangeService.ExpandCurrentSubnet(request.SelectedLocalIP).Select(ip => ip.ToString()).ToList(),
            ScanMode.FaixaManual => ipRangeService.ExpandManualRange(request.StartIp ?? string.Empty, request.EndIp ?? string.Empty).Select(ip => ip.ToString()).ToList(),
            ScanMode.Cidr => ipRangeService.ExpandCidr(request.CidrNotation ?? string.Empty).Select(ip => ip.ToString()).ToList(),
            _ => throw new InvalidOperationException("Modo de varredura nao suportado.")
        };
    }

    private async Task<PrinterDevice?> ProbeAddressAsync(string ipAddress, ScanRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pingSuccess = await PingAsync(ipAddress, request.TimeoutMilliseconds);
        if (!pingSuccess)
        {
            return null;
        }

        var tcp9100 = await IsPortOpenAsync(ipAddress, 9100, request.TimeoutMilliseconds, cancellationToken);
        var tcp515 = await IsPortOpenAsync(ipAddress, 515, request.TimeoutMilliseconds, cancellationToken);
        var tcp631 = await IsPortOpenAsync(ipAddress, 631, request.TimeoutMilliseconds, cancellationToken);

        var snmpData = await TryReadSnmpAsync(ipAddress, request.Communities, request.TimeoutMilliseconds, cancellationToken);
        var reverseDns = await TryResolveDnsAsync(ipAddress);
        var mac = await TryReadMacAddressAsync(ipAddress, cancellationToken);

        var isLikelyPrinter = tcp9100 || tcp515 || tcp631 || snmpData.IsPrinter;
        if (!isLikelyPrinter)
        {
            return null;
        }

        // Camadas adicionais apenas quando SNMP não retornou modelo
        var needsDeepProbe = string.IsNullOrWhiteSpace(snmpData.HrDeviceDescr)
                          && string.IsNullOrWhiteSpace(snmpData.PrinterName);

        var httpModel   = needsDeepProbe ? await TryReadHttpModelAsync(ipAddress, cancellationToken) : null;
        var escPosModel = (needsDeepProbe && string.IsNullOrWhiteSpace(httpModel) && tcp9100)
                          ? await TryReadEscPosModelAsync(ipAddress, request.TimeoutMilliseconds, cancellationToken)
                          : null;

        var preferredName = SelectPreferredName(snmpData.HrDeviceDescr, snmpData.PrinterName, httpModel, escPosModel, snmpData.SysDescription, snmpData.SysName);
        return new PrinterDevice
        {
            IpAddress = ipAddress,
            SubnetMask = snmpData.SubnetMask ?? request.PreferredSubnetMask,
            Gateway = snmpData.Gateway,
            DhcpStatus = string.IsNullOrWhiteSpace(snmpData.DhcpStatus) ? "Nao Identificado" : snmpData.DhcpStatus,
            MacAddress = string.IsNullOrWhiteSpace(mac) ? string.Empty : mac,
            DeviceName = string.IsNullOrWhiteSpace(preferredName) ? "Dispositivo de Impressao Desconhecido" : preferredName,
            DnsName = reverseDns,
            SysDescription = snmpData.SysDescription,
            SnmpResponded = snmpData.SnmpResponded,
            Port9100Open = tcp9100,
            Port515Open = tcp515,
            Port631Open = tcp631,
            IsLikelyPrinter = true,
            DiscoveredAt = DateTimeOffset.Now
        };
    }

    private static async Task<bool> PingAsync(string ipAddress, int timeoutMilliseconds)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(ipAddress, timeoutMilliseconds);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> IsPortOpenAsync(string ipAddress, int port, int timeoutMilliseconds, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMilliseconds);

        try
        {
            await client.ConnectAsync(IPAddress.Parse(ipAddress), port, timeoutCts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string SelectPreferredName(string? hrDeviceDescr, string? printerName, string? httpModel, string? escPosModel, string? sysDescription, string? sysName)
    {
        if (!string.IsNullOrWhiteSpace(hrDeviceDescr))
            return hrDeviceDescr;

        if (!string.IsNullOrWhiteSpace(printerName))
            return printerName;

        if (!string.IsNullOrWhiteSpace(httpModel))
            return httpModel;

        if (!string.IsNullOrWhiteSpace(escPosModel))
            return escPosModel;

        if (!string.IsNullOrWhiteSpace(sysDescription) && sysDescription.Length <= 80)
        {
            // Tenta extrair só a marca+modelo de strings de firmware como "7.010CR2N Elgin i8 _CHINA,GB18030"
            var clean = CleanFirmwareString(sysDescription);
            return clean ?? sysDescription;
        }

        if (!string.IsNullOrWhiteSpace(sysName))
            return sysName;

        return string.Empty;
    }

    // Remove prefixo de versão de firmware e sufixo de país/charset de strings brutas OEM
    private static string? CleanFirmwareString(string raw)
    {
        var m = Regex.Match(raw, @"(?i)(elgin\s+\w+)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.Trim();

        var b = Regex.Match(raw, @"(?i)(bematech\s+[\w-]+)", RegexOptions.IgnoreCase);
        if (b.Success) return b.Groups[1].Value.Trim();

        return null;
    }

    private static string? CleanSnmpString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        // SharpSnmpLib retorna "NoSuchObject" / "NoSuchInstance" quando o OID não existe
        if (value.StartsWith("NoSuch", StringComparison.OrdinalIgnoreCase))
            return null;
        return value.Trim();
    }


    private async Task<string?> TryResolveDnsAsync(string ipAddress)
    {
        try
        {
            var host = await Dns.GetHostEntryAsync(IPAddress.Parse(ipAddress));
            return host.HostName;
        }
        catch (Exception ex)
        {
            logService.LogError($"DNS reverso nao disponivel para {ipAddress}.", ex);
            return null;
        }
    }

    private async Task<string?> TryReadMacAddressAsync(string ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.Contains(ipAddress, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var match = MacRegex.Match(line);
                if (match.Success)
                {
                    return NormalizeMacAddress(match.Value);
                }
            }
        }
        catch (Exception ex)
        {
            logService.LogError($"Falha ao obter MAC para {ipAddress}.", ex);
        }

        return null;
    }

    private static string NormalizeMacAddress(string macAddress)
    {
        return macAddress.Replace(':', '-').ToUpperInvariant();
    }

    private static async Task<string?> TryReadEscPosModelAsync(string ipAddress, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            await client.ConnectAsync(IPAddress.Parse(ipAddress), 9100, cts.Token);

            using var stream = client.GetStream();
            stream.WriteTimeout = timeoutMs;
            stream.ReadTimeout = timeoutMs;

            // GS I n — consulta somente leitura, não dispara impressão
            // 0x41 = firmware/modelo combinado  (ex: "_7.010CR2N Elgin i8 _CHINA,GB18030")
            // 0x42 = fabricante                 (ex: "_EPSON")
            // 0x43 = nome do modelo             (ex: "_TM-T88III")
            // Cada resposta é terminada por NUL (0x00)
            var query = new byte[] { 0x1D,0x49,0x41, 0x1D,0x49,0x42, 0x1D,0x49,0x43 };
            await stream.WriteAsync(query, cts.Token);

            var buffer = new byte[512];
            var totalRead = 0;
            try
            {
                int read;
                while (totalRead < buffer.Length)
                {
                    read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cts.Token);
                    if (read == 0) break;
                    totalRead += read;
                    await Task.Delay(50, cts.Token);
                    if (!stream.DataAvailable) break;
                }
            }
            catch (IOException) { }
            catch (OperationCanceledException) { }

            if (totalRead == 0) return null;

            // Separa as respostas individuais por NUL e tira o prefixo "_"
            var parts = SplitByNul(buffer, totalRead);
            var firmware     = parts.Count > 0 ? parts[0].TrimStart('_').Trim() : null;
            var manufacturer = parts.Count > 1 ? parts[1].TrimStart('_').Trim() : null;
            var model        = parts.Count > 2 ? parts[2].TrimStart('_').Trim() : null;

            return BuildModelName(firmware, manufacturer, model)
                   ?? ResolveModelFromId(buffer[0]);
        }
        catch
        {
            return null;
        }
    }

    private static string? BuildModelName(string? firmware, string? manufacturer, string? model)
    {
        // Elgin/OEM com marca embutida no firmware: "7.010CR2N Elgin i8 _CHINA,GB18030"
        if (firmware != null)
        {
            var m = Regex.Match(firmware, @"(?i)(elgin\s+\w+)", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();

            var b = Regex.Match(firmware, @"(?i)(bematech\s+[\w-]+)", RegexOptions.IgnoreCase);
            if (b.Success) return b.Groups[1].Value.Trim();
        }

        // Fabricante + modelo explícitos (ex: EPSON + TM-T88III)
        if (!string.IsNullOrWhiteSpace(manufacturer)
            && !string.IsNullOrWhiteSpace(model)
            && !model.Contains("ESC/POS", StringComparison.OrdinalIgnoreCase))
        {
            var mfr = char.ToUpper(manufacturer[0]) + manufacturer[1..].ToLower();
            return $"{mfr} {model}";
        }

        // Só modelo disponível
        if (!string.IsNullOrWhiteSpace(model)
            && !model.Contains("ESC/POS", StringComparison.OrdinalIgnoreCase)
            && model.Length > 2)
            return model;

        // Firmware como fallback (omite sufixo de país)
        if (!string.IsNullOrWhiteSpace(firmware) && firmware.Length > 3)
        {
            var clean = Regex.Replace(firmware, @"_?CHINA.*$", "", RegexOptions.IgnoreCase).Trim().TrimEnd(',');
            if (clean.Length > 3) return clean;
        }

        return null;
    }

    private static List<string> SplitByNul(byte[] buf, int len)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i < len; i++)
        {
            if (buf[i] == 0x00)
            {
                if (i > start)
                    parts.Add(Encoding.ASCII.GetString(buf, start, i - start));
                start = i + 1;
            }
        }
        if (start < len)
            parts.Add(Encoding.ASCII.GetString(buf, start, len - start));
        return parts;
    }

    private static string? ResolveModelFromId(byte modelId)
    {
        return modelId switch
        {
            0x20 => "ELGIN i5",
            0x21 => "ELGIN i7",
            0x28 => "ELGIN i8",
            0x29 => "ELGIN i9",
            0x30 => "ELGIN i9 Full",
            0x41 => "Epson TM-T20",
            0x42 => "Epson TM-T88",
            0x43 => "Epson TM-T88VI",
            0x50 => "Bematech MP-4200",
            0x51 => "Bematech MP-100",
            _    => null
        };
    }

    private static async Task<string?> TryReadHttpModelAsync(string ipAddress, CancellationToken cancellationToken)
    {
        // Tenta porta 80 e 443; para impressoras térmicas/label sem SNMP
        foreach (var url in new[] { $"http://{ipAddress}/", $"http://{ipAddress}/index.html", $"http://{ipAddress}/status.html" })
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(4));

                var html = await HttpProbe.GetStringAsync(url, cts.Token);
                if (string.IsNullOrWhiteSpace(html)) continue;

                var match = HtmlTitleRegex.Match(html);
                if (!match.Success) continue;

                var title = match.Groups[1].Value.Trim();

                // Descarta títulos genéricos que não identificam o modelo
                var lower = title.ToLowerInvariant();
                if (lower.Length < 3 ||
                    lower is "index" or "home" or "login" or "welcome" or "untitled document" ||
                    lower.StartsWith("please", StringComparison.Ordinal) ||
                    lower.StartsWith("error", StringComparison.Ordinal))
                    continue;

                return title;
            }
            catch { }
        }

        return null;
    }

    private static async Task<SnmpProbeResult> TryReadSnmpAsync(
        string ipAddress,
        IReadOnlyList<string> communities,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(ipAddress), 161);

        foreach (var community in communities.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1. SNMPv2c — GET com todos os OIDs padrão de uma vez
            var result = await TrySnmpGetAsync(endpoint, VersionCode.V2, community, timeoutMilliseconds, cancellationToken);
            if (result is not null) return result;

            // 2. SNMPv1 — fallback para dispositivos mais antigos
            result = await TrySnmpGetAsync(endpoint, VersionCode.V1, community, timeoutMilliseconds, cancellationToken);
            if (result is not null) return result;

            // 3. Queries individuais — fallback para dispositivos que rejeitam GET com múltiplos OIDs
            result = await TrySnmpGetIndividualAsync(endpoint, community, timeoutMilliseconds, cancellationToken);
            if (result is not null) return result;
        }

        return new SnmpProbeResult();
    }

    private static async Task<SnmpProbeResult?> TrySnmpGetAsync(
        IPEndPoint endpoint, VersionCode version, string community, int timeout, CancellationToken ct)
    {
        try
        {
            return await Task.Run(() =>
            {
                var ip = endpoint.Address.ToString();
                var variables = new List<Variable>
                {
                    new(new ObjectIdentifier("1.3.6.1.2.1.1.1.0")),               // [0] sysDescr
                    new(new ObjectIdentifier("1.3.6.1.2.1.1.5.0")),               // [1] sysName
                    new(new ObjectIdentifier("1.3.6.1.2.1.43.5.1.1.16.1")),       // [2] prtGeneralPrinterName
                    new(new ObjectIdentifier("1.3.6.1.2.1.25.3.2.1.3.1")),        // [3] hrDeviceDescr
                    new(new ObjectIdentifier($"1.3.6.1.2.1.4.20.1.3.{ip}")),      // [4] ipAdEntNetMask
                    new(new ObjectIdentifier("1.3.6.1.2.1.4.21.1.7.0.0.0.0")),    // [5] default gateway
                    new(new ObjectIdentifier("1.3.6.1.4.1.11.2.4.3.5.13.0")),     // [6] HP: boot method
                    new(new ObjectIdentifier("1.3.6.1.4.1.1248.1.2.2.1.1.3.1")), // [7] Epson: IP config
                    new(new ObjectIdentifier("1.3.6.1.4.1.2435.2.3.9.4.2.1.4.3.1.0")), // [8] Brother
                    new(new ObjectIdentifier("1.3.6.1.4.1.641.2.1.2.1.3.1")),     // [9] Lexmark
                    new(new ObjectIdentifier("1.3.6.1.4.1.1602.1.2.1.6.0")),      // [10] Kyocera
                };

                var data = Messenger.Get(version, endpoint, new OctetString(community), variables, timeout);

                var sysDescription = CleanSnmpString(data.ElementAtOrDefault(0)?.Data?.ToString());
                var sysName        = CleanSnmpString(data.ElementAtOrDefault(1)?.Data?.ToString());
                var printerName    = CleanSnmpString(data.ElementAtOrDefault(2)?.Data?.ToString());
                var hrDeviceDescr  = CleanSnmpString(data.ElementAtOrDefault(3)?.Data?.ToString());
                var subnetMask     = CleanSnmpString(data.ElementAtOrDefault(4)?.Data?.ToString());
                var gateway        = CleanSnmpString(data.ElementAtOrDefault(5)?.Data?.ToString());

                var dhcpStatus =
                    (CleanSnmpString(data.ElementAtOrDefault(6)?.Data?.ToString()) is { } hp ? InterpretDhcpValue(hp, "hp") : null) ??
                    (CleanSnmpString(data.ElementAtOrDefault(7)?.Data?.ToString()) is { } eps ? InterpretDhcpValue(eps, "epson") : null) ??
                    (CleanSnmpString(data.ElementAtOrDefault(8)?.Data?.ToString()) is { } bro ? InterpretDhcpValue(bro, "brother") : null) ??
                    (CleanSnmpString(data.ElementAtOrDefault(9)?.Data?.ToString()) is { } lex ? InterpretDhcpValue(lex, "lexmark") : null) ??
                    (CleanSnmpString(data.ElementAtOrDefault(10)?.Data?.ToString()) is { } kyo ? InterpretDhcpValue(kyo, "kyocera") : null);

                return new SnmpProbeResult
                {
                    SnmpResponded = true,
                    IsPrinter = LooksLikePrinter(sysDescription) || !string.IsNullOrWhiteSpace(printerName) || !string.IsNullOrWhiteSpace(hrDeviceDescr),
                    SysDescription = sysDescription,
                    SysName = sysName,
                    PrinterName = printerName,
                    HrDeviceDescr = hrDeviceDescr,
                    SubnetMask = subnetMask,
                    Gateway = gateway,
                    DhcpStatus = dhcpStatus
                };
            }, ct);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<SnmpProbeResult?> TrySnmpGetIndividualAsync(
        IPEndPoint endpoint, string community, int timeout, CancellationToken ct)
    {
        static string? GetOne(IPEndPoint ep, VersionCode ver, string comm, string oid, int to)
        {
            try
            {
                var vars = new List<Variable> { new(new ObjectIdentifier(oid)) };
                var data = Messenger.Get(ver, ep, new OctetString(comm), vars, to);
                return CleanSnmpString(data.ElementAtOrDefault(0)?.Data?.ToString());
            }
            catch { return null; }
        }

        try
        {
            return await Task.Run(() =>
            {
                var ip = endpoint.Address.ToString();

                foreach (var version in new[] { VersionCode.V1, VersionCode.V2 })
                {
                    var sysDescr = GetOne(endpoint, version, community, "1.3.6.1.2.1.1.1.0", timeout);
                    if (sysDescr is null) continue;

                    var sysName     = GetOne(endpoint, version, community, "1.3.6.1.2.1.1.5.0", timeout);
                    var hrDescr     = GetOne(endpoint, version, community, "1.3.6.1.2.1.25.3.2.1.3.1", timeout);
                    var printerName = GetOne(endpoint, version, community, "1.3.6.1.2.1.43.5.1.1.16.1", timeout);
                    var subnetMask  = GetOne(endpoint, version, community, $"1.3.6.1.2.1.4.20.1.3.{ip}", timeout);
                    var gateway     = GetOne(endpoint, version, community, "1.3.6.1.2.1.4.21.1.7.0.0.0.0", timeout);

                    var dhcpStatus =
                        (GetOne(endpoint, version, community, "1.3.6.1.4.1.11.2.4.3.5.13.0", timeout) is { } hp ? InterpretDhcpValue(hp, "hp") : null) ??
                        (GetOne(endpoint, version, community, "1.3.6.1.4.1.1248.1.2.2.1.1.3.1", timeout) is { } eps ? InterpretDhcpValue(eps, "epson") : null) ??
                        (GetOne(endpoint, version, community, "1.3.6.1.4.1.2435.2.3.9.4.2.1.4.3.1.0", timeout) is { } bro ? InterpretDhcpValue(bro, "brother") : null) ??
                        (GetOne(endpoint, version, community, "1.3.6.1.4.1.641.2.1.2.1.3.1", timeout) is { } lex ? InterpretDhcpValue(lex, "lexmark") : null) ??
                        (GetOne(endpoint, version, community, "1.3.6.1.4.1.1602.1.2.1.6.0", timeout) is { } kyo ? InterpretDhcpValue(kyo, "kyocera") : null);

                    return new SnmpProbeResult
                    {
                        SnmpResponded = true,
                        IsPrinter = LooksLikePrinter(sysDescr) || !string.IsNullOrWhiteSpace(printerName) || !string.IsNullOrWhiteSpace(hrDescr),
                        SysDescription = sysDescr,
                        SysName = sysName,
                        PrinterName = printerName,
                        HrDeviceDescr = hrDescr,
                        SubnetMask = subnetMask,
                        Gateway = gateway,
                        DhcpStatus = dhcpStatus
                    };
                }
                return null;
            }, ct);
        }
        catch
        {
            return null;
        }
    }

    private static string? InterpretDhcpValue(string raw, string vendor)
    {
        if (!int.TryParse(raw, out var val)) return null;
        return vendor switch
        {
            "hp"      => val switch { 1 => "IP Fixo", 2 => "BOOTP", 3 => "DHCP", 4 => "DHCP", _ => null },
            "epson"   => val switch { 0 => "IP Fixo", 1 => "DHCP",  2 => "BOOTP", 3 => "Auto-IP", _ => null },
            "brother" => val switch { 0 => "DHCP",    1 => "IP Fixo", _ => null },
            "lexmark" => val switch { 1 => "IP Fixo", 3 => "DHCP",  _ => null },
            "kyocera" => val switch { 0 => "IP Fixo", 1 => "DHCP",  _ => null },
            _         => null
        };
    }

    // ── Broadcast Discovery ──────────────────────────────────────────────────
    // 4 etapas:
    //   1. Tabela ARP (vizinhos L2 recentemente vistos)
    //   2. Scan ativo de sub-redes vizinhas na porta 9100 (JP-800 e similares)
    //   3. Probe UDP broadcast 255.255.255.255 (SNMP / Rongta)
    //   4. Probe dos candidatos (ping + porta + SNMP opcional)

    public async Task BroadcastDiscoverAsync(
        IReadOnlyList<string> communities,
        IProgress<string> status,
        IProgress<double> progressPct,
        Action<PrinterDevice> onDeviceDiscovered,
        CancellationToken ct)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);

        // Etapa 1 (0-8%): Tabela ARP
        progressPct.Report(0);
        status.Report("Etapa 1/4 — Lendo tabela ARP da rede...");
        foreach (var ip in await GetArpTableAllIpsAsync(ct))
            candidates.Add(ip);
        progressPct.Report(8);

        // Etapa 2 (8-42%): Scan de sub-redes vizinhas para porta 9100
        // Encontra impressoras que mudaram de sub-rede (ex: 192.168.1.10 com PC em 192.168.0.x)
        var subnetIps = BuildAdjacentSubnetIps();
        if (subnetIps.Count > 0)
        {
            status.Report($"Etapa 2/4 — Sondando sub-redes vizinhas ({subnetIps.Count} IPs, porta 9100)...");
            var subnetFound = new System.Collections.Concurrent.ConcurrentBag<string>();
            int subnetTotal = subnetIps.Count;
            int subnetDone  = 0;

            using var subnetSem = new SemaphoreSlim(64);
            var subnetTasks = subnetIps.Select(async ip =>
            {
                await subnetSem.WaitAsync(ct);
                try
                {
                    if (await IsPortOpenAsync(ip, 9100, 350, ct))
                        subnetFound.Add(ip);
                }
                catch (OperationCanceledException) { throw; }
                catch { }
                finally
                {
                    int done = Interlocked.Increment(ref subnetDone);
                    progressPct.Report(8 + done * 34.0 / subnetTotal); // 8% → 42%
                    subnetSem.Release();
                }
            });
            await Task.WhenAll(subnetTasks);
            foreach (var ip in subnetFound) candidates.Add(ip);
        }
        progressPct.Report(42);

        // Etapa 3 (42-55%): UDP broadcast com deadline garantido
        status.Report($"Etapa 3/4 — Broadcast UDP (3s)... {candidates.Count} candidato(s) até agora");
        using var broadcastCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        broadcastCts.CancelAfter(3500);
        var broadcastTask = UdpBroadcastProbeAsync(broadcastCts.Token);
        for (int i = 0; i < 30 && !broadcastTask.IsCompleted; i++)
        {
            await Task.Delay(100, ct);
            progressPct.Report(42 + (i + 1) * 0.43); // 42% → 55%
        }
        IEnumerable<string> broadcastIps;
        try { broadcastIps = await broadcastTask; }
        catch (OperationCanceledException) { broadcastIps = []; }
        foreach (var ip in broadcastIps) candidates.Add(ip);
        progressPct.Report(55);

        if (candidates.Count == 0)
        {
            status.Report("Nenhum candidato encontrado.");
            progressPct.Report(100);
            return;
        }

        // Etapa 4 (55-100%): Probe por candidato
        status.Report($"Etapa 4/4 — Verificando {candidates.Count} candidato(s)...");
        int total  = candidates.Count;
        int probed = 0;
        int found  = 0;

        using var sem = new SemaphoreSlim(8);
        var tasks = candidates.Select(async ip =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var device = await ProbeBroadcastCandidateAsync(ip, communities, ct);
                if (device is not null)
                {
                    Interlocked.Increment(ref found);
                    onDeviceDiscovered(device);
                }
            }
            finally
            {
                int done = Interlocked.Increment(ref probed);
                progressPct.Report(55 + done * 45.0 / total); // 55% → 100%
                sem.Release();
            }
        });

        await Task.WhenAll(tasks);
        progressPct.Report(100);
        status.Report($"Busca ampliada concluida. {found} impressora(s) encontrada(s).");
    }

    // Gera IPs de sub-redes /24 adjacentes à(s) interface(s) local(is),
    // excluindo a própria sub-rede (já coberta pelo scan normal).
    private static List<string> BuildAdjacentSubnetIps()
    {
        var own = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                var parts = ua.Address.ToString().Split('.');
                if (parts.Length != 4 ||
                    !int.TryParse(parts[0], out int p0) ||
                    !int.TryParse(parts[1], out int p1) ||
                    !int.TryParse(parts[2], out int p2)) continue;

                if (p0 == 127) continue;
                own.Add($"{p0}.{p1}.{p2}"); // marca sub-rede atual

                // Para 192.168.x.y: varre 0-9 (sub-redes comuns) + p2±10 (vizinhança do PC)
                // Para 10.a.b.c: varre 10.a.b-3..b+3.1-254 (exceto própria)
                IEnumerable<int> thirds;
                if (p0 == 192 && p1 == 168)
                {
                    var set = new HashSet<int>(Enumerable.Range(0, 10));
                    for (int i = Math.Max(0, p2 - 10); i <= Math.Min(254, p2 + 10); i++)
                        set.Add(i);
                    thirds = set;
                }
                else
                {
                    thirds = Enumerable.Range(Math.Max(0, p2 - 3), 7);
                }

                foreach (int c in thirds)
                {
                    var prefix = $"{p0}.{p1}.{c}";
                    if (own.Contains(prefix)) continue;
                    for (int d = 1; d <= 254; d++)
                        result.Add($"{prefix}.{d}");
                }
            }
        }
        return result;
    }

    // Retorna prefixos /24 adjacentes que o PC NÃO tem interface local
    // (precisam de IP temporário para serem alcançados diretamente).
    public static IReadOnlyList<string> GetUnreachableAdjacentSubnetPrefixes()
    {
        var localPrefixes     = new HashSet<string>(StringComparer.Ordinal);
        var candidatePrefixes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                var parts = ua.Address.ToString().Split('.');
                if (parts.Length != 4 ||
                    !int.TryParse(parts[0], out int p0) ||
                    !int.TryParse(parts[1], out int p1) ||
                    !int.TryParse(parts[2], out int p2)) continue;
                if (p0 == 127) continue;

                var own = $"{p0}.{p1}.{p2}";
                localPrefixes.Add(own);

                IEnumerable<int> thirds;
                if (p0 == 192 && p1 == 168)
                {
                    var set = new HashSet<int>(Enumerable.Range(0, 10));
                    for (int i = Math.Max(0, p2 - 10); i <= Math.Min(254, p2 + 10); i++)
                        set.Add(i);
                    thirds = set;
                }
                else
                {
                    thirds = Enumerable.Range(Math.Max(0, p2 - 3), 7);
                }

                foreach (int c in thirds)
                {
                    var prefix = $"{p0}.{p1}.{c}";
                    if (prefix != own) candidatePrefixes.Add(prefix);
                }
            }
        }

        return candidatePrefixes.Where(p => !localPrefixes.Contains(p)).ToList();
    }

    // Lê TODOS os IPs da tabela ARP do Windows (arp -a)
    private static async Task<IEnumerable<string>> GetArpTableAllIpsAsync(CancellationToken ct)
    {
        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "arp", Arguments = "-a",
                    RedirectStandardOutput = true, CreateNoWindow = true, UseShellExecute = false
                }
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            // Extrai IPs (ex: "  192.168.1.100      aa-bb-cc ...")
            return Regex.Matches(output, @"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b")
                        .Select(m => m.Groups[1].Value)
                        .Where(ip => !ip.StartsWith("224.") && !ip.StartsWith("239.")
                                  && !ip.StartsWith("255.") && ip != "0.0.0.0")
                        .Distinct();
        }
        catch { return []; }
    }

    // Envia probe UDP para 255.255.255.255 e escuta respostas por 5s
    // Retorna todos os IPs que responderam (dispositivos no mesmo segmento L2)
    private static async Task<IEnumerable<string>> UdpBroadcastProbeAsync(CancellationToken ct)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var udp = new UdpClient();
            udp.EnableBroadcast = true;
            udp.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0));

            // Probe 1: porta 3000 (protocolo Rongta)
            byte[] probe3000 = [0x1F, 0x01];
            await udp.SendAsync(probe3000, new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, 3000));

            // Probe 2: SNMP GET sysDescr na porta 161
            try
            {
                var snmpVars = new List<Lextm.SharpSnmpLib.Variable>
                    { new(new Lextm.SharpSnmpLib.ObjectIdentifier("1.3.6.1.2.1.1.1.0")) };
                var snmpReq = new Lextm.SharpSnmpLib.Messaging.GetRequestMessage(
                    1, Lextm.SharpSnmpLib.VersionCode.V1,
                    new Lextm.SharpSnmpLib.OctetString("public"), snmpVars);
                var snmpBytes = snmpReq.ToBytes();
                await udp.SendAsync(snmpBytes, new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, 161));
            }
            catch { }

            // Escuta até o CT disparar (broadcastCts cancela após 3,5s)
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await udp.ReceiveAsync(ct); // CT necessário — ReceiveTimeout ignora async
                    found.Add(result.RemoteEndPoint.Address.ToString());
                }
                catch (OperationCanceledException) { break; }
                catch { break; }
            }
        }
        catch { }
        return found;
    }

    // Probe rápido para candidato de broadcast.
    // Estratégia: portas primeiro (TCP async, não bloqueia thread pool),
    // SNMP apenas se alguma porta indicar impressora (evita starvation do pool).
    private async Task<PrinterDevice?> ProbeBroadcastCandidateAsync(
        string ip, IReadOnlyList<string> communities, CancellationToken ct)
    {
        // 1. Ping rápido — descarta imediatamente quem não responde
        if (!await PingAsync(ip, 500)) return null;

        // 2. Portas em paralelo (TCP async — não bloqueia thread pool)
        //    Inclui 9100 (RAW/Rongta) pois JP-800 não tem 515/631 mas tem 9100
        var port9100Task = IsPortOpenAsync(ip, 9100, 700, ct);
        var port515Task  = IsPortOpenAsync(ip, 515,  700, ct);
        var port631Task  = IsPortOpenAsync(ip, 631,  700, ct);
        await Task.WhenAll(port9100Task, port515Task, port631Task);
        bool port9100 = port9100Task.Result;
        bool port515  = port515Task.Result;
        bool port631  = port631Task.Result;

        // 3. SNMP só para quem tem pelo menos uma porta de impressão aberta
        SnmpProbeResult snmp = new();
        if (port9100 || port515 || port631)
            snmp = await TryReadSnmpAsync(ip, communities, 1000, ct);
        else
            return null;

        var mac  = await TryReadMacAddressAsync(ip, ct);
        var name = SelectPreferredName(snmp.HrDeviceDescr, snmp.PrinterName, null, null, snmp.SysDescription, snmp.SysName);

        return new PrinterDevice
        {
            IpAddress       = ip,
            SubnetMask      = snmp.SubnetMask,
            Gateway         = snmp.Gateway,
            DhcpStatus      = string.IsNullOrWhiteSpace(snmp.DhcpStatus) ? "Nao Identificado" : snmp.DhcpStatus,
            MacAddress      = mac ?? string.Empty,
            DeviceName      = string.IsNullOrWhiteSpace(name) ? "Impressora (broadcast)" : name,
            DnsName         = string.Empty,
            SysDescription  = snmp.SysDescription,
            SnmpResponded   = snmp.SnmpResponded,
            Port9100Open    = port9100,
            Port515Open     = port515,
            Port631Open     = port631,
            IsLikelyPrinter = true,
            DiscoveredAt    = DateTimeOffset.Now
        };
    }

    private static bool LooksLikePrinter(string? sysDescription)
    {
        if (string.IsNullOrWhiteSpace(sysDescription))
        {
            return false;
        }

        var text = sysDescription.ToLowerInvariant();
        return text.Contains("printer", StringComparison.Ordinal) ||
               text.Contains("impressora", StringComparison.Ordinal) ||
               text.Contains("laserjet", StringComparison.Ordinal) ||
               text.Contains("deskjet", StringComparison.Ordinal) ||
               text.Contains("epson", StringComparison.Ordinal) ||
               text.Contains("brother", StringComparison.Ordinal) ||
               text.Contains("elgin", StringComparison.Ordinal) ||
               text.Contains("lexmark", StringComparison.Ordinal) ||
               text.Contains("xerox", StringComparison.Ordinal) ||
               text.Contains("ricoh", StringComparison.Ordinal) ||
               text.Contains("kyocera", StringComparison.Ordinal) ||
               text.Contains("samsung", StringComparison.Ordinal) ||
               text.Contains("canon", StringComparison.Ordinal);
    }

    private sealed class SnmpProbeResult
    {
        public bool SnmpResponded { get; init; }
        public bool IsPrinter { get; init; }
        public string? SysDescription { get; init; }
        public string? SysName { get; init; }
        public string? PrinterName { get; init; }
        public string? HrDeviceDescr { get; init; }
        public string? SubnetMask { get; init; }
        public string? Gateway { get; init; }
        public string? DhcpStatus { get; init; }
    }
}

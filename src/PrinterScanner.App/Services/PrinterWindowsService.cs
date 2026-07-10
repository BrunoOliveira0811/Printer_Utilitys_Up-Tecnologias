using System.Diagnostics;
using System.IO;
using System.Text;
using PrinterScanner.App.Models;

namespace PrinterScanner.App.Services;

public sealed class PrinterWindowsService
{
    // ── Limpar Fila de Impressoras ──────────────────────────────────────────
    // Se um IP for fornecido, limpa apenas a fila da impressora com esse IP.
    // Caso contrário, limpa as filas de todas as impressoras instaladas.
    public async Task ClearPrintQueueAsync(string? ip)
    {
        string script;
        if (string.IsNullOrWhiteSpace(ip))
        {
            script = new StringBuilder()
                .AppendLine("try {")
                .AppendLine("  Get-Printer -ErrorAction SilentlyContinue | ForEach-Object {")
                .AppendLine("    Get-PrintJob -PrinterName $_.Name -ErrorAction SilentlyContinue | Remove-PrintJob -ErrorAction SilentlyContinue")
                .AppendLine("  }")
                .AppendLine("} catch {}")
                .ToString();
        }
        else
        {
            var safeIp = ip.Replace("'", "");
            script = new StringBuilder()
                .AppendLine("try {")
                .AppendLine("  $ip = '" + safeIp + "'")
                .AppendLine("  $port = Get-PrinterPort -ErrorAction SilentlyContinue | Where-Object { $_.PrinterHostAddress -eq $ip } | Select-Object -First 1")
                .AppendLine("  if ($port) {")
                .AppendLine("    $pname = (Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.PortName -eq $port.Name } | Select-Object -First 1).Name")
                .AppendLine("    if ($pname) { Get-PrintJob -PrinterName $pname -ErrorAction SilentlyContinue | Remove-PrintJob -ErrorAction SilentlyContinue }")
                .AppendLine("  } else {")
                .AppendLine("    Get-Printer -ErrorAction SilentlyContinue | ForEach-Object {")
                .AppendLine("      Get-PrintJob -PrinterName $_.Name -ErrorAction SilentlyContinue | Remove-PrintJob -ErrorAction SilentlyContinue")
                .AppendLine("    }")
                .AppendLine("  }")
                .AppendLine("} catch {}")
                .ToString();
        }
        await RunHiddenPsAsync(script);
    }

    // ── Limpar Spooler ──────────────────────────────────────────────────────
    // Para o serviço Spooler, apaga os arquivos da fila e reinicia.
    // Executa em janela elevada (UAC) pois requer administrador.
    // IMPORTANTE: deve ser async Task para que o finally só execute APÓS o
    // processo terminar — senão o arquivo .cmd é apagado antes de ser lido.
    public async Task ClearSpoolerAsync()
    {
        var script = "@echo off\r\n" +
                     "echo Parando servico Spooler...\r\n" +
                     "net stop Spooler\r\n" +
                     "echo Limpando arquivos de fila...\r\n" +
                     "del /Q /F \"%SystemRoot%\\System32\\spool\\PRINTERS\\*.*\" 2>nul\r\n" +
                     "echo Reiniciando servico Spooler...\r\n" +
                     "net start Spooler\r\n" +
                     "echo.\r\n" +
                     "echo Spooler limpo com sucesso.\r\n" +
                     "timeout /t 3 >nul\r\n";

        var tempCmd = Path.Combine(Path.GetTempPath(), $"clear_spooler_{Guid.NewGuid():N}.cmd");
        File.WriteAllText(tempCmd, script, Encoding.GetEncoding(850));
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName        = "cmd.exe",
                Arguments       = $"/c \"{tempCmd}\"",
                Verb            = "runas",
                UseShellExecute = true
            });
            if (proc is not null) await proc.WaitForExitAsync(); // finally só roda após aqui
        }
        catch { }
        finally { try { File.Delete(tempCmd); } catch { } }
    }

    // ── Pausar/Retomar Fila de Impressão ───────────────────────────────────
    // Alterna entre pausar e retomar a fila da impressora pelo IP.
    // Retorna true se pausou, false se retomou, null se não encontrou.
    // NOTA: PrinterStatus do Get-Printer é um enum CIM — não pode ser comparado
    // com string 'Paused' via -eq. Usamos Win32_Printer.PrinterStatus (uint16)
    // onde o valor 6 = "Stopped Printing" indica fila pausada/parada.
    public async Task<bool?> TogglePauseQueueAsync(string ip)
    {
        var safeIp = ip.Replace("'", "");
        var script = new StringBuilder()
            .AppendLine("$ip = '" + safeIp + "'")
            .AppendLine("$port = Get-PrinterPort -ErrorAction SilentlyContinue | Where-Object { $_.PrinterHostAddress -eq $ip } | Select-Object -First 1")
            .AppendLine("if (-not $port) { Write-Output 'NOT_FOUND'; exit }")
            .AppendLine("$p = Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.PortName -eq $port.Name } | Select-Object -First 1")
            .AppendLine("if (-not $p) { Write-Output 'NOT_FOUND'; exit }")
            .AppendLine("$pName = $p.Name")
            .AppendLine("$wmi = Get-CimInstance Win32_Printer -ErrorAction SilentlyContinue |")
            .AppendLine("  Where-Object { $_.Name -eq $pName } | Select-Object -First 1")
            // Get-Printer.PrinterStatus bitmap: bit 1 (valor 1) = fila pausada
            .AppendLine("$isStopped = ($p.PrinterStatus -band 1) -ne 0")
            .AppendLine("if ($isStopped) {")
            .AppendLine("  if ($wmi) { $wmi | Invoke-CimMethod -MethodName 'Resume' -ErrorAction SilentlyContinue | Out-Null }")
            .AppendLine("  Write-Output 'RESUMED'")
            .AppendLine("} else {")
            .AppendLine("  if ($wmi) { $wmi | Invoke-CimMethod -MethodName 'Pause' -ErrorAction SilentlyContinue | Out-Null }")
            .AppendLine("  Write-Output 'PAUSED'")
            .AppendLine("}")
            .ToString();

        var output = await RunHiddenPsAndGetOutputAsync(script);
        return output.Contains("PAUSED") ? true : output.Contains("RESUMED") ? false : null;
    }

    // ── Compartilhar / Remover compartilhamento de impressora ─────────────────
    // Alterna o compartilhamento da impressora identificada pelo IP.
    // Retorna true = compartilhada, false = removido, null = não encontrada.
    public async Task<bool?> ToggleSharePrinterAsync(string ip)
    {
        var safeIp = ip.Replace("'", "");
        var script = new StringBuilder()
            .AppendLine("$ip = '" + safeIp + "'")
            .AppendLine("$port = Get-PrinterPort -ErrorAction SilentlyContinue | Where-Object { $_.PrinterHostAddress -eq $ip } | Select-Object -First 1")
            .AppendLine("if (-not $port) { Write-Output 'NOT_FOUND'; exit }")
            .AppendLine("$p = Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.PortName -eq $port.Name } | Select-Object -First 1")
            .AppendLine("if (-not $p) { Write-Output 'NOT_FOUND'; exit }")
            .AppendLine("if ($p.Shared) {")
            .AppendLine("    Set-Printer -Name $p.Name -Shared $false -ErrorAction SilentlyContinue")
            .AppendLine("    Write-Output 'UNSHARED'")
            .AppendLine("} else {")
            // ShareName: primeiros 13 chars do nome (limite compatível com clientes legados)
            .AppendLine("    $shareName = $p.Name -replace '[\\\\/:*?\"<>|]', ''")
            .AppendLine("    if ($shareName.Length -gt 13) { $shareName = $shareName.Substring(0, 13) }")
            .AppendLine("    Set-Printer -Name $p.Name -Shared $true -ShareName $shareName -ErrorAction SilentlyContinue")
            .AppendLine("    Write-Output 'SHARED'")
            .AppendLine("}")
            .ToString();

        var output = await RunHiddenPsAndGetOutputAsync(script);
        return output.Contains("SHARED") ? true : output.Contains("UNSHARED") ? false : null;
    }

    // ── Enviar Impressão de Teste ───────────────────────────────────────────
    // Envia a página de teste do Windows para a impressora identificada pelo IP.
    // NOTA: evita -Filter com WQL (escaping complexo). Usa Where-Object no pipe.
    public async Task SendTestPageAsync(string ip)
    {
        var safeIp = ip.Replace("'", "");
        var script = new StringBuilder()
            .AppendLine("$ip = '" + safeIp + "'")
            .AppendLine("$port = Get-PrinterPort -ErrorAction SilentlyContinue | Where-Object { $_.PrinterHostAddress -eq $ip } | Select-Object -First 1")
            .AppendLine("if (-not $port) { exit 1 }")
            .AppendLine("$p = Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.PortName -eq $port.Name } | Select-Object -First 1")
            .AppendLine("if (-not $p) { exit 1 }")
            .AppendLine("$pName = $p.Name")
            .AppendLine("$wmi = Get-CimInstance Win32_Printer -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq $pName } | Select-Object -First 1")
            .AppendLine("if ($wmi) { $wmi | Invoke-CimMethod -MethodName PrintTestPage | Out-Null }")
            .ToString();
        await RunHiddenPsAsync(script);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static async Task RunHiddenPsAsync(string script)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ps_{Guid.NewGuid():N}.ps1");
        try
        {
            await File.WriteAllTextAsync(tmp, script, Encoding.UTF8);
            var psi = new ProcessStartInfo
            {
                FileName               = "powershell.exe",
                Arguments              = $"-NonInteractive -NoProfile -ExecutionPolicy Bypass -File \"{tmp}\"",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };
            using var proc = Process.Start(psi);
            if (proc is not null) await proc.WaitForExitAsync();
        }
        catch { }
        finally { try { File.Delete(tmp); } catch { } }
    }

    private static async Task<string> RunHiddenPsAndGetOutputAsync(string script)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ps_{Guid.NewGuid():N}.ps1");
        try
        {
            await File.WriteAllTextAsync(tmp, script, Encoding.UTF8);
            var psi = new ProcessStartInfo
            {
                FileName               = "powershell.exe",
                Arguments              = $"-NonInteractive -NoProfile -ExecutionPolicy Bypass -File \"{tmp}\"",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return string.Empty;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return output;
        }
        catch { return string.Empty; }
        finally { try { File.Delete(tmp); } catch { } }
    }

    // ── USB: listar impressoras ───────────────────────────────────────────────
    // Retorna impressoras instaladas com porta USB (PortName LIKE 'USB%').
    // IpAddress recebe o nome da porta (ex.: "USB001") para uso como chave na lista.
    public async Task<List<PrinterDevice>> GetUsbPrintersAsync()
    {
        var script = new StringBuilder()
            .AppendLine("Get-CimInstance Win32_Printer -ErrorAction SilentlyContinue |")
            .AppendLine("  Where-Object { $_.PortName -like 'USB*' } |")
            .AppendLine("  ForEach-Object {")
            .AppendLine("    $off = if ($_.WorkOffline) { '1' } else { '0' }")
            .AppendLine("    $gp  = Get-Printer -Name $_.Name -ErrorAction SilentlyContinue")
            .AppendLine("    $pau = if ($gp -and ($gp.PrinterStatus -band 1) -ne 0) { '1' } else { '0' }")
            .AppendLine("    \"$($_.Name)|$($_.PortName)|$($_.DriverName)|$off|$pau\"")
            .AppendLine("  }")
            .ToString();

        var output = await RunHiddenPsAndGetOutputAsync(script);
        var result = new List<PrinterDevice>();

        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line  = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var parts = line.Split('|');
            if (parts.Length < 2) continue;

            result.Add(new PrinterDevice
            {
                IpAddress            = parts[1].Trim(),
                InstalledPrinterName = parts[0].Trim(),
                DeviceName           = parts[0].Trim(),
                IsUsbDevice          = true,
                IsInstalled          = true,
                DhcpStatus           = "USB",
                SysDescription       = parts.Length > 2 ? parts[2].Trim() : string.Empty,
                WorkOffline          = parts.Length > 3 && parts[3].Trim() == "1",
                QueuePaused          = parts.Length > 4 && parts[4].Trim() == "1",
            });
        }
        return result;
    }

    // ── Rede: listar impressoras de rede já instaladas no Windows ─────────────
    // Retorna impressoras com porta TCP/IP (endereço IP válido), excluindo USB.
    public async Task<List<PrinterDevice>> GetInstalledNetworkPrintersAsync()
    {
        var script = new StringBuilder()
            .AppendLine("$ports = @{}")
            .AppendLine("Get-PrinterPort -ErrorAction SilentlyContinue |")
            .AppendLine("  Where-Object { $_.PrinterHostAddress -match '^\\d+\\.\\d+\\.\\d+\\.\\d+$' } |")
            .AppendLine("  ForEach-Object { $ports[$_.Name] = $_.PrinterHostAddress }")
            .AppendLine("$wmiMap = @{}")
            .AppendLine("Get-CimInstance Win32_Printer -ErrorAction SilentlyContinue |")
            .AppendLine("  ForEach-Object { $wmiMap[$_.Name] = $_ }")
            .AppendLine("Get-Printer -ErrorAction SilentlyContinue |")
            .AppendLine("  Where-Object { $ports.ContainsKey($_.PortName) } |")
            .AppendLine("  ForEach-Object {")
            .AppendLine("    $ip  = $ports[$_.PortName]")
            .AppendLine("    $wmi = $wmiMap[$_.Name]")
            .AppendLine("    $off = if ($wmi -and $wmi.WorkOffline) { '1' } else { '0' }")
            .AppendLine("    $pau = if (($_.PrinterStatus -band 1) -ne 0) { '1' } else { '0' }")
            .AppendLine("    \"$($_.Name)|$($_.PortName)|$ip|$($_.DriverName)|$off|$pau\"")
            .AppendLine("  }")
            .ToString();

        var output = await RunHiddenPsAndGetOutputAsync(script);
        var result = new List<PrinterDevice>();

        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line  = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var parts = line.Split('|');
            if (parts.Length < 3) continue;

            result.Add(new PrinterDevice
            {
                InstalledPrinterName = parts[0].Trim(),
                DeviceName           = parts[0].Trim(),
                IpAddress            = parts[2].Trim(),
                IsInstalled          = true,
                DhcpStatus           = "Instalado",
                SysDescription       = parts.Length > 3 ? parts[3].Trim() : string.Empty,
                WorkOffline          = parts.Length > 4 && parts[4].Trim() == "1",
                QueuePaused          = parts.Length > 5 && parts[5].Trim() == "1",
            });
        }
        return result;
    }

    // ── Ativar/desativar modo offline de uma impressora ───────────────────────
    public async Task<bool> SetWorkOfflineAsync(string printerName, bool offline)
    {
        var safeName   = printerName.Replace("'", "''");
        var offlineVal = offline ? "$true" : "$false";
        var script = new StringBuilder()
            .AppendLine($"$name = '{safeName}'")
            .AppendLine("$wmi = Get-CimInstance Win32_Printer -ErrorAction SilentlyContinue |")
            .AppendLine("  Where-Object { $_.Name -eq $name } | Select-Object -First 1")
            .AppendLine("if ($wmi) {")
            .AppendLine($"  Set-CimInstance -InputObject $wmi -Property @{{WorkOffline={offlineVal}}} -ErrorAction SilentlyContinue")
            .AppendLine("  Write-Output 'OK'")
            .AppendLine("}")
            .ToString();
        var output = await RunHiddenPsAndGetOutputAsync(script);
        return output.Contains("OK");
    }

    // ── Retomar fila pausada pelo nome da impressora ──────────────────────────
    // Usa Invoke-CimMethod 'Resume' no Win32_Printer — mais confiável que
    // Resume-PrintQueue que depende do módulo PrintManagement estar carregado.
    public async Task ResumePrintQueueByNameAsync(string printerName)
    {
        var safeName = printerName.Replace("'", "''");
        var script = new StringBuilder()
            .AppendLine($"$name = '{safeName}'")
            .AppendLine("$wmi = Get-CimInstance Win32_Printer -ErrorAction SilentlyContinue |")
            .AppendLine("  Where-Object { $_.Name -eq $name } | Select-Object -First 1")
            .AppendLine("if ($wmi) {")
            .AppendLine("  $wmi | Invoke-CimMethod -MethodName 'Resume' -ErrorAction SilentlyContinue | Out-Null")
            .AppendLine("}")
            .ToString();
        await RunHiddenPsAsync(script);
    }

    // ── USB: listar portas disponíveis ────────────────────────────────────────
    public async Task<List<string>> GetAvailableUsbPortsAsync()
    {
        var script = "Get-PrinterPort -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'USB*' } | Select-Object -ExpandProperty Name";
        var output = await RunHiddenPsAndGetOutputAsync(script);
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    // ── USB: alterar porta da impressora ──────────────────────────────────────
    public async Task<bool> ChangeUsbPortAsync(string printerName, string newPortName)
    {
        var safeName = printerName.Replace("'", "''");
        var safePort = newPortName.Replace("'", "");
        var script = new StringBuilder()
            .AppendLine($"$pName  = '{safeName}'")
            .AppendLine($"$pPort  = '{safePort}'")
            .AppendLine("Set-Printer -Name $pName -PortName $pPort -ErrorAction SilentlyContinue")
            .AppendLine("Write-Output 'OK'")
            .ToString();
        var output = await RunHiddenPsAndGetOutputAsync(script);
        return output.Contains("OK");
    }

    // ── IPs temporários para sub-redes inacessíveis ───────────────────────────
    // Fluxo: UAC 1 → limpa órfãos + adiciona IPs → scan → UAC 2 → remove IPs.
    public async Task RunWithTemporarySubnetIpsAsync(
        IReadOnlyList<string> subnetPrefixes,
        Func<Task> work,
        Action<string>? onStatus = null)
    {
        if (subnetPrefixes.Count == 0) { await work(); return; }

        var tempIps   = subnetPrefixes.Select(p => $"{p}.253").ToList();
        var ipsJoined = string.Join("','", tempIps);

        bool ipsAdded = false;
        try
        {
            onStatus?.Invoke(
                $"Detectadas {subnetPrefixes.Count} sub-rede(s) sem rota local. " +
                "Aguarde o UAC para adicionar IPs temporarios...");

            // UAC 1: limpa eventuais IPs órfãos de execuções anteriores e adiciona os novos
            var addScript = new StringBuilder();
            addScript.AppendLine("# Remove IPs .253/24 manualmente adicionados de execucoes anteriores");
            addScript.AppendLine("Get-NetIPAddress -AddressFamily IPv4 |");
            addScript.AppendLine("  Where-Object { $_.IPAddress -match '\\.253$' -and $_.PrefixLength -eq 24 } |");
            addScript.AppendLine("  Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue");
            addScript.AppendLine($"$ips = @('{ipsJoined}')");
            addScript.AppendLine("$iface = Get-NetIPAddress -AddressFamily IPv4 |");
            addScript.AppendLine("  Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |");
            addScript.AppendLine("  Select-Object -First 1");
            addScript.AppendLine("if (-not $iface) { exit 1 }");
            addScript.AppendLine("foreach ($ip in $ips) {");
            addScript.AppendLine("  try { New-NetIPAddress -InterfaceIndex $iface.InterfaceIndex -IPAddress $ip -PrefixLength 24 -ErrorAction Stop } catch {}");
            addScript.AppendLine("}");

            ipsAdded = await RunElevatedPsAsync(addScript.ToString(), timeoutSeconds: 45);

            if (ipsAdded)
            {
                // Aguarda DAD (Duplicate Address Detection) completar para todos os IPs
                onStatus?.Invoke("IPs temporarios adicionados. Aguardando ativacao (5 s)...");
                await Task.Delay(5000);
            }
            else
            {
                onStatus?.Invoke("Nao foi possivel adicionar IPs temporarios. Continuando mesmo assim...");
            }

            await work();
        }
        finally
        {
            if (ipsAdded)
            {
                onStatus?.Invoke("Removendo IPs temporarios — aguarde o UAC...");
                var remScript = new StringBuilder();
                remScript.AppendLine($"$ips = @('{ipsJoined}')");
                remScript.AppendLine("foreach ($ip in $ips) {");
                remScript.AppendLine("  Remove-NetIPAddress -IPAddress $ip -Confirm:$false -ErrorAction SilentlyContinue");
                remScript.AppendLine("}");
                await RunElevatedPsAsync(remScript.ToString(), timeoutSeconds: 90);
                onStatus?.Invoke("IPs temporarios removidos.");
            }
        }
    }

    // Remove IPs .253/24 que podem ter ficado de execuções anteriores mal-sucedidas.
    // Deve ser chamado no início da Busca Ampliada, antes de adicionar novos IPs.
    public async Task CleanupOrphanedSubnetIpsAsync()
    {
        var script = new StringBuilder()
            .AppendLine("Get-NetIPAddress -AddressFamily IPv4 |")
            .AppendLine("  Where-Object { $_.IPAddress -match '\\.253$' -and $_.PrefixLength -eq 24 } |")
            .AppendLine("  Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue")
            .ToString();
        await RunElevatedPsAsync(script, timeoutSeconds: 60);
    }

    // Roda um script PS elevado (runas/UAC) e aguarda até timeoutSeconds.
    // Retorna true se o processo saiu com código 0 dentro do tempo limite.
    private static async Task<bool> RunElevatedPsAsync(string script, int timeoutSeconds = 30)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ps_elev_{Guid.NewGuid():N}.ps1");
        try
        {
            await File.WriteAllTextAsync(tmp, script, Encoding.UTF8);
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName        = "powershell.exe",
                Arguments       = $"-NonInteractive -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{tmp}\"",
                Verb            = "runas",
                UseShellExecute = true
            });
            if (proc is null) return false;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
                return proc.ExitCode == 0;
            }
            catch { return false; }
            finally { proc.Dispose(); }
        }
        catch { return false; } // usuário cancelou UAC ou erro
        finally { try { File.Delete(tmp); } catch { } }
    }

    public async Task RenameInstalledPrinterAsync(string ipAddress, string newName)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || string.IsNullOrWhiteSpace(newName))
            return;

        var safeIp   = ipAddress.Replace("'", "");
        var safeName = newName.Replace("'", " ").Replace("\"", " ");

        // Construído sem raw-string para não conflitar com $ do PowerShell
        var script = new StringBuilder()
            .AppendLine("$ip = '" + safeIp + "'")
            .AppendLine("$newName = '" + safeName + "'")
            .AppendLine("try {")
            .AppendLine("  $port = Get-PrinterPort -ErrorAction SilentlyContinue | Where-Object { $_.PrinterHostAddress -eq $ip } | Select-Object -First 1")
            .AppendLine("  if ($null -ne $port) {")
            .AppendLine("    $printer = Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.PortName -eq $port.Name } | Select-Object -First 1")
            .AppendLine("    if ($null -ne $printer -and $printer.Name -ne $newName) {")
            .AppendLine("      Rename-Printer -InputObject $printer -NewName $newName -ErrorAction SilentlyContinue")
            .AppendLine("    }")
            .AppendLine("  }")
            .AppendLine("} catch { }")
            .ToString();

        var tempScript = Path.Combine(Path.GetTempPath(), $"rename_printer_{Guid.NewGuid():N}.ps1");
        try
        {
            await File.WriteAllTextAsync(tempScript, script, Encoding.UTF8);

            var psi = new ProcessStartInfo
            {
                FileName               = "powershell.exe",
                Arguments              = $"-NonInteractive -NoProfile -ExecutionPolicy Bypass -File \"{tempScript}\"",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };

            using var proc = Process.Start(psi);
            if (proc is not null) await proc.WaitForExitAsync();
        }
        catch { }
        finally
        {
            try { File.Delete(tempScript); } catch { }
        }
    }
}

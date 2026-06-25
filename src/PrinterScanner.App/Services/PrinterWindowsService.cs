using System.Diagnostics;
using System.IO;
using System.Text;

namespace PrinterScanner.App.Services;

public sealed class PrinterWindowsService
{
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

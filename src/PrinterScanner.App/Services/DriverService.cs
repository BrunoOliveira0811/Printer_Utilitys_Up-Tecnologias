using System.IO;
using System.Reflection;

namespace PrinterScanner.App.Services;

public sealed class DriverService
{
    private const string EmbeddedResourceName = "driver-installer.exe";

    public async Task<(bool Success, string Message)> InstallEmbeddedDriverAsync()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), EmbeddedResourceName);

        try
        {
            // Extrai o EXE embutido para pasta temporária
            var assembly = Assembly.GetExecutingAssembly();
            await using var resource = assembly.GetManifestResourceStream(EmbeddedResourceName);

            if (resource is null)
                return (false, "Instalador de driver não encontrado nos recursos do aplicativo.");

            await using var fileStream = File.Create(tempPath);
            await resource.CopyToAsync(fileStream);
        }
        catch (Exception ex)
        {
            return (false, $"Erro ao extrair instalador: {ex.Message}");
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName        = tempPath,
                UseShellExecute = true,
                Verb            = "runas",
            };

            var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
                return (false, "Não foi possível iniciar o instalador.");

            var completed = await Task.Run(() => proc.WaitForExit(600_000));
            if (!completed)
                return (true, "Instalador iniciado.");

            return proc.ExitCode == 0
                ? (true,  "Driver instalado com sucesso.")
                : (false, $"Instalador encerrou com código {proc.ExitCode}.");
        }
        catch (Exception ex)
        {
            return (false, $"Erro ao executar instalador: {ex.Message}");
        }
    }
}

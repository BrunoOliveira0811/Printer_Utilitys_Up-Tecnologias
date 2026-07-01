using System.Diagnostics;
using System.IO;
using PrinterScanner.App.Models;

namespace PrinterScanner.App.Services;

public sealed class UtilitariosService
{
    private static readonly string UtilitariosFolder =
        Path.Combine(AppContext.BaseDirectory, "Utilitarios");

    public bool IsAvailable => Directory.Exists(UtilitariosFolder);

    public IReadOnlyList<UtilitarioItem> GetUtilitarios()
    {
        if (!IsAvailable)
            return [];

        return Directory
            .GetDirectories(UtilitariosFolder)
            .Select(dir =>
            {
                var exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(dir, "*.EXE", SearchOption.TopDirectoryOnly))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(p => new FileInfo(p))
                    .ToList();

                if (exes.Count == 0) return null;

                // Maior exe = executável principal da ferramenta
                var mainExe = exes.OrderByDescending(f => f.Length).First();

                return new UtilitarioItem
                {
                    Name       = Path.GetFileName(dir),
                    FolderPath = dir,
                    ExePath    = mainExe.FullName
                };
            })
            .Where(u => u is not null)
            .Select(u => u!)
            .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Launch(UtilitarioItem item)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName        = item.ExePath,
            WorkingDirectory = item.FolderPath,
            UseShellExecute = true
        });
    }
}

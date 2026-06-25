using System.IO;
using System.Text;

namespace PrinterScanner.App.Services;

public sealed class FileLogService
{
    private readonly string logFilePath;
    private readonly object syncRoot = new();
    private const long MaxLogBytes = 5 * 1024 * 1024;

    public FileLogService(string appDataDirectory)
    {
        Directory.CreateDirectory(appDataDirectory);
        logFilePath = Path.Combine(appDataDirectory, "printer-scanner.log");
    }

    public void LogError(string message, Exception? exception = null)
    {
        var builder = new StringBuilder();
        builder.Append('[')
               .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"))
               .Append("] ERROR ")
               .Append(message);

        if (exception is not null)
        {
            builder.Append(" | ").Append(exception);
        }

        builder.AppendLine();

        lock (syncRoot)
        {
            RotateIfNeeded();
            File.AppendAllText(logFilePath, builder.ToString(), Encoding.UTF8);
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(logFilePath))
        {
            return;
        }

        var fileInfo = new FileInfo(logFilePath);
        if (fileInfo.Length < MaxLogBytes)
        {
            return;
        }

        var allLines = File.ReadAllLines(logFilePath);
        var trimmedLines = allLines.Skip(Math.Max(0, allLines.Length / 2)).ToArray();
        File.WriteAllLines(logFilePath, trimmedLines, Encoding.UTF8);
    }
}

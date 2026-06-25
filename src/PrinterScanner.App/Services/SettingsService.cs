using System.IO;
using System.Text.Json;
using PrinterScanner.App.Models;

namespace PrinterScanner.App.Services;

public sealed class SettingsService
{
    private readonly string appDataDirectory;
    private readonly string settingsFilePath;
    private readonly string lastScanFilePath;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

    public SettingsService()
    {
        appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrinterScannerApp");

        Directory.CreateDirectory(appDataDirectory);
        settingsFilePath = Path.Combine(appDataDirectory, "settings.json");
        lastScanFilePath = Path.Combine(appDataDirectory, "last-scan.json");
    }

    public string AppDataDirectory => appDataDirectory;

    public async Task<AppSettings> LoadSettingsAsync()
    {
        if (!File.Exists(settingsFilePath))
        {
            var defaultSettings = new AppSettings();
            await SaveSettingsAsync(defaultSettings);
            return defaultSettings;
        }

        await using var stream = File.OpenRead(settingsFilePath);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, jsonOptions);
        return settings ?? new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await using var stream = File.Create(settingsFilePath);
        await JsonSerializer.SerializeAsync(stream, settings, jsonOptions);
    }

    public async Task<IReadOnlyList<PrinterDevice>> LoadLastScanAsync()
    {
        if (!File.Exists(lastScanFilePath))
        {
            return Array.Empty<PrinterDevice>();
        }

        await using var stream = File.OpenRead(lastScanFilePath);
        var data = await JsonSerializer.DeserializeAsync<List<PrinterDevice>>(stream, jsonOptions);
        return data ?? new List<PrinterDevice>();
    }

    public async Task SaveLastScanAsync(IEnumerable<PrinterDevice> devices)
    {
        await using var stream = File.Create(lastScanFilePath);
        await JsonSerializer.SerializeAsync(stream, devices.ToList(), jsonOptions);
    }
}

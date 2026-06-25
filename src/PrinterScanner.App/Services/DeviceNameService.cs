using System.IO;
using System.Text.Json;

namespace PrinterScanner.App.Services;

public sealed class DeviceNameService
{
    private readonly string filePath;
    private Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);

    public DeviceNameService(string appDataDirectory)
    {
        filePath = Path.Combine(appDataDirectory, "device-names.json");
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(filePath)) return;
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            names = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { }
    }

    public string? GetName(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;
        return names.TryGetValue(Normalize(mac), out var name) ? name : null;
    }

    public async Task SetNameAsync(string mac, string name)
    {
        if (string.IsNullOrWhiteSpace(mac)) return;
        names[Normalize(mac)] = name;
        try
        {
            var json = JsonSerializer.Serialize(names, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
        }
        catch { }
    }

    private static string Normalize(string mac) =>
        mac.ToUpperInvariant().Replace("-", ":").Trim();
}

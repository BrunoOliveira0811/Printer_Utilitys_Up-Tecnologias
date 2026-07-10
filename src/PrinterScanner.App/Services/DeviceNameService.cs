using System.IO;
using System.Text.Json;

namespace PrinterScanner.App.Services;

public sealed class DeviceNameService
{
    private readonly string namesPath;
    private readonly string descriptionsPath;
    private Dictionary<string, string> names        = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> descriptions = new(StringComparer.OrdinalIgnoreCase);

    public DeviceNameService(string appDataDirectory)
    {
        namesPath        = Path.Combine(appDataDirectory, "device-names.json");
        descriptionsPath = Path.Combine(appDataDirectory, "device-descriptions.json");
    }

    public async Task LoadAsync()
    {
        names        = await LoadFileAsync(namesPath);
        descriptions = await LoadFileAsync(descriptionsPath);
    }

    public string? GetName(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;
        return names.TryGetValue(Normalize(mac), out var v) ? v : null;
    }

    public string? GetDescription(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;
        return descriptions.TryGetValue(Normalize(mac), out var v) ? v : null;
    }

    public async Task SetNameAsync(string mac, string name)
    {
        if (string.IsNullOrWhiteSpace(mac)) return;
        names[Normalize(mac)] = name;
        await SaveFileAsync(namesPath, names);
    }

    public async Task SetDescriptionAsync(string mac, string description)
    {
        if (string.IsNullOrWhiteSpace(mac)) return;
        descriptions[Normalize(mac)] = description;
        await SaveFileAsync(descriptionsPath, descriptions);
    }

    private static async Task<Dictionary<string, string>> LoadFileAsync(string path)
    {
        if (!File.Exists(path)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private static async Task SaveFileAsync(string path, Dictionary<string, string> dict)
    {
        try
        {
            var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }
        catch { }
    }

    private static string Normalize(string mac) =>
        mac.ToUpperInvariant().Replace("-", ":").Trim();
}

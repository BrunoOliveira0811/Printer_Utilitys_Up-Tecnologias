namespace PrinterScanner.App.Models;

public sealed class UtilitarioItem
{
    public required string Name       { get; init; }
    public required string FolderPath { get; init; }
    public required string ExePath    { get; init; }

    public string ExeFileName => System.IO.Path.GetFileName(ExePath);
}

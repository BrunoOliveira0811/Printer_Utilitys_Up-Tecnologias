using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

const uint MB_OK          = 0x00000000;
const uint MB_YESNO       = 0x00000004;
const uint MB_ICONWARNING = 0x00000030;
const uint MB_ICONERROR   = 0x00000010;
const uint MB_ICONINFO    = 0x00000040;
const int  IDYES          = 6;

const string DotNetDownloadUrl = "https://dotnet.microsoft.com/en-us/download/dotnet/8.0";
const string EmbeddedAppName   = "PrinterScanner.App.exe";
const string TempFolderName    = "UPUtilImpressoras_v3";
const string AppTitle          = "Utilitario de Impressoras - UP Tecnologias";

if (!IsDotNet8DesktopRuntimeInstalled())
{
    int choice = NativeMessageBox(
        "O .NET 8 Desktop Runtime nao foi encontrado nesta maquina.\n\n" +
        "Este componente e necessario para executar o aplicativo.\n\n" +
        "Deseja abrir a pagina de download da Microsoft?",
        AppTitle,
        MB_YESNO | MB_ICONWARNING);

    if (choice == IDYES)
    {
        ShellOpen(DotNetDownloadUrl);
        NativeMessageBox(
            "Instale o '.NET 8.0 Desktop Runtime (v8.x.x) Windows x64' e\nexecute o aplicativo novamente.",
            AppTitle,
            MB_OK | MB_ICONINFO);
    }

    return;
}

// Extrai o app embutido para a pasta temporária
var assembly = Assembly.GetExecutingAssembly();
using var resource = assembly.GetManifestResourceStream(EmbeddedAppName);

if (resource is null)
{
    NativeMessageBox(
        "Arquivo do aplicativo nao encontrado nos recursos.\nTente reinstalar.",
        AppTitle,
        MB_OK | MB_ICONERROR);
    return;
}

var tempDir = Path.Combine(Path.GetTempPath(), TempFolderName);
Directory.CreateDirectory(tempDir);
var appPath = Path.Combine(tempDir, EmbeddedAppName);

// Encerra instâncias anteriores para liberar o arquivo antes de sobrescrever
foreach (var proc in System.Diagnostics.Process.GetProcessesByName("PrinterScanner.App"))
{
    try { proc.Kill(); proc.WaitForExit(3000); } catch { }
}

// Sobrescreve sempre para garantir que a versão extraída está atualizada
try
{
    using var fs = File.Create(appPath);
    resource.CopyTo(fs);
}
catch (IOException)
{
    if (!File.Exists(appPath))
    {
        NativeMessageBox(
            "Nao foi possivel extrair o aplicativo.\nTente novamente.",
            AppTitle,
            MB_OK | MB_ICONERROR);
        return;
    }
}

Process.Start(new ProcessStartInfo { FileName = appPath, UseShellExecute = true });

// ----- helpers -----

static bool IsDotNet8DesktopRuntimeInstalled()
{
    var runtimePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "dotnet", "shared", "Microsoft.WindowsDesktop.App");

    if (!Directory.Exists(runtimePath)) return false;

    return Directory.GetDirectories(runtimePath)
        .Any(d => Path.GetFileName(d).StartsWith("8.", StringComparison.Ordinal));
}

static void ShellOpen(string url) =>
    ShellExecute(IntPtr.Zero, "open", url, null, null, 1);

static int NativeMessageBox(string text, string caption, uint type) =>
    MessageBox(IntPtr.Zero, text, caption, type);

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
static extern nint ShellExecute(IntPtr hwnd, string? op, string file,
    string? args, string? dir, int show);

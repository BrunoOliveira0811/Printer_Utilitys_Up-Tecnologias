using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

const uint MB_OK           = 0x00000000;
const uint MB_YESNO        = 0x00000004;
const uint MB_ICONWARNING  = 0x00000030;
const uint MB_ICONERROR    = 0x00000010;
const uint MB_ICONINFO     = 0x00000040;
const uint MB_ICONQUESTION = 0x00000020;
const int  IDYES           = 6;

const string DotNetDownloadUrl  = "https://dotnet.microsoft.com/en-us/download/dotnet/8.0";
const string AppExeResource     = "PrinterScanner.App.exe";
const string UtilitariosResource = "Utilitarios.zip";
const string AppTitle           = "Utilitario de Impressoras - UP Tecnologias";
const string AppDisplayName     = "Utilitario de Impressoras";
const string Publisher          = "UP Tecnologias";
const string UninstallKeyPath   = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\UPUtilImpressoras";

var installDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
    Publisher,
    AppDisplayName);

// Verifica .NET 8 Desktop Runtime antes de instalar
if (!IsDotNet8DesktopRuntimeInstalled())
{
    int choice = NativeMessageBox(
        "O .NET 8 Desktop Runtime nao foi encontrado nesta maquina.\n\n" +
        "Este componente e necessario para executar o aplicativo apos a instalacao.\n\n" +
        "Deseja abrir a pagina de download da Microsoft?",
        AppTitle,
        MB_YESNO | MB_ICONWARNING);

    if (choice == IDYES)
    {
        ShellOpen(DotNetDownloadUrl);
        NativeMessageBox(
            "Instale o '.NET 8.0 Desktop Runtime (v8.x.x) Windows x64'\ne execute este instalador novamente.",
            AppTitle,
            MB_OK | MB_ICONINFO);
    }

    return;
}

// Confirmacao de instalacao
int confirm = NativeMessageBox(
    $"Instalar '{AppDisplayName}'?\n\n" +
    $"Destino: {installDir}\n\n" +
    "Serao criados atalhos na Area de Trabalho e no Menu Iniciar.",
    AppTitle,
    MB_YESNO | MB_ICONQUESTION);

if (confirm != IDYES) return;

try
{
    var assembly = Assembly.GetExecutingAssembly();

    // Cria pasta de instalacao
    Directory.CreateDirectory(installDir);

    // Extrai o executavel principal
    var appExePath = Path.Combine(installDir, "PrinterScanner.exe");
    ExtractResource(assembly, AppExeResource, appExePath);

    // Extrai os utilitarios (se embutidos)
    var utilitariosDir = Path.Combine(installDir, "Utilitarios");
    using (var zipStream = assembly.GetManifestResourceStream(UtilitariosResource))
    {
        if (zipStream is not null)
        {
            if (Directory.Exists(utilitariosDir))
                Directory.Delete(utilitariosDir, recursive: true);
            Directory.CreateDirectory(utilitariosDir);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            archive.ExtractToDirectory(utilitariosDir, overwriteFiles: true);
        }
    }

    // Atalho na Area de Trabalho (todos os usuarios)
    var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    CreateShortcut(
        shortcutPath: Path.Combine(desktopDir, $"{AppDisplayName}.lnk"),
        targetPath:   appExePath,
        workingDir:   installDir,
        description:  AppTitle);

    // Atalho no Menu Iniciar (todos os usuarios)
    var startMenuPublisherDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        "Programs",
        Publisher);
    Directory.CreateDirectory(startMenuPublisherDir);
    CreateShortcut(
        shortcutPath: Path.Combine(startMenuPublisherDir, $"{AppDisplayName}.lnk"),
        targetPath:   appExePath,
        workingDir:   installDir,
        description:  AppTitle);

    // Registro para Adicionar/Remover Programas
    RegisterUninstallEntry(installDir, appExePath);

    // Cria script de desinstalacao
    WriteUninstallScript(installDir, desktopDir, startMenuPublisherDir);

    // Sucesso
    int launch = NativeMessageBox(
        $"'{AppDisplayName}' instalado com sucesso!\n\nDeseja abrir o aplicativo agora?",
        AppTitle,
        MB_YESNO | MB_ICONINFO);

    if (launch == IDYES)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = appExePath,
            WorkingDirectory = installDir,
            UseShellExecute = true
        });
    }
}
catch (Exception ex)
{
    NativeMessageBox(
        $"Falha durante a instalacao:\n\n{ex.Message}",
        AppTitle,
        MB_OK | MB_ICONERROR);
}

// ----- helpers -----

static void ExtractResource(Assembly assembly, string resourceName, string destPath)
{
    using var stream = assembly.GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"Recurso '{resourceName}' nao encontrado no instalador.");
    using var fs = File.Create(destPath);
    stream.CopyTo(fs);
}

static void RegisterUninstallEntry(string installDir, string exePath)
{
    using var key = Registry.LocalMachine.CreateSubKey(UninstallKeyPath);
    if (key is null) return;

    key.SetValue("DisplayName",     AppDisplayName);
    key.SetValue("Publisher",       Publisher);
    key.SetValue("InstallLocation", installDir);
    key.SetValue("DisplayIcon",     exePath);
    key.SetValue("UninstallString", $"\"{Path.Combine(installDir, "desinstalar.bat")}\"");
    key.SetValue("NoModify",  1, RegistryValueKind.DWord);
    key.SetValue("NoRepair",  1, RegistryValueKind.DWord);
}

static void WriteUninstallScript(string installDir, string desktopDir, string startMenuPublisherDir)
{
    var desktopLnk   = Path.Combine(desktopDir, $"{AppDisplayName}.lnk");
    var startMenuLnk = Path.Combine(startMenuPublisherDir, $"{AppDisplayName}.lnk");

    var bat = $"""
        @echo off
        echo Desinstalando {AppDisplayName}...
        del /f /q "{desktopLnk}" 2>nul
        del /f /q "{startMenuLnk}" 2>nul
        rmdir /s /q "{startMenuPublisherDir}" 2>nul
        reg delete "HKLM\{UninstallKeyPath}" /f >nul 2>nul
        timeout /t 1 /nobreak >nul
        rmdir /s /q "{installDir}"
        """;

    File.WriteAllText(Path.Combine(installDir, "desinstalar.bat"), bat, System.Text.Encoding.UTF8);
}

static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description)
{
    var shellType = Type.GetTypeFromProgID("WScript.Shell")
        ?? throw new InvalidOperationException("WScript.Shell nao disponivel.");
    var shell = Activator.CreateInstance(shellType)!;

    try
    {
        var sc = shellType.InvokeMember(
            "CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
            null, shell, new object[] { shortcutPath })!;
        var scType = sc.GetType();

        void Set(string prop, object val) =>
            scType.InvokeMember(prop, System.Reflection.BindingFlags.SetProperty, null, sc, new[] { val });

        Set("TargetPath",       targetPath);
        Set("WorkingDirectory", workingDir);
        Set("IconLocation",     targetPath);
        Set("Description",      description);
        scType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
    }
    finally
    {
        Marshal.ReleaseComObject(shell);
    }
}

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

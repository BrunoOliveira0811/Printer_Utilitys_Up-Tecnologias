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

const string DotNetInstallerUrl = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe";
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

// Verifica .NET 8 Desktop Runtime — instala automaticamente se ausente
if (!IsDotNet8DesktopRuntimeInstalled())
{
    int choice = NativeMessageBox(
        "O .NET 8 Desktop Runtime nao foi encontrado nesta maquina.\n\n" +
        "Este componente e necessario para executar o aplicativo.\n\n" +
        "Deseja instala-lo agora automaticamente?",
        AppTitle,
        MB_YESNO | MB_ICONWARNING);

    if (choice != IDYES) return;

    bool installed = TryInstallDotNet8Runtime();

    if (!installed || !IsDotNet8DesktopRuntimeInstalled())
    {
        NativeMessageBox(
            "Nao foi possivel instalar o .NET 8 Desktop Runtime automaticamente.\n\n" +
            "Instale manualmente em:\nhttps://dotnet.microsoft.com/download/dotnet/8.0\n\n" +
            "Em seguida, execute este instalador novamente.",
            AppTitle,
            MB_OK | MB_ICONERROR);
        return;
    }

    NativeMessageBox(
        ".NET 8 Desktop Runtime instalado com sucesso!\n\nA instalacao do aplicativo continuara.",
        AppTitle,
        MB_OK | MB_ICONINFO);
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

static bool TryInstallDotNet8Runtime()
{
    // Tentativa 1: winget (disponivel no Windows 10 1809+ com App Installer)
    if (TryInstallViaWinget()) return true;

    // Tentativa 2: download direto + instalacao silenciosa via PowerShell
    return TryInstallViaDirectDownload();
}

static bool TryInstallViaWinget()
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName  = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " +
                        "\"winget install Microsoft.DotNet.DesktopRuntime.8 " +
                        "--accept-source-agreements --accept-package-agreements --silent\"",
            UseShellExecute  = false,
            CreateNoWindow   = true,
            WindowStyle      = System.Diagnostics.ProcessWindowStyle.Hidden
        };

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc is null) return false;

        proc.WaitForExit(120_000); // aguarda ate 2 minutos
        return proc.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

static bool TryInstallViaDirectDownload()
{
    var tempInstaller = Path.Combine(Path.GetTempPath(), "dotnet8-desktop-runtime.exe");

    try
    {
        NativeMessageBox(
            "O .NET 8 Desktop Runtime sera baixado e instalado.\n\n" +
            "O processo pode levar alguns minutos dependendo da sua conexao.\nAguarde...",
            AppTitle,
            MB_OK | MB_ICONINFO);

        // Download via PowerShell (Invoke-WebRequest ou WebClient)
        var downloadScript =
            $"[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; " +
            $"Invoke-WebRequest -Uri '{DotNetInstallerUrl}' -OutFile '{tempInstaller}' -UseBasicParsing";

        var dlPsi = new System.Diagnostics.ProcessStartInfo
        {
            FileName  = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{downloadScript}\"",
            UseShellExecute = false,
            CreateNoWindow  = true,
            WindowStyle     = System.Diagnostics.ProcessWindowStyle.Hidden
        };

        using (var dlProc = System.Diagnostics.Process.Start(dlPsi))
        {
            if (dlProc is null) return false;
            dlProc.WaitForExit(300_000); // aguarda ate 5 minutos
            if (dlProc.ExitCode != 0 || !File.Exists(tempInstaller)) return false;
        }

        // Executa o instalador baixado em modo silencioso
        var installPsi = new System.Diagnostics.ProcessStartInfo
        {
            FileName        = tempInstaller,
            Arguments       = "/quiet /norestart",
            UseShellExecute = false,
            CreateNoWindow  = true
        };

        using var installProc = System.Diagnostics.Process.Start(installPsi);
        if (installProc is null) return false;

        installProc.WaitForExit(300_000); // aguarda ate 5 minutos
        return installProc.ExitCode == 0;
    }
    catch
    {
        return false;
    }
    finally
    {
        try { if (File.Exists(tempInstaller)) File.Delete(tempInstaller); } catch { }
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

static int NativeMessageBox(string text, string caption, uint type) =>
    MessageBox(IntPtr.Zero, text, caption, type);

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

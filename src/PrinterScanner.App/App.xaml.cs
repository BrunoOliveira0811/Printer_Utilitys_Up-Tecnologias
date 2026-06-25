using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace PrinterScanner.App;

public partial class App : Application
{
    private string StartupLogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrinterScannerApp",
            "startup-error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterGlobalExceptionHandlers();

        try
        {
            base.OnStartup(e);

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            LogUnhandledException("Falha ao iniciar a janela principal.", ex);
            MessageBox.Show(
                "O aplicativo falhou ao iniciar. Consulte o arquivo startup-error.log em %AppData%\\PrinterScannerApp.",
                "Falha ao iniciar",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogUnhandledException("Excecao nao tratada na UI.", e.Exception);
        MessageBox.Show(
            "O aplicativo encontrou um erro inesperado. Consulte startup-error.log em %AppData%\\PrinterScannerApp.",
            "Erro inesperado",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(-1);
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ?? new Exception("Erro desconhecido no dominio atual.");
        LogUnhandledException("Excecao nao tratada no AppDomain.", exception);
    }

    private void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogUnhandledException("Excecao nao observada em tarefa.", e.Exception);
        e.SetObserved();
    }

    private void LogUnhandledException(string message, Exception exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(StartupLogPath)!;
            Directory.CreateDirectory(directory);

            var content = new StringBuilder()
                .Append('[').Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                .AppendLine(message)
                .AppendLine(exception.ToString())
                .AppendLine(new string('-', 80))
                .ToString();

            File.AppendAllText(StartupLogPath, content, Encoding.UTF8);
        }
        catch
        {
        }
    }
}

using System.Threading;
using System.Windows;

namespace PetOverlay;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log"),
                    $"{DateTime.Now}: {args.Exception}\n\n");
            }
            catch
            {
                // best effort only
            }
            args.Handled = true;
        };

        _mutex = new Mutex(true, "Local\\ClaudeAskUserPetSingleton", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        base.OnExit(e);
    }
}

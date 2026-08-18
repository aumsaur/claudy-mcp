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

        var parsedArgs = ParseArgs(e.Args);
        var pipeName = parsedArgs.GetValueOrDefault("pipe-name", "ClaudeAskUserPet");
        var displayName = parsedArgs.GetValueOrDefault("display-name", "Claudy");
        var sessionCwd = parsedArgs.GetValueOrDefault("session-cwd", "");
        int.TryParse(parsedArgs.GetValueOrDefault("parent-pid"), out var parentPid);

        // Scoped per pipe name (one per Claude session) so multiple sessions can each
        // run their own Claudy concurrently, while still preventing the same session
        // from accidentally launching a second instance of its own pet.
        _mutex = new Mutex(true, $"Local\\ClaudeAskUserPetSingleton-{pipeName}", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        new MainWindow(pipeName, displayName, parentPid, sessionCwd).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        base.OnExit(e);
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                result[args[i][2..]] = args[i + 1];
            }
        }
        return result;
    }
}

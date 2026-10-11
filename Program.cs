using System;
using System.Threading;
using System.Windows;

namespace LoLPing;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var mutex = new Mutex(true, "LoLPing-Win-SingleInstance", out bool isNew);
        if (!isNew) return 0;

        var app = new System.Windows.Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        void Log(string text)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "LoLPing", "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}\n");
            }
            catch { }
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("Unhandled: " + e.ExceptionObject);
        app.DispatcherUnhandledException += (_, e) => { Log("Dispatcher: " + e.Exception); e.Handled = true; };

        var model = new AppModel();
        if (args.Length > 0 && args[0] == "--trace")
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LoLPing");
            System.IO.Directory.CreateDirectory(dir);
            model.TracePath = System.IO.Path.Combine(dir, "trace.log");
        }
        var window = new Ui.SettingsWindow(model);
        using var tray = new Ui.TrayIcon(model, window);

        window.QuitRequested += () =>
        {
            model.Shutdown();
            tray.Dispose();
            app.Shutdown();
        };

        model.RestoreState();
        window.Show();

        app.Exit += (_, _) => model.Shutdown();
        return app.Run();
    }
}

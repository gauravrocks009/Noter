using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;

namespace AppleNotesWpf;

public partial class App : Application
{
    public App()
    {
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (s, e) => 
        {
            System.IO.File.WriteAllText("crash_domain.log", $"Domain Crashed: {e.ExceptionObject}");
            Environment.Exit(1);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try 
        {
            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("crash_startup.log", $"Startup Crashed: {ex}");
            Environment.Exit(1);
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var ex = e.Exception;
        var msg = $"Application Crashed: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}";
        if (ex.InnerException != null) 
        {
            msg += $"\n\nInner Exception: {ex.InnerException.Message}\n\nInner Stack Trace:\n{ex.InnerException.StackTrace}";
        }
        System.IO.File.WriteAllText("crash.log", msg);
        e.Handled = true;
        Environment.Exit(1);
    }
}

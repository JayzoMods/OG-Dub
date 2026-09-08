using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace OgDub;

public partial class App : Application
{
    public App()
    {
        var au = CultureInfo.GetCultureInfo("en-AU");
        CultureInfo.DefaultThreadCurrentCulture = au;
        CultureInfo.DefaultThreadCurrentUICulture = au;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrash(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        try
        {
            AppHost.Start();
        }
        catch (Exception ex)
        {
            WriteCrash(ex);
            throw;
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrash(e.Exception);
        e.Handled = true;
        Current?.Shutdown();
    }

    internal static void WriteCrash(Exception ex)
    {
        var path = Path.Combine(Path.GetTempPath(), "og-dub-crash.txt");
        try
        {
            File.WriteAllText(path, DateTime.Now + Environment.NewLine + ex);
        }
        catch
        {
            path = "(could not write crash file)";
        }

        try
        {
            MessageBox.Show(
                "OG Dub could not start." + Environment.NewLine + Environment.NewLine +
                ex.Message + Environment.NewLine + Environment.NewLine +
                "Details were saved to:" + Environment.NewLine + path,
                "OG Dub",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // ignore
        }
    }
}

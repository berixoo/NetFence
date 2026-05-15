using System.Windows;
using NetFence.Core;
using NetFence.App.Services;

namespace NetFence.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                OperationLog.Write(OperationLog.DefaultPath, "UnhandledUiException",
                    args.Exception.Message, []);
                System.Windows.MessageBox.Show(args.Exception.Message, "NetFence",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                try
                {
                    OperationLog.Write(OperationLog.DefaultPath, "UnhandledException", ex.Message, []);
                }
                catch { }
            }
        };

        ThemeService.Apply(SettingsService.Theme);
        base.OnStartup(e);
    }
}

using System.Windows;
using NetFence.Core;

namespace NetFence.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                OperationLog.Write(OperationLog.DefaultPath, "UnhandledUiException", args.Exception.Message, []);
                System.Windows.MessageBox.Show(
                    args.Exception.Message,
                    "NetFence",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // Last-chance handler: avoid throwing from the exception path.
            }

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
                catch
                {
                    // Ignore logging failures during process shutdown.
                }
            }
        };

        base.OnStartup(e);
    }
}

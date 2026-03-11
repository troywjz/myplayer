using System.Windows;

namespace MyPlayer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"程序发生未处理异常：\n{args.Exception.Message}",
                "MyPlayer 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(-1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            MessageBox.Show(
                $"程序发生未处理异常：\n{exception?.Message ?? "未知错误"}",
                "MyPlayer 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        base.OnStartup(e);

        var mainWindow = new MainWindow();
        if (e.Args.Length > 0)
        {
            mainWindow.PendingOpenPath = e.Args[0];
        }

        MainWindow = mainWindow;
        mainWindow.Show();
    }
}

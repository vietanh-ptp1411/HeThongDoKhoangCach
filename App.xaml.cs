using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using HeThongDoKhoangCach.Services;
using HeThongDoKhoangCach.ViewModels;

namespace HeThongDoKhoangCach;

public partial class App : Application
{
    private MainViewModel? _viewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Dùng dấu chấm thập phân nhất quán trong toàn ứng dụng (3.70 mm).
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        var settings = SettingsService.Load();
        _viewModel = new MainViewModel(settings);

        var window = new MainWindow(_viewModel);
        MainWindow = window;
        window.Show();

        await _viewModel.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _viewModel?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // đang tắt, bỏ qua
        }
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("Đã xảy ra lỗi không mong muốn:\n\n" + e.Exception.Message,
            "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

using System.Windows;
using System.Windows.Input;
using HeThongDoKhoangCach.Models;
using HeThongDoKhoangCach.ViewModels;
using HeThongDoKhoangCach.Views;
using Microsoft.Win32;

namespace HeThongDoKhoangCach;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.ShowSettingsDialog = ShowSettingsDialog;
        viewModel.ChooseExportPath = ChooseExportPath;
        viewModel.ShowMessage = ShowMessage;
        viewModel.ShowReportDialog = all =>
        {
            var w = new ReportWindow(all, ChooseExportPath, ShowMessage) { Owner = this };
            w.ShowDialog();
        };
        viewModel.ShowStatisticsDialog = (all, specs) =>
        {
            var w = new StatisticsWindow(all, specs) { Owner = this };
            w.ShowDialog();
        };
        viewModel.ShowLoginDialog = current =>
        {
            var w = new LoginWindow(current) { Owner = this };
            return w.ShowDialog() == true ? w.InspectorCode : null;
        };

        Loaded += (_, _) => SerialBox.Focus();
    }

    private AppSettings? ShowSettingsDialog(AppSettings current)
    {
        var dialog = new SettingsWindow(current) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private string? ChooseExportPath(string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Xuất kết quả ra Excel",
            FileName = defaultFileName,
            DefaultExt = ".xlsx",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            AddExtension = true,
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void ShowMessage(string message, bool isError)
        => MessageBox.Show(this, message, isError ? "Lỗi" : "Thông báo", MessageBoxButton.OK,
            isError ? MessageBoxImage.Error : MessageBoxImage.Information);

    /// <summary>Nút Home: đưa con trỏ về ô scan serial để thao tác tiếp.</summary>
    private void Home_Click(object sender, RoutedEventArgs e)
    {
        SerialBox.Focus();
        SerialBox.SelectAll();
    }

    /// <summary>Máy quét mã vạch gửi Enter sau mã → tra cứu và chọn sẵn để lần quét sau ghi đè.</summary>
    private void SerialBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        _viewModel.LookupSerialCommand.Execute(null);
        SerialBox.SelectAll();
        e.Handled = true;
    }
}

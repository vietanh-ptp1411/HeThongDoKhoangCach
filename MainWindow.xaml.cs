using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BeltTensionMeasurement.Models;
using BeltTensionMeasurement.ViewModels;
using BeltTensionMeasurement.Views;
using Microsoft.Win32;

namespace BeltTensionMeasurement;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _ignoredScan;

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
            RunDialog(() => w.ShowDialog());
        };
        viewModel.ShowStatisticsDialog = (all, specs) =>
        {
            var w = new StatisticsWindow(all, specs) { Owner = this };
            RunDialog(() => w.ShowDialog());
        };

        PreviewMouseDown += (_, _) => _ignoredScan = false;
        Deactivated += (_, _) => _ignoredScan = false;
        Closing += (_, e) =>
        {
            if (viewModel.TryFlushPendingResults()) return;
            e.Cancel = true;
            ShowMessage("Kết quả chưa ghi được vào lịch sử. Kiểm tra đường dẫn/quyền ghi hoặc dung lượng ổ đĩa; phần mềm sẽ tự thử lưu lại. Hãy đóng cửa sổ sau khi lưu thành công.", true);
        };
    }

    private AppSettings? ShowSettingsDialog(AppSettings current)
    {
        var dialog = new SettingsWindow(current) { Owner = this };
        return RunDialog(() => dialog.ShowDialog()) == true ? dialog.Result : null;
    }

    private string? ChooseExportPath(string defaultFileName)
    {
        bool pdf = defaultFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        var dialog = new SaveFileDialog
        {
            Title = pdf ? "Xuất kết quả ra PDF" : "Xuất kết quả ra Excel",
            FileName = defaultFileName,
            DefaultExt = pdf ? ".pdf" : ".xlsx",
            Filter = pdf ? "PDF (*.pdf)|*.pdf" : "Excel Workbook (*.xlsx)|*.xlsx",
            AddExtension = true,
        };
        return RunDialog(() => dialog.ShowDialog(this)) == true ? dialog.FileName : null;
    }

    private void ShowMessage(string message, bool isError)
        => RunDialog(() => MessageBox.Show(this, message, isError ? "Lỗi" : "Thông báo", MessageBoxButton.OK,
            isError ? MessageBoxImage.Error : MessageBoxImage.Information));

    private T RunDialog<T>(Func<T> action)
    {
        _viewModel.BeginDialog();
        try { return action(); }
        finally { _viewModel.EndDialog(); }
    }

    private TextBox? ScanBox(DependencyObject? source)
    {
        var box = source as TextBox ?? FindAncestor<TextBox>(source);
        return box == QrBox || box == InspectorBox || box == PurposeScanBox ? box : null;
    }

    private void ScanBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var box = (TextBox)sender;
        _ignoredScan = false;
        if (box.IsKeyboardFocusWithin) return;
        e.Handled = true;
        box.Focus();
        box.SelectAll();
    }

    private void ScanBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _ignoredScan = false;
        ((TextBox)sender).SelectAll();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        var box = ScanBox(e.OriginalSource as DependencyObject);
        if (box is null)
        {
            if (_ignoredScan) { e.Handled = true; _ignoredScan = false; }
            return;
        }
        e.Handled = true; // Enter của máy quét chỉ xác nhận ô nhập, không bấm START.
        if (_viewModel.CanEditSelection)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            _viewModel.AcceptScan(box == QrBox ? ScanField.Serial : box == InspectorBox ? ScanField.Inspector : ScanField.Purpose);
        }
        box.SelectAll(); // Quét tiếp vào cùng ô sẽ thay mã, không tự chuyển focus.
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || e.Text is "\r" or "\n") return;
        var source = e.OriginalSource as DependencyObject;
        if (ScanBox(source) is not null)
        {
            if (!_viewModel.CanEditSelection) e.Handled = true;
            return; // WPF nhập ký tự đúng TextBox đang được chọn.
        }
        if (FindAncestor<DatePicker>(source) is not null) return;
        // Scan ngoài ô nhập không được đổi model hay dùng Enter cuối mã kích hoạt nút.
        _ignoredScan = true;
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }
}

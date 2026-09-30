using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

        Loaded += (_, _) =>
        {
            if (_viewModel.SelectedSpec is null) SpecBox.Focus();
            else if (_viewModel.SelectedModel is null) ModelBox.Focus();
            else QrBox.Focus();
        };
        // Sau khi bấm bất kỳ nút nào, trả con trỏ về ô scan để máy quét luôn gõ đúng chỗ.
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(AnyButton_Click), handledEventsToo: true);
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

    /// <summary>Máy quét gửi Enter sau mã QR → tách và hiển thị, chọn sẵn để lần quét sau ghi đè.</summary>
    private void QrBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        if (_viewModel.ScanCommand.CanExecute(null)) _viewModel.ScanCommand.Execute(null);
        QrBox.SelectAll();
        e.Handled = true;
    }

    /// <summary>
    /// Máy quét mã vạch gõ như bàn phím: nếu con trỏ đang không nằm trong ô nhập nào (vd vừa bấm nút, bấm vào bảng),
    /// chuyển ký tự đầu tiên vào ô scan để không mất ký tự nào của mã QR.
    /// </summary>
    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || e.Text == "\r" || e.Text == "\n") return;
        if (!_viewModel.CanEditSelection || _viewModel.SelectedModel is null) return;
        if (Keyboard.FocusedElement is DependencyObject focused && IsTextInput(focused)) return;

        QrBox.Focus();
        QrBox.SelectAll();
        QrBox.SelectedText = e.Text;
        QrBox.CaretIndex = QrBox.Text.Length;
        e.Handled = true;
    }

    private void AnyButton_Click(object sender, RoutedEventArgs e)
    {
        // Không cướp focus của nút lịch trong DatePicker (đang mở popup chọn ngày).
        if (e.OriginalSource is DependencyObject src &&
            (FindAncestor<DatePicker>(src) is not null || FindAncestor<ComboBox>(src) is not null)) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (IsActive && _viewModel.CanEditSelection && _viewModel.SelectedModel is not null && !QrBox.IsKeyboardFocusWithin) QrBox.Focus();
        });
    }

    private static bool IsTextInput(DependencyObject element)
        => element is TextBoxBase or PasswordBox || FindAncestor<TextBoxBase>(element) is not null || FindAncestor<ComboBox>(element) is not null;

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

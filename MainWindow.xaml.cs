using System.ComponentModel;
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
    private bool _scanInputActive;
    private bool _redirectingScan;
    private bool _discardScan;

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
        ModelBox.DropDownClosed += (_, _) => RequestScanFocus();
        Activated += (_, _) => RequestScanFocus();
        Deactivated += (_, _) => ResetScanRouting();
        PreviewMouseDown += (_, _) => ResetScanRouting();
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= ViewModel_PropertyChanged;
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

    /// <summary>Bắt Enter của máy quét ở cấp cửa sổ, tránh kích hoạt nút đang có focus.</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        if (!_scanInputActive && !QrBox.IsKeyboardFocusWithin) return;
        e.Handled = true;
        if (!_discardScan && (_scanInputActive || !string.IsNullOrWhiteSpace(_viewModel.QrInput)))
        {
            if (_viewModel.ScanCommand.CanExecute(null)) _viewModel.ScanCommand.Execute(null);
        }
        ResetScanRouting();
        QrBox.SelectAll();
    }

    /// <summary>
    /// Máy quét gửi ký tự vào ô serial dù focus đang ở nút, bảng hay danh sách model.
    /// Ghi trực tiếp vào binding để giữ đủ ký tự ngay cả khi popup chưa kịp trả focus.
    /// </summary>
    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || e.Text == "\r" || e.Text == "\n") return;
        var source = e.OriginalSource as DependencyObject ?? Keyboard.FocusedElement as DependencyObject;
        // Ô ngày vẫn cho nhập tay; sự kiện của các hộp thoại không đi qua cửa sổ này.
        if (FindAncestor<DatePicker>(source) is not null) return;
        if (!_scanInputActive) _discardScan = !_viewModel.CanEditSelection || _viewModel.SelectedModel is null;
        _scanInputActive = true;
        if (_discardScan || !_viewModel.CanEditSelection || _viewModel.SelectedModel is null)
        {
            _discardScan = true;
            e.Handled = true; // Không để mã scan đổi model hay Enter kích hoạt START/STOP khi đang đo.
            return;
        }
        if (ReferenceEquals(source, QrBox) || ReferenceEquals(FindAncestor<TextBox>(source), QrBox)) return;

        e.Handled = true;
        if (!_redirectingScan) _viewModel.QrInput = "";
        _redirectingScan = true;
        SpecBox.IsDropDownOpen = false;
        ModelBox.IsDropDownOpen = false;
        _viewModel.QrInput += e.Text;
        QrBox.Focus();
        QrBox.CaretIndex = QrBox.Text.Length;
    }

    private void AnyButton_Click(object sender, RoutedEventArgs e)
    {
        // Không cướp focus của nút lịch trong DatePicker (đang mở popup chọn ngày).
        if (e.OriginalSource is DependencyObject src &&
            (FindAncestor<DatePicker>(src) is not null || FindAncestor<ComboBox>(src) is not null)) return;
        RequestScanFocus();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CanEditSelection) or nameof(MainViewModel.SelectedModel))
            RequestScanFocus();
    }

    private void RequestScanFocus()
        => Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!IsActive || !IsEnabled || !_viewModel.CanEditSelection || _viewModel.SelectedModel is null) return;
            if (SpecBox.IsDropDownOpen || ModelBox.IsDropDownOpen || FromDatePicker.IsDropDownOpen || ToDatePicker.IsDropDownOpen) return;
            if (Keyboard.FocusedElement is DependencyObject focused && FindAncestor<DatePicker>(focused) is not null) return;
            if (!QrBox.IsKeyboardFocusWithin) QrBox.Focus();
        });

    private void ResetScanRouting()
    {
        _scanInputActive = false;
        _redirectingScan = false;
        _discardScan = false;
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

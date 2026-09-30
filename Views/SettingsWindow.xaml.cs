using System.IO;
using System.Windows;
using System.Windows.Controls;
using HeThongDoKhoangCach.Models;
using HeThongDoKhoangCach.Services;
using HeThongDoKhoangCach.ViewModels;
using Microsoft.Win32;

namespace HeThongDoKhoangCach.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        _viewModel = new SettingsViewModel(current);
        DataContext = _viewModel;
    }

    /// <summary>Cài đặt mới sau khi người dùng bấm Lưu; null nếu hủy.</summary>
    public AppSettings? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!SpecGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !SpecGrid.CommitEdit(DataGridEditingUnit.Row, true)
            || !ModelGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !ModelGrid.CommitEdit(DataGridEditingUnit.Row, true)) return;
        var error = _viewModel.Validate();
        if (error is not null)
        {
            MessageBox.Show(this, error, "Cài đặt chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Result = _viewModel.Build();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không lưu được danh mục: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void BrowseMaster_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn file master BISG",
            Filter = "CSV (*.csv)|*.csv|Tất cả (*.*)|*.*",
            CheckFileExists = true,
        };
        var current = SettingsService.ResolvePath(_viewModel.MasterFilePath);
        if (File.Exists(current)) dialog.InitialDirectory = Path.GetDirectoryName(current);

        if (dialog.ShowDialog(this) == true)
            _viewModel.MasterFilePath = dialog.FileName;
    }
}

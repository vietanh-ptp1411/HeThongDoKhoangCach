using System.Windows;
using System.Windows.Controls;
using BeltTensionMeasurement.Models;
using BeltTensionMeasurement.ViewModels;

namespace BeltTensionMeasurement.Views;

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
        foreach (var (grid, section) in new[] { (TagsGrid, SettingsSection.Tags), (SpecGrid, SettingsSection.Catalog), (ModelGrid, SettingsSection.Catalog) })
        {
            if (grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true)) continue;
            SectionTabs.SelectedIndex = (int)section;
            return;
        }
        // Các trường nhập sai kiểu số có thể nằm ở mục khác với mục đang mở.
        for (int i = 0; i < SectionTabs.Items.Count; i++)
        {
            var tab = (TabItem)SectionTabs.Items[i];
            if (tab.Content is not DependencyObject content || FindInvalidInput(content) is not { } invalid) continue;
            SectionTabs.SelectedIndex = i;
            Dispatcher.BeginInvoke(() => { invalid.BringIntoView(); invalid.Focus(); });
            MessageBox.Show(this, "Có giá trị nhập chưa hợp lệ. Vui lòng kiểm tra ô được đánh dấu.", "Cài đặt chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var error = _viewModel.Validate();
        if (error is not null)
        {
            SectionTabs.SelectedIndex = (int)_viewModel.ValidationSection;
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

    private static FrameworkElement? FindInvalidInput(DependencyObject element)
    {
        if (element is FrameworkElement input && Validation.GetHasError(input)) return input;
        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
            if (FindInvalidInput(child) is { } invalid) return invalid;
        return null;
    }

}

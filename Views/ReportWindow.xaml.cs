using System.Windows;
using HeThongDoKhoangCach.Models;
using HeThongDoKhoangCach.ViewModels;

namespace HeThongDoKhoangCach.Views;

public partial class ReportWindow : Window
{
    public ReportWindow(IReadOnlyList<MeasurementResult> results, Func<string, string?> chooseExportPath, Action<string, bool> showMessage)
    {
        InitializeComponent();
        DataContext = new ReportViewModel(results)
        {
            ChooseExportPath = chooseExportPath,
            ShowMessage = showMessage,
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

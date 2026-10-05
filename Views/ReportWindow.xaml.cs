using System.Windows;
using BeltTensionMeasurement.Models;
using BeltTensionMeasurement.ViewModels;

namespace BeltTensionMeasurement.Views;

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

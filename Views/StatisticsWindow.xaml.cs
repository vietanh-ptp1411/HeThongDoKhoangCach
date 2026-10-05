using System.Windows;
using BeltTensionMeasurement.Models;
using BeltTensionMeasurement.ViewModels;

namespace BeltTensionMeasurement.Views;

public partial class StatisticsWindow : Window
{
    public StatisticsWindow(IReadOnlyList<MeasurementResult> results, IReadOnlyList<SpecDefinition> specs)
    {
        InitializeComponent();
        DataContext = new StatisticsViewModel(results, specs);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

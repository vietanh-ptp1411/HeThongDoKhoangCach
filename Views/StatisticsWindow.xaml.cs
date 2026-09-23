using System.Windows;
using HeThongDoKhoangCach.Models;
using HeThongDoKhoangCach.ViewModels;

namespace HeThongDoKhoangCach.Views;

public partial class StatisticsWindow : Window
{
    public StatisticsWindow(IReadOnlyList<MeasurementResult> results, IReadOnlyList<ModelSpec> specs)
    {
        InitializeComponent();
        DataContext = new StatisticsViewModel(results, specs);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

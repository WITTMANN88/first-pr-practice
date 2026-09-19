using System.Windows;
using Observation.App.ViewModels;

namespace Observation.App.Views;

public partial class ApplySummaryWindow : Window
{
    public ApplySummaryWindow(ApplySummaryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Closed += confirmed =>
        {
            DialogResult = confirmed;
            Close();
        };
    }
}

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StormOS.App.ViewModels;

namespace StormOS.App.Views;

public sealed partial class PerformancePage : Page
{
    public PerformancePage()
    {
        ViewModel = App.GetService<PerformanceViewModel>();
        InitializeComponent();
    }

    public PerformanceViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.Activate(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.Deactivate();
}

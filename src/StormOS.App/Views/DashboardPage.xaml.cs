using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StormOS.App.ViewModels;

namespace StormOS.App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        ViewModel = App.GetService<DashboardViewModel>();
        InitializeComponent();
    }

    public DashboardViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.Activate(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.Deactivate();
}

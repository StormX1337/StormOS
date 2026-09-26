using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StormOS.App.ViewModels;

namespace StormOS.App.Views;

public sealed partial class StartupPage : Page
{
    public StartupPage()
    {
        ViewModel = App.GetService<StartupViewModel>();
        InitializeComponent();
    }

    public StartupViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.Activate(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.Deactivate();

    private async void OnToggle(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is StartupRow row)
        {
            await ViewModel.ToggleAsync(row);
        }
    }

    private void OnOpenLocation(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is StartupRow row)
        {
            StartupViewModel.OpenLocation(row);
        }
    }
}

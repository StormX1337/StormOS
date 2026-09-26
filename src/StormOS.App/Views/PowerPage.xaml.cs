using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StormOS.App.ViewModels;

namespace StormOS.App.Views;

public sealed partial class PowerPage : Page
{
    public PowerPage()
    {
        ViewModel = App.GetService<PowerViewModel>();
        InitializeComponent();
    }

    public PowerViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.Activate(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.Deactivate();

    private async void OnActivate(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PowerPlanRow row)
        {
            await ViewModel.ActivateAsync(row);
        }
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StormOS.App.ViewModels;

namespace StormOS.App.Views;

public sealed partial class NetworkPage : Page
{
    public NetworkPage()
    {
        ViewModel = App.GetService<NetworkViewModel>();
        InitializeComponent();
    }

    public NetworkViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.Activate(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.Deactivate();

    private async void OnUseDns(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DnsRow row)
        {
            await ViewModel.UseDnsAsync(row);
        }
    }
}

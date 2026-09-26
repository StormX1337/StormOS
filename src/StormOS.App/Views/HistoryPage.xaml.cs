using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StormOS.App.ViewModels;

namespace StormOS.App.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryPage()
    {
        ViewModel = App.GetService<HistoryViewModel>();
        InitializeComponent();
    }

    public HistoryViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.Activate(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.Deactivate();

    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ChangeRow row)
        {
            await ViewModel.RestoreAsync(row);
        }
    }
}

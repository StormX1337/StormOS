using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StormOS.App.ViewModels;

namespace StormOS.App.Views;

public sealed partial class GamesPage : Page
{
    public GamesPage()
    {
        ViewModel = App.GetService<GamesViewModel>();
        InitializeComponent();
    }

    public GamesViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.Activate(e.Parameter);

    private static GameItem? Item(object sender) => (sender as FrameworkElement)?.DataContext as GameItem;

    private void OnLaunch(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is { } item)
        {
            ViewModel.Launch(item);
        }
    }

    private async void OnOptimize(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is { } item)
        {
            await ViewModel.OptimizeAsync(item);
        }
    }

    private void OnBenchmark(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is { } item)
        {
            ViewModel.Benchmark(item);
        }
    }

    private async void OnOpenProfile(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is { } item)
        {
            await ViewModel.OpenProfileAsync(item);
        }
    }

    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        if (Item(sender) is { } item)
        {
            await ViewModel.RestoreAsync(item);
        }
    }
}

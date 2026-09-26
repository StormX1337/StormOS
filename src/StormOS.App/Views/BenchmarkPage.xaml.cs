using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StormOS.App.ViewModels;

namespace StormOS.App.Views;

public sealed partial class BenchmarkPage : Page
{
    public BenchmarkPage()
    {
        ViewModel = App.GetService<BenchmarkViewModel>();
        InitializeComponent();
    }

    public BenchmarkViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.Activate(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.Deactivate();

    private void OnResultClick(object sender, ItemClickEventArgs e) => ViewModel.ShowResultCommand.Execute(e.ClickedItem as BenchmarkRow);
}

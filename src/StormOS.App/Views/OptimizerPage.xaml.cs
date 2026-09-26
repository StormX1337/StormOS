using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StormOS.App.ViewModels;

namespace StormOS.App.Views;

public sealed partial class OptimizerPage : Page
{
    public OptimizerPage()
    {
        ViewModel = App.GetService<OptimizerViewModel>();
        InitializeComponent();
    }

    public OptimizerViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.Activate(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.Deactivate();

    private async void OnApplyRule(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RuleItem rule)
        {
            await ViewModel.ApplyRuleAsync(rule);
        }
    }

    private async void OnFixFinding(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FindingItem finding)
        {
            await ViewModel.FixFindingAsync(finding);
        }
    }

    private async void OnApplyRecommendation(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RecommendationItem item)
        {
            await ViewModel.ApplyRecommendationAsync(item);
        }
    }
}

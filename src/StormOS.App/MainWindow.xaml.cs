using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StormOS.App.Services;
using StormOS.App.ViewModels;
using StormOS.Core.Settings;
using Windows.Graphics;

namespace StormOS.App;

/// <summary>The shell window: custom title bar, Mica backdrop, navigation and notifications.</summary>
public sealed partial class MainWindow : Window
{
    private readonly NavigationService _navigation;
    private readonly ISettingsStore _settings;
    private bool _suppressSelection;

    public MainWindow(ShellViewModel viewModel, NavigationService navigation, DialogService dialogs, ThemeService theme, ISettingsStore settings)
    {
        ViewModel = viewModel;
        _navigation = navigation;
        _settings = settings;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        if (settings.Current.Appearance.UseMica)
        {
            SystemBackdrop = new MicaBackdrop();
        }

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "StormOS.ico"));
        AppWindow.Resize(new SizeInt32(1480, 940));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        navigation.Frame = ContentFrame;
        navigation.Navigated += (_, key) => SelectItem(key);
        theme.Root = RootGrid;
        theme.Apply(settings.Current.Appearance.Theme);
        RootGrid.Loaded += (_, _) =>
        {
            dialogs.Root = RootGrid.XamlRoot;
            navigation.Navigate(settings.Current.OnboardingCompleted ? "dashboard" : "onboarding");
        };
    }

    public ShellViewModel ViewModel { get; }

    /// <summary>Restores and focuses the window (second instance activation).</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressSelection || args.SelectedItem is not NavigationViewItem { Tag: string key })
        {
            return;
        }

        if (!_settings.Current.OnboardingCompleted && key != "settings")
        {
            _navigation.Navigate("onboarding");
            return;
        }

        _navigation.Navigate(key);
    }

    private void SelectItem(string key)
    {
        _suppressSelection = true;
        try
        {
            Navigation.SelectedItem = Navigation.MenuItems.Concat(Navigation.FooterMenuItems)
                .OfType<NavigationViewItem>()
                .FirstOrDefault(i => (string)i.Tag == key);
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    private void OnNotificationClosed(InfoBar sender, InfoBarClosedEventArgs args) => ViewModel.Notifications.Clear();
}

using Microsoft.UI.Xaml.Controls;

namespace StormOS.App.Services;

/// <summary>Navigates the shell frame by page key.</summary>
public sealed class NavigationService
{
    private static readonly Dictionary<string, Type> Pages = new(StringComparer.Ordinal)
    {
        ["dashboard"] = typeof(Views.DashboardPage),
        ["games"] = typeof(Views.GamesPage),
        ["performance"] = typeof(Views.PerformancePage),
        ["benchmark"] = typeof(Views.BenchmarkPage),
        ["optimizer"] = typeof(Views.OptimizerPage),
        ["network"] = typeof(Views.NetworkPage),
        ["processes"] = typeof(Views.ProcessesPage),
        ["startup"] = typeof(Views.StartupPage),
        ["power"] = typeof(Views.PowerPage),
        ["overlay"] = typeof(Views.OverlayPage),
        ["devices"] = typeof(Views.DevicesPage),
        ["history"] = typeof(Views.HistoryPage),
        ["settings"] = typeof(Views.SettingsPage),
        ["onboarding"] = typeof(Views.OnboardingPage),
    };

    /// <summary>Gets or sets the frame (set by the main window).</summary>
    public Frame? Frame { get; set; }

    /// <summary>Raised after navigation with the page key.</summary>
    public event EventHandler<string>? Navigated;

    /// <summary>Navigates to a page.</summary>
    /// <param name="key">Page key.</param>
    /// <param name="parameter">Optional parameter.</param>
    public void Navigate(string key, object? parameter = null)
    {
        if (Frame is null || !Pages.TryGetValue(key, out var page))
        {
            return;
        }

        if (Frame.CurrentSourcePageType != page || parameter is not null)
        {
            Frame.Navigate(page, parameter);
        }

        Navigated?.Invoke(this, key);
    }
}

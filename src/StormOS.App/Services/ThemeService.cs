using Microsoft.UI.Xaml;
using StormOS.Core.Settings;

namespace StormOS.App.Services;

/// <summary>Applies the configured theme to the main window content.</summary>
public sealed class ThemeService
{
    /// <summary>Gets or sets the root element (set by the main window).</summary>
    public FrameworkElement? Root { get; set; }

    /// <summary>Applies a theme.</summary>
    /// <param name="theme">Theme.</param>
    public void Apply(AppTheme theme)
    {
        if (Root is not null)
        {
            Root.RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.System => ElementTheme.Default,
                _ => ElementTheme.Dark,
            };
        }
    }
}

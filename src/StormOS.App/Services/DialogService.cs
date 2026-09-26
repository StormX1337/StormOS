using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StormOS.Core.Optimization;

namespace StormOS.App.Services;

/// <summary>Content dialogs anchored to the main window.</summary>
public sealed class DialogService
{
    /// <summary>Gets or sets the XAML root used by dialogs (set by the main window).</summary>
    public XamlRoot? Root { get; set; }

    /// <summary>Asks for confirmation.</summary>
    /// <param name="title">Title.</param>
    /// <param name="content">Body text or element.</param>
    /// <param name="primary">Primary button text.</param>
    /// <returns><see langword="true"/> when confirmed.</returns>
    public async Task<bool> ConfirmAsync(string title, object content, string primary = "Continue")
    {
        if (Root is null)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Asks for confirmation before a system change, stating risk, reversibility and restart needs.</summary>
    /// <param name="name">Change name.</param>
    /// <param name="description">What it does.</param>
    /// <param name="risk">Risk level.</param>
    /// <param name="reversible">Whether it can be restored.</param>
    /// <param name="restart">Whether a restart is required.</param>
    /// <param name="current">Current value.</param>
    /// <param name="target">Target value.</param>
    /// <returns><see langword="true"/> when confirmed.</returns>
    public Task<bool> ConfirmChangeAsync(string name, string description, RiskLevel risk, bool reversible, bool restart, string? current = null, string? target = null)
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 460 };
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        if (current is not null && target is not null)
        {
            panel.Children.Add(new TextBlock { Text = $"Current: {current}   →   Target: {target}", Opacity = 0.8 });
        }

        panel.Children.Add(new TextBlock { Text = $"Risk: {risk}", Opacity = 0.8 });
        panel.Children.Add(new TextBlock
        {
            Text = reversible ? "A backup is created first. You can restore the previous state from History at any time." : "This change cannot be undone.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = reversible ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold,
        });
        if (restart)
        {
            panel.Children.Add(new TextBlock { Text = "A restart is required for this change to take full effect.", TextWrapping = TextWrapping.Wrap });
        }

        return ConfirmAsync(name, panel, reversible ? "Apply" : "Apply anyway");
    }

    /// <summary>Shows a message.</summary>
    /// <param name="title">Title.</param>
    /// <param name="content">Content.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task ShowAsync(string title, object content)
    {
        if (Root is null)
        {
            return;
        }

        var dialog = new ContentDialog { XamlRoot = Root, Title = title, Content = content, CloseButtonText = "Close" };
        await dialog.ShowAsync();
    }
}

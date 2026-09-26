using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;

namespace StormOS.App.Services;

/// <summary>An in-app notification shown in the shell's InfoBar.</summary>
/// <param name="Title">Title.</param>
/// <param name="Message">Message.</param>
/// <param name="Severity">Severity.</param>
public sealed record AppNotification(string Title, string Message, InfoBarSeverity Severity);

/// <summary>Shows friendly, non-technical messages in the shell. Technical detail stays in the log.</summary>
public sealed partial class NotificationService(UiDispatcher dispatcher) : ObservableObject
{
    /// <summary>Gets the current notification.</summary>
    [ObservableProperty]
    public partial AppNotification? Current { get; set; }

    /// <summary>Gets a value indicating whether a notification is shown.</summary>
    [ObservableProperty]
    public partial bool HasNotification { get; set; }

    /// <summary>Shows an informational message.</summary>
    /// <param name="message">Message.</param>
    /// <param name="title">Title.</param>
    public void Info(string message, string title = "") => Show(new AppNotification(title, message, InfoBarSeverity.Informational));

    /// <summary>Shows a success message.</summary>
    /// <param name="message">Message.</param>
    /// <param name="title">Title.</param>
    public void Success(string message, string title = "") => Show(new AppNotification(title, message, InfoBarSeverity.Success));

    /// <summary>Shows a warning.</summary>
    /// <param name="message">Message.</param>
    /// <param name="title">Title.</param>
    public void Warning(string message, string title = "") => Show(new AppNotification(title, message, InfoBarSeverity.Warning));

    /// <summary>Shows an error.</summary>
    /// <param name="message">Message.</param>
    /// <param name="title">Title.</param>
    public void Error(string message, string title = "") => Show(new AppNotification(title, message, InfoBarSeverity.Error));

    /// <summary>Clears the notification.</summary>
    public void Clear() => dispatcher.Post(() =>
    {
        HasNotification = false;
        Current = null;
    });

    private void Show(AppNotification notification) => dispatcher.Post(() =>
    {
        Current = notification;
        HasNotification = true;
    });
}

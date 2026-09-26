using CommunityToolkit.Mvvm.ComponentModel;

namespace StormOS.App.ViewModels;

/// <summary>Base class for page view models.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    /// <summary>Gets or sets a value indicating whether a long operation is running.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Gets or sets a friendly status message.</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>Called when the page is shown.</summary>
    /// <param name="parameter">Navigation parameter.</param>
    public virtual void Activate(object? parameter)
    {
    }

    /// <summary>Called when the page is left.</summary>
    public virtual void Deactivate()
    {
    }
}

using Microsoft.UI.Dispatching;

namespace StormOS.App.Services;

/// <summary>Marshals work to the UI thread.</summary>
public sealed class UiDispatcher(DispatcherQueue queue)
{
    /// <summary>Runs an action on the UI thread (inline when already on it).</summary>
    /// <param name="action">Action.</param>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (queue.HasThreadAccess)
        {
            action();
        }
        else
        {
            queue.TryEnqueue(() => action());
        }
    }
}

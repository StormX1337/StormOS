using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using StormOS.App.Composition;
using StormOS.App.Services;

namespace StormOS.App;

/// <summary>Application entry: single instance, dependency injection and the main window.</summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>Gets the application's service provider.</summary>
    public static IServiceProvider Services => ((App)Current)._services ?? throw new InvalidOperationException("The application has not started yet.");

    /// <summary>Resolves a service.</summary>
    /// <typeparam name="T">Service type.</typeparam>
    /// <returns>The service.</returns>
    public static T GetService<T>()
        where T : notnull => Services.GetRequiredService<T>();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var instance = AppInstance.FindOrRegisterForKey("StormOS.Main");
        if (!instance.IsCurrent)
        {
            await instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
            Exit();
            return;
        }

        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _services = AppHost.Build(dispatcher);
        instance.Activated += (_, _) => dispatcher.TryEnqueue(() => _window?.BringToFront());

        await AppHost.InitializeAsync(_services);
        _window = _services.GetRequiredService<MainWindow>();
        _window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // UI thread exceptions are logged with full detail and surfaced as a friendly message instead of a crash.
        _services?.GetService<ILogger<App>>()?.LogError(e.Exception, "Unhandled UI exception");
        _services?.GetService<NotificationService>()?.Error("Something went wrong. Storm OS kept running; details were written to the application log.");
        e.Handled = true;
    }
}

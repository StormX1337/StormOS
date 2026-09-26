using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StormOS.App.Services;
using StormOS.Core.Settings;
using StormOS.Core.Telemetry;
using StormOS.Overlay;

namespace StormOS.App.ViewModels;

/// <summary>A selectable overlay metric.</summary>
public sealed partial class OverlayMetricOption(string key, string label, bool selected) : ObservableObject
{
    public string Key => key;

    public string Label => label;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = selected;
}

/// <summary>Preview line.</summary>
public sealed record OverlayPreviewLine(string Label, string Value, bool Accent);

/// <summary>In-game overlay settings, live preview and start/stop.</summary>
public sealed partial class OverlayViewModel(
    ISettingsStore settings,
    OverlayController overlay,
    TelemetryFeed feed,
    UiDispatcher ui,
    NotificationService notifications) : PageViewModel
{
    private bool _loading;

    public ObservableCollection<OverlayMetricOption> Metrics { get; } = [];

    public ObservableCollection<OverlayPreviewLine> Preview { get; } = [];

    public IReadOnlyList<string> Positions { get; } = ["Top left", "Top right", "Bottom left", "Bottom right"];

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial bool ShowWithGames { get; set; }

    [ObservableProperty]
    public partial int PositionIndex { get; set; }

    [ObservableProperty]
    public partial double OffsetX { get; set; }

    [ObservableProperty]
    public partial double OffsetY { get; set; }

    [ObservableProperty]
    public partial double FontSize { get; set; }

    [ObservableProperty]
    public partial double OpacityPercent { get; set; }

    [ObservableProperty]
    public partial string Hotkey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? HotkeyError { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    public override void Activate(object? parameter)
    {
        _loading = true;
        var current = settings.Current.Overlay;
        Enabled = current.Enabled;
        ShowWithGames = current.ShowWithGames;
        PositionIndex = (int)current.Position;
        OffsetX = current.OffsetX;
        OffsetY = current.OffsetY;
        FontSize = current.FontSize;
        OpacityPercent = Math.Round(current.Opacity * 100);
        Hotkey = current.Hotkey;
        Metrics.Clear();
        foreach (var (key, label) in OverlayContent.AvailableMetrics)
        {
            var option = new OverlayMetricOption(key, label, current.Metrics.Contains(key, StringComparer.Ordinal));
            option.PropertyChanged += (_, _) => _ = SaveAsync();
            Metrics.Add(option);
        }

        _loading = false;
        IsRunning = overlay.IsRunning;
        UpdatePreview(feed.Latest);
        feed.SnapshotReceived += OnSnapshot;
    }

    public override void Deactivate() => feed.SnapshotReceived -= OnSnapshot;

    partial void OnEnabledChanged(bool value) => _ = SaveAsync();

    partial void OnShowWithGamesChanged(bool value) => _ = SaveAsync();

    partial void OnPositionIndexChanged(int value) => _ = SaveAsync();

    partial void OnOffsetXChanged(double value) => _ = SaveAsync();

    partial void OnOffsetYChanged(double value) => _ = SaveAsync();

    partial void OnFontSizeChanged(double value) => _ = SaveAsync();

    partial void OnOpacityPercentChanged(double value) => _ = SaveAsync();

    partial void OnHotkeyChanged(string value)
    {
        HotkeyError = OverlayContent.TryParseHotkey(value, out _, out _) ? null : "Use a combination like Ctrl+Shift+F10 (modifiers plus F1–F24, A–Z or 0–9).";
        if (HotkeyError is null)
        {
            _ = SaveAsync();
        }
    }

    [RelayCommand]
    private void Toggle()
    {
        if (overlay.IsRunning)
        {
            overlay.Stop();
        }
        else
        {
            overlay.Start();
            notifications.Info($"Overlay started. Toggle it in-game with {Hotkey}. It is a normal always-on-top window; it does not hook or inject into games.");
        }

        IsRunning = overlay.IsRunning;
    }

    private async Task SaveAsync()
    {
        if (_loading)
        {
            return;
        }

        var current = settings.Current;
        var updated = current.Overlay with
        {
            Enabled = Enabled,
            ShowWithGames = ShowWithGames,
            Position = (OverlayPosition)Math.Clamp(PositionIndex, 0, 3),
            OffsetX = (int)Math.Clamp(OffsetX, 0, 2000),
            OffsetY = (int)Math.Clamp(OffsetY, 0, 2000),
            FontSize = (int)Math.Clamp(FontSize, 8, 32),
            Opacity = Math.Clamp(OpacityPercent / 100.0, 0.2, 1.0),
            Hotkey = HotkeyError is null ? Hotkey : current.Overlay.Hotkey,
            Metrics = Metrics.Where(m => m.IsSelected).Select(m => m.Key).ToList(),
        };
        await settings.SaveAsync(current with { Overlay = updated });
        UpdatePreview(feed.Latest);
    }

    private void OnSnapshot(object? sender, MetricsSnapshot snapshot) => ui.Post(() => UpdatePreview(snapshot));

    private void UpdatePreview(MetricsSnapshot? snapshot)
    {
        var lines = OverlayContent.Build(snapshot, settings.Current.Overlay);
        Preview.Clear();
        foreach (var line in lines)
        {
            Preview.Add(new OverlayPreviewLine(line.Label, line.Value, line.Accent));
        }

        IsRunning = overlay.IsRunning;
    }
}

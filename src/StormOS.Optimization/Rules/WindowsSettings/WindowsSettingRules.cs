using StormOS.Core.Optimization;
using StormOS.Optimization.Rules.Registry;
using StormOS.Windows.RegistryAccess;

namespace StormOS.Optimization.Rules.WindowsSettings;

/// <summary>Enables Windows Game Mode (on by default since Windows 10 1903; STORM OS only restores it if it was turned off).</summary>
public sealed class GameModeRule(IRegistryAccess registry) : RegistryValueRule(registry)
{
    /// <inheritdoc />
    public override string Id => "windows.game-mode";

    /// <inheritdoc />
    public override string Name => "Game Mode";

    /// <inheritdoc />
    public override string Description => "Turns on Windows Game Mode, which prioritizes the game and pauses Windows Update driver installs and restart notifications while you play.";

    /// <inheritdoc />
    public override OptimizationCategory Category => OptimizationCategory.WindowsSettings;

    /// <inheritdoc />
    public override RiskLevel RiskLevel => RiskLevel.Low;

    /// <inheritdoc />
    protected override IReadOnlyList<RegistryTarget> Targets { get; } =
    [
        new(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", DWord(1), DWord(1)),
    ];

    /// <inheritdoc />
    protected override string AppliedState => "On";

    /// <inheritdoc />
    protected override string NotAppliedState => "Off";
}

/// <summary>Disables Xbox Game Bar background recording and captures.</summary>
public sealed class GameDvrRule(IRegistryAccess registry) : RegistryValueRule(registry)
{
    /// <inheritdoc />
    public override string Id => "windows.game-dvr";

    /// <inheritdoc />
    public override string Name => "Background recording (Game DVR)";

    /// <inheritdoc />
    public override string Description => "Turns off Xbox Game Bar captures and background recording, which continuously use the GPU video encoder and disk. Game Bar clips will be unavailable until restored.";

    /// <inheritdoc />
    public override OptimizationCategory Category => OptimizationCategory.WindowsSettings;

    /// <inheritdoc />
    public override RiskLevel RiskLevel => RiskLevel.Low;

    /// <inheritdoc />
    protected override IReadOnlyList<RegistryTarget> Targets { get; } =
    [
        new(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", DWord(0), DWord(1)),
        new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", DWord(0), DWord(1)),
    ];

    /// <inheritdoc />
    protected override string AppliedState => "Off";

    /// <inheritdoc />
    protected override string NotAppliedState => "On";
}

/// <summary>Enables hardware-accelerated GPU scheduling (HAGS).</summary>
public sealed class HardwareGpuSchedulingRule(IRegistryAccess registry) : RegistryValueRule(registry)
{
    /// <inheritdoc />
    public override string Id => "windows.hags";

    /// <inheritdoc />
    public override string Name => "Hardware-accelerated GPU scheduling";

    /// <inheritdoc />
    public override string Description => "Lets the GPU manage its own memory scheduling. Required for DLSS Frame Generation. Only effective with a supporting GPU and driver (for example NVIDIA GTX 10-series or AMD RX 5000-series and newer). Requires a restart.";

    /// <inheritdoc />
    public override OptimizationCategory Category => OptimizationCategory.WindowsSettings;

    /// <inheritdoc />
    public override RiskLevel RiskLevel => RiskLevel.Medium;

    /// <inheritdoc />
    public override bool RequiresRestart => true;

    /// <inheritdoc />
    protected override IReadOnlyList<RegistryTarget> Targets { get; } =
    [
        new(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", DWord(2)),
    ];

    /// <inheritdoc />
    protected override string AppliedState => "On";

    /// <inheritdoc />
    protected override string NotAppliedState => "Off / driver default";
}

/// <summary>Makes sure TRIM is enabled for NTFS volumes.</summary>
public sealed class TrimRule(IRegistryAccess registry) : RegistryValueRule(registry)
{
    /// <inheritdoc />
    public override string Id => "storage.trim";

    /// <inheritdoc />
    public override string Name => "SSD TRIM";

    /// <inheritdoc />
    public override string Description => "Makes sure Windows sends TRIM commands to SSDs (NTFS), which keeps write performance stable over time. Enabled by default on Windows; STORM OS only re-enables it if it was turned off.";

    /// <inheritdoc />
    public override OptimizationCategory Category => OptimizationCategory.Storage;

    /// <inheritdoc />
    public override RiskLevel RiskLevel => RiskLevel.Low;

    /// <inheritdoc />
    public override bool RequiresRestart => true;

    /// <inheritdoc />
    protected override IReadOnlyList<RegistryTarget> Targets { get; } =
    [
        new(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem", "DisableDeleteNotification", DWord(0), DWord(0)),
    ];

    /// <inheritdoc />
    protected override string AppliedState => "Enabled";

    /// <inheritdoc />
    protected override string NotAppliedState => "Disabled";
}

namespace StormOS.Core.Optimization;

/// <summary>Risk of an optimization.</summary>
public enum RiskLevel
{
    /// <summary>No measurable risk; purely informational or trivially reversible.</summary>
    None,

    /// <summary>Low risk; easily reversible user preference.</summary>
    Low,

    /// <summary>Medium risk; affects system behaviour and may require a restart.</summary>
    Medium,

    /// <summary>High risk; only for experienced users, always explicitly confirmed.</summary>
    High,
}

/// <summary>Optimization module.</summary>
public enum OptimizationCategory
{
    /// <summary>Power plans and power settings.</summary>
    Power,

    /// <summary>Xbox / gaming services.</summary>
    GamingServices,

    /// <summary>Startup applications.</summary>
    Startup,

    /// <summary>Background processes.</summary>
    BackgroundProcesses,

    /// <summary>Network configuration.</summary>
    Network,

    /// <summary>Windows gaming related settings.</summary>
    WindowsSettings,

    /// <summary>Game specific settings.</summary>
    Game,

    /// <summary>Storage settings.</summary>
    Storage,

    /// <summary>Process scheduling.</summary>
    Process,

    /// <summary>Windows services.</summary>
    Services,
}

/// <summary>Result of detecting a rule's current state.</summary>
public enum DetectionState
{
    /// <summary>The rule does not apply to this system.</summary>
    NotApplicable,

    /// <summary>The system already has the target configuration.</summary>
    AlreadyApplied,

    /// <summary>The rule can be applied.</summary>
    Applicable,

    /// <summary>The OS or hardware does not support the rule.</summary>
    Unsupported,

    /// <summary>Detection failed.</summary>
    Error,
}

/// <summary>Final outcome of an optimization attempt.</summary>
public enum OptimizationOutcome
{
    /// <summary>Applied and verified.</summary>
    Applied,

    /// <summary>Nothing to do.</summary>
    Skipped,

    /// <summary>Failed before any change was made.</summary>
    Failed,

    /// <summary>Failed after changing the system; the change was rolled back automatically.</summary>
    FailedRolledBack,

    /// <summary>Failed and the automatic rollback failed too; manual attention required.</summary>
    FailedRollbackFailed,
}

/// <summary>Rollback state of a recorded change.</summary>
public enum RollbackStatus
{
    /// <summary>Rollback is not applicable to this event.</summary>
    NotApplicable,

    /// <summary>The change is active and can be rolled back.</summary>
    Available,

    /// <summary>The change was rolled back.</summary>
    RolledBack,

    /// <summary>A rollback was attempted and failed.</summary>
    Failed,

    /// <summary>The change cannot be rolled back.</summary>
    Unavailable,
}

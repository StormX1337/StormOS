namespace StormOS.Core.Power;

/// <summary>A Windows power scheme.</summary>
public sealed record PowerPlan
{
    /// <summary>Gets the scheme GUID.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the friendly name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets a value indicating whether the scheme is active.</summary>
    public bool IsActive { get; init; }

    /// <summary>Gets the well known base personality, when recognised.</summary>
    public string? Personality { get; init; }
}

/// <summary>Well known Windows power scheme GUIDs.</summary>
public static class KnownPowerSchemes
{
    /// <summary>Balanced (SCHEME_BALANCED).</summary>
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");

    /// <summary>High performance (SCHEME_MIN).</summary>
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    /// <summary>Power saver (SCHEME_MAX).</summary>
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    /// <summary>Ultimate performance template (duplicated on demand).</summary>
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    /// <summary>Returns a friendly personality name for a well known GUID.</summary>
    /// <param name="id">Scheme GUID.</param>
    /// <returns>The personality or <see langword="null"/>.</returns>
    public static string? Describe(Guid id) =>
        id == Balanced ? "Balanced"
        : id == HighPerformance ? "High performance"
        : id == PowerSaver ? "Power saver"
        : id == UltimatePerformance ? "Ultimate performance"
        : null;
}

/// <summary>Manages power schemes through the documented PowrProf API.</summary>
public interface IPowerPlanService
{
    /// <summary>Lists installed power schemes.</summary>
    /// <returns>The schemes.</returns>
    IReadOnlyList<PowerPlan> GetPlans();

    /// <summary>Gets the active scheme GUID.</summary>
    /// <returns>The active scheme GUID.</returns>
    Guid GetActivePlanId();

    /// <summary>Activates a scheme.</summary>
    /// <param name="schemeId">Scheme GUID.</param>
    void SetActivePlan(Guid schemeId);

    /// <summary>Creates a copy of a template scheme (for example Ultimate Performance) and returns the new scheme id.</summary>
    /// <param name="templateId">Template scheme GUID.</param>
    /// <returns>The new scheme GUID.</returns>
    Guid DuplicatePlan(Guid templateId);

    /// <summary>Deletes a scheme that STORM OS created.</summary>
    /// <param name="schemeId">Scheme GUID.</param>
    void DeletePlan(Guid schemeId);

    /// <summary>Gets the effective power mode reported by Windows (for example "Best performance"), when available.</summary>
    /// <returns>The effective mode or <see langword="null"/>.</returns>
    string? GetEffectivePowerMode();
}

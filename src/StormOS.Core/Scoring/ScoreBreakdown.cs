namespace StormOS.Core.Scoring;

/// <summary>A single input of a transparent score.</summary>
/// <param name="Name">Input name.</param>
/// <param name="RawValue">Measured raw value, or <see langword="null"/> when not measured.</param>
/// <param name="Unit">Unit of the raw value.</param>
/// <param name="Normalized">Normalized sub-score 0–100, or <see langword="null"/> when not measured.</param>
/// <param name="Weight">Relative weight.</param>
/// <param name="Explanation">How the raw value was normalized.</param>
public sealed record ScoreInput(string Name, double? RawValue, string Unit, double? Normalized, double Weight, string Explanation);

/// <summary>
/// A transparent score: the result is the weighted mean of the normalized inputs that were actually
/// measured. When less than half of the total weight was measured, no score is produced.
/// </summary>
public sealed record ScoreBreakdown
{
    /// <summary>Gets the score name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the score 0–100, or <see langword="null"/> when there is insufficient data.</summary>
    public double? Score { get; init; }

    /// <summary>Gets the inputs.</summary>
    public IReadOnlyList<ScoreInput> Inputs { get; init; } = [];

    /// <summary>Gets a description of the formula.</summary>
    public string Formula { get; init; } = string.Empty;

    /// <summary>Gets the share of the total weight that was measured (0–1).</summary>
    public double Coverage { get; init; }

    /// <summary>Gets a verbal rating for the score.</summary>
    public string Rating => Score switch
    {
        null => "Insufficient data",
        >= 90 => "Excellent",
        >= 75 => "Good",
        >= 55 => "Fair",
        >= 35 => "Poor",
        _ => "Critical",
    };
}

/// <summary>Weighted scoring and normalization helpers.</summary>
public static class WeightedScore
{
    /// <summary>Minimum measured weight share required to produce a score.</summary>
    public const double MinimumCoverage = 0.5;

    /// <summary>Computes a weighted score from inputs, ignoring inputs that were not measured.</summary>
    /// <param name="name">Score name.</param>
    /// <param name="inputs">Inputs.</param>
    /// <returns>The breakdown.</returns>
    public static ScoreBreakdown Compute(string name, IReadOnlyList<ScoreInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var totalWeight = inputs.Sum(i => i.Weight);
        var measured = inputs.Where(i => i.Normalized.HasValue).ToList();
        var measuredWeight = measured.Sum(i => i.Weight);
        var coverage = totalWeight > 0 ? measuredWeight / totalWeight : 0;

        double? score = null;
        if (measuredWeight > 0 && coverage >= MinimumCoverage)
        {
            score = Math.Round(measured.Sum(i => i.Normalized!.Value * i.Weight) / measuredWeight, 1);
        }

        return new ScoreBreakdown
        {
            Name = name,
            Score = score,
            Inputs = inputs,
            Coverage = coverage,
            Formula = "Score = Σ(normalized × weight) / Σ(weight of measured inputs); inputs that were not measured are excluded, and no score is produced below 50 % weight coverage.",
        };
    }

    /// <summary>Linear normalization where lower raw values are better.</summary>
    /// <param name="value">Raw value.</param>
    /// <param name="best">Value (or lower) that maps to 100.</param>
    /// <param name="worst">Value (or higher) that maps to 0.</param>
    /// <returns>Normalized 0–100.</returns>
    public static double LowerIsBetter(double value, double best, double worst)
    {
        if (worst <= best)
        {
            throw new ArgumentException("worst must be greater than best.", nameof(worst));
        }

        return Math.Clamp(100.0 * (worst - value) / (worst - best), 0, 100);
    }

    /// <summary>Linear normalization where higher raw values are better.</summary>
    /// <param name="value">Raw value.</param>
    /// <param name="worst">Value (or lower) that maps to 0.</param>
    /// <param name="best">Value (or higher) that maps to 100.</param>
    /// <returns>Normalized 0–100.</returns>
    public static double HigherIsBetter(double value, double worst, double best)
    {
        if (best <= worst)
        {
            throw new ArgumentException("best must be greater than worst.", nameof(best));
        }

        return Math.Clamp(100.0 * (value - worst) / (best - worst), 0, 100);
    }
}

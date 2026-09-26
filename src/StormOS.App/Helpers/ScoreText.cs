using System.Globalization;
using System.Text;
using StormOS.Core.Scoring;

namespace StormOS.App.Helpers;

/// <summary>Human readable score explanations (inputs, weights, result).</summary>
public static class ScoreText
{
    public static string Describe(ScoreBreakdown? score)
    {
        if (score is null)
        {
            return "Not calculated yet.";
        }

        var builder = new StringBuilder();
        builder.Append(score.Name).Append(": ").AppendLine(score.Score is { } s ? s.ToString("0", CultureInfo.CurrentCulture) + " / 100" : "insufficient data");
        foreach (var input in score.Inputs)
        {
            builder.Append("• ").Append(input.Name).Append(": ");
            builder.Append(input.RawValue is { } raw ? raw.ToString("0.##", CultureInfo.CurrentCulture) + " " + input.Unit : "not measured");
            builder.Append(input.Normalized is { } n ? $" → {n.ToString("0", CultureInfo.CurrentCulture)} pts" : string.Empty);
            builder.Append(CultureInfo.CurrentCulture, $" (weight {input.Weight:P0}; {input.Explanation})").AppendLine();
        }

        builder.Append(score.Formula);
        return builder.ToString();
    }
}

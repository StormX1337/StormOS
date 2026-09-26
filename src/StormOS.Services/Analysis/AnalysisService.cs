using Microsoft.Extensions.Logging;
using StormOS.Core.Analysis;
using StormOS.Core.Licensing;
using StormOS.Core.Settings;
using StormOS.Services.Cloud;

namespace StormOS.Services.Analysis;

/// <summary>
/// Combines the deterministic rule engine with optional AI analysis from STORM Cloud (Ultimate tier, cloud enabled,
/// explicit opt-in). AI output is advisory only: actions still require user confirmation in the UI.
/// </summary>
public sealed class AnalysisService(RuleBasedAnalysisEngine rules, CloudClient cloud, IEntitlementService entitlements, ISettingsStore settings, ILogger<AnalysisService> logger)
{
    /// <summary>Gets a value indicating whether AI analysis can be used.</summary>
    public bool AiAvailable => entitlements.IsEnabled(Features.AiAnalysis) && settings.Current.Cloud.Enabled && settings.Current.Privacy.CloudSync;

    /// <summary>Analyzes measurements.</summary>
    /// <param name="window">Measurements.</param>
    /// <param name="includeAi">Whether to request AI analysis when available.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recommendations (rule-based first).</returns>
    public async Task<IReadOnlyList<Recommendation>> AnalyzeAsync(AnalysisWindow window, bool includeAi, CancellationToken cancellationToken = default)
    {
        var results = (await rules.AnalyzeAsync(window, cancellationToken).ConfigureAwait(false)).ToList();
        if (!includeAi || !AiAvailable)
        {
            return results;
        }

        var ai = await cloud.AnalyzeAsync(window, cancellationToken).ConfigureAwait(false);
        if (!ai.IsSuccess)
        {
            logger.LogInformation("AI analysis unavailable: {Reason}", ai.Error.Message);
            return results;
        }

        foreach (var recommendation in ai.Value!.Recommendations.Where(r => r.Evidence.Count > 0))
        {
            if (results.All(r => r.Id != recommendation.Id))
            {
                results.Add(recommendation with { Source = "ai" });
            }
        }

        return results;
    }
}

using System.Globalization;
using StormOS.Core.Common;
using StormOS.Core.Optimization;

namespace StormOS.Optimization.Rules.Storage;

/// <summary>
/// Deletes temporary files older than seven days from the user's temp folder. This change cannot be undone and is
/// always presented with that warning; files in use are skipped.
/// </summary>
public sealed class TempCleanupRule : IOptimizationRule
{
    private static readonly TimeSpan MinimumAge = TimeSpan.FromDays(7);
    private readonly string _tempDirectory;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="TempCleanupRule"/> class.</summary>
    /// <param name="tempDirectory">Temp directory (defaults to the user's temp folder).</param>
    /// <param name="timeProvider">Time source.</param>
    public TempCleanupRule(string? tempDirectory = null, TimeProvider? timeProvider = null)
    {
        _tempDirectory = tempDirectory ?? Path.GetTempPath();
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Id => "storage.temp-cleanup";

    /// <inheritdoc />
    public string Name => "Clean up old temporary files";

    /// <inheritdoc />
    public string Description => "Deletes files older than 7 days from your temp folder. This cannot be undone. Files that are in use are skipped.";

    /// <inheritdoc />
    public OptimizationCategory Category => OptimizationCategory.Storage;

    /// <inheritdoc />
    public RiskLevel RiskLevel => RiskLevel.Low;

    /// <inheritdoc />
    public bool RequiresAdmin => false;

    /// <inheritdoc />
    public bool CanRollback => false;

    /// <inheritdoc />
    public bool RequiresRestart => false;

    /// <inheritdoc />
    public OsSupport SupportedOs => OsSupport.Windows10OrLater;

    /// <inheritdoc />
    public IReadOnlyList<RuleParameter> Parameters => [];

    /// <inheritdoc />
    public Task<RuleDetection> DetectAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var (count, bytes) = Measure();
        return Task.FromResult(bytes < 1024 * 1024
            ? new RuleDetection(DetectionState.NotApplicable, Units.FormatBytes(bytes), "-", "There is nothing worth cleaning up.")
            : new RuleDetection(DetectionState.Applicable, $"{count} files, {Units.FormatBytes(bytes)}", "0 files", Description));
    }

    /// <inheritdoc />
    public Task<RuleSnapshot> CaptureAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var (count, bytes) = Measure();
        return Task.FromResult(new RuleSnapshot
        {
            RuleId = Id,
            CapturedAt = _time.GetUtcNow(),
            Description = $"{count} files, {Units.FormatBytes(bytes)} (not restorable)",
            Values = new Dictionary<string, string?> { ["bytes"] = bytes.ToString(CultureInfo.InvariantCulture), ["count"] = count.ToString(CultureInfo.InvariantCulture) },
        });
    }

    /// <inheritdoc />
    public Task ApplyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        foreach (var file in OldFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if ((file.Attributes & (FileAttributes.System | FileAttributes.ReparsePoint)) == 0)
                {
                    file.Delete();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use or protected: skipped by design.
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RuleVerification> VerifyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
    {
        var (count, bytes) = Measure();
        return Task.FromResult(new RuleVerification(true, $"{count} files, {Units.FormatBytes(bytes)} remaining (in use)"));
    }

    /// <inheritdoc />
    public Task<RuleVerification> RollbackAsync(OptimizationContext context, RuleSnapshot snapshot, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RuleVerification(false, "-", "Deleted temporary files cannot be restored."));

    private (int Count, long Bytes) Measure()
    {
        var count = 0;
        long bytes = 0;
        foreach (var file in OldFiles())
        {
            count++;
            bytes += file.Length;
        }

        return (count, bytes);
    }

    private IEnumerable<FileInfo> OldFiles()
    {
        if (!Directory.Exists(_tempDirectory))
        {
            yield break;
        }

        var cutoff = _time.GetUtcNow().UtcDateTime - MinimumAge;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        foreach (var file in new DirectoryInfo(_tempDirectory).EnumerateFiles("*", options))
        {
            if (file.LastWriteTimeUtc < cutoff && file.LastAccessTimeUtc < cutoff)
            {
                yield return file;
            }
        }
    }
}

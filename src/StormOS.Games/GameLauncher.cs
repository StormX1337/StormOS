using System.Diagnostics;
using System.Text.Json;
using StormOS.Core.Common;
using StormOS.Core.Games;

namespace StormOS.Games;

/// <summary>Launches games through their launcher URI or executable. Only allow-listed URI schemes are opened.</summary>
public static class GameLauncher
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "com.epicgames.launcher", "uplay", "goggalaxy", "battlenet", "origin2", "link2ea",
    };

    /// <summary>Checks whether a launch URI uses an allowed scheme.</summary>
    /// <param name="uri">URI.</param>
    /// <returns><see langword="true"/> when allowed.</returns>
    public static bool IsAllowedUri(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && AllowedSchemes.Contains(parsed.Scheme);

    /// <summary>Launches a game.</summary>
    /// <param name="game">The game.</param>
    /// <returns>Success or a friendly error.</returns>
    public static Result Launch(GameInfo game)
    {
        ArgumentNullException.ThrowIfNull(game);
        try
        {
            if (IsAllowedUri(game.LaunchUri))
            {
                using var _ = Process.Start(new ProcessStartInfo(game.LaunchUri!) { UseShellExecute = true });
                return Result.Success;
            }

            if (game.Launcher == LauncherKind.Riot && TryRiotClient(game.LauncherGameId) is { } riot)
            {
                using var _ = Process.Start(riot);
                return Result.Success;
            }

            var exe = game.Executables.FirstOrDefault(e => File.Exists(e) && game.InstallPath is not null && Path.GetFullPath(e).StartsWith(Path.GetFullPath(game.InstallPath), StringComparison.OrdinalIgnoreCase));
            if (exe is null)
            {
                return Result.Failure(StormErrorCodes.NotSupported, $"{game.Name} cannot be started from STORM OS. Start it from its launcher.");
            }

            using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
            return Result.Success;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return Result.Failure(StormError.FromException(StormErrorCodes.Internal, $"{game.Name} could not be started.", ex));
        }
    }

    private static ProcessStartInfo? TryRiotClient(string? product)
    {
        if (product is not ("valorant" or "league_of_legends" or "bacon"))
        {
            return null;
        }

        var installs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games", "RiotClientInstalls.json");
        if (!File.Exists(installs))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(installs));
        if (!document.RootElement.TryGetProperty("rc_default", out var path) || path.GetString() is not { } client || !File.Exists(client))
        {
            return null;
        }

        var info = new ProcessStartInfo(client) { UseShellExecute = false };
        info.ArgumentList.Add("--launch-product=" + product);
        info.ArgumentList.Add("--launch-patchline=live");
        return info;
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StormOS.Core.Games;
using StormOS.Games.Profiles;

namespace StormOS.Games.Tests;

public sealed class ProfileEngineTests
{
    private static string ProfilesDirectory()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !Directory.Exists(Path.Combine(directory, "profiles")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return Path.Combine(directory ?? throw new DirectoryNotFoundException("profiles"), "profiles");
    }

    private static GameProfileRepository Repository(params string[] directories) =>
        new(Options.Create(new GameProfileOptions { Directories = directories }), NullLogger<GameProfileRepository>.Instance);

    [Fact]
    public void BundledProfiles_AllValidate()
    {
        var repository = Repository(ProfilesDirectory());

        Assert.Empty(repository.LoadErrors);
        Assert.True(repository.Profiles.Count >= 10);
        Assert.Contains(repository.Profiles, p => p.Id == "cs2");
    }

    [Fact]
    public void MatchProcess_UsesExecutablePathAndCommandLine()
    {
        var repository = Repository(ProfilesDirectory());

        Assert.Equal("cs2", repository.MatchProcess(@"C:\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe", null)?.Id);
        Assert.Null(repository.MatchProcess(@"C:\Other\cs2.exe", null));
        Assert.Equal("fivem", repository.MatchProcess(@"C:\FiveM\FiveM_b2944_GTAProcess.exe", null)?.Id);
        Assert.Equal("minecraft", repository.MatchProcess(@"C:\Java\bin\javaw.exe", "-cp x net.minecraft.client.main.Main")?.Id);
        Assert.Null(repository.MatchProcess(@"C:\Java\bin\javaw.exe", "-jar some-other-tool.jar"));
        Assert.True(repository.NeedsCommandLine(@"C:\Java\bin\javaw.exe"));
        Assert.False(repository.NeedsCommandLine(@"C:\x\cs2.exe"));
    }

    [Fact]
    public void MatchGame_ByLauncherId()
    {
        var repository = Repository(ProfilesDirectory());
        var game = new GameInfo { GameId = "steam:730", Launcher = LauncherKind.Steam, LauncherGameId = "730" };

        Assert.Equal("cs2", repository.MatchGame(game)?.Id);
    }

    [Fact]
    public void Validator_RejectsUnsafeProfiles()
    {
        var bad = new GameProfile
        {
            SchemaVersion = 99,
            Id = "Bad Id",
            Version = "one",
            Name = string.Empty,
            Detection = new ProfileDetection { Executables = [new ExecutableMatch(@"..\evil.exe")] },
            OptimizationRules = [new ProfileRuleReference { RuleId = "rm -rf", Parameters = new Dictionary<string, string> { ["x"] = "a\nb" } }],
            Launch = new ProfileLaunch { Uri = "file:///c:/windows/system32/cmd.exe" },
        };

        var errors = GameProfileValidator.Validate(bad);

        Assert.True(errors.Count >= 7, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void Overrides_ReplaceBundledProfilesById()
    {
        var overrides = Path.Combine(Path.GetTempPath(), "profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(overrides);
        try
        {
            File.WriteAllText(Path.Combine(overrides, "cs2.json"), """
                { "schemaVersion": 1, "id": "cs2", "version": "9.0.0", "name": "CS2 (managed)",
                  "detection": { "executables": [ { "name": "cs2.exe" } ] } }
                """);
            File.WriteAllText(Path.Combine(overrides, "broken.json"), "{ not json");

            var repository = Repository(ProfilesDirectory(), overrides);

            Assert.Equal("9.0.0", repository.Find("cs2")!.Version);
            Assert.Single(repository.LoadErrors);
        }
        finally
        {
            Directory.Delete(overrides, recursive: true);
        }
    }

    [Theory]
    [InlineData("steam://rungameid/730", true)]
    [InlineData("com.epicgames.launcher://apps/Fortnite?action=launch", true)]
    [InlineData("file:///c:/windows/system32/cmd.exe", false)]
    [InlineData("ms-settings:privacy", false)]
    [InlineData("not a uri", false)]
    public void LaunchUris_AreAllowListed(string uri, bool allowed) => Assert.Equal(allowed, GameLauncher.IsAllowedUri(uri));
}

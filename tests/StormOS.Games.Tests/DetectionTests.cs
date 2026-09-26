using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StormOS.Core.Games;
using StormOS.Games.Detection;
using StormOS.Games.Profiles;

namespace StormOS.Games.Tests;

public sealed class DetectionTests
{
    [Fact]
    public async Task Detector_RaisesStartAndStop_AndPrefersForeground()
    {
        var profiles = Path.Combine(Path.GetTempPath(), "det-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profiles);
        File.WriteAllText(Path.Combine(profiles, "g.json"), """{ "schemaVersion": 1, "id": "g", "version": "1.0.0", "name": "Game G", "detection": { "executables": [ { "name": "g.exe" } ] } }""");
        try
        {
            var repository = new GameProfileRepository(Options.Create(new GameProfileOptions { Directories = [profiles] }), NullLogger<GameProfileRepository>.Instance);
            var registry = new FakeRegistry(new GameInfo { GameId = "steam:1", Name = "Library Game", Launcher = LauncherKind.Steam, InstallPath = @"D:\Games\Lib" });
            var processes = new FakeProcesses();
            using var detector = new RunningGameDetector(registry, repository, processes, new GameDetectionOptions(), NullLogger<RunningGameDetector>.Instance);
            var started = new List<RunningGame>();
            var stopped = new List<RunningGame>();
            detector.GameStarted += (_, g) => started.Add(g);
            detector.GameStopped += (_, g) => stopped.Add(g);

            processes.Items = [new(100, "g", @"C:\G\g.exe", DateTime.UnixEpoch), new(200, "explorer", @"C:\Windows\explorer.exe", DateTime.UnixEpoch), new(300, "Lib", @"D:\Games\Lib\bin\Lib.exe", DateTime.UnixEpoch), new(400, "UnityCrashHandler64", @"D:\Games\Lib\UnityCrashHandler64.exe", DateTime.UnixEpoch)];
            processes.Foreground = 300;
            await detector.PollAsync(TestContext.Current.CancellationToken);

            Assert.Equal([100, 300], started.Select(g => g.ProcessId).Order());
            Assert.Equal("Game G", started.Single(g => g.ProcessId == 100).DisplayName);
            Assert.Equal("Library Game", detector.Primary!.DisplayName);
            Assert.Equal(("Library Game", 300), detector.ActiveGame);

            processes.Items = [new(300, "Lib", @"D:\Games\Lib\bin\Lib.exe", DateTime.UnixEpoch)];
            await detector.PollAsync(TestContext.Current.CancellationToken);

            Assert.Equal(100, Assert.Single(stopped).ProcessId);
            Assert.Single(detector.Running);
        }
        finally
        {
            Directory.Delete(profiles, recursive: true);
        }
    }

    [Theory]
    [InlineData("UnityCrashHandler64", true)]
    [InlineData("EpicGamesLauncher", true)]
    [InlineData("steamwebhelper", true)]
    [InlineData("cs2", false)]
    [InlineData("RocketLeague", false)]
    public void Helpers_AreExcluded(string name, bool helper) => Assert.Equal(helper, RunningGameDetector.IsHelper(name));

    private sealed class FakeProcesses : IProcessSource
    {
        public IReadOnlyList<ProcessSnapshot> Items { get; set; } = [];

        public int? Foreground { get; set; }

        public IReadOnlyList<ProcessSnapshot> List() => Items;

        public string? GetCommandLine(int processId) => null;

        public int? GetForegroundProcessId() => Foreground;
    }

    private sealed class FakeRegistry(params GameInfo[] games) : IGameRegistry
    {
        public Task<IReadOnlyList<GameInfo>> GetGamesAsync(bool refresh = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GameInfo>>(games);

        public GameInfo? Find(string gameId) => games.FirstOrDefault(g => g.GameId == gameId);

        public GameInfo? MatchExecutable(string executablePath) =>
            games.FirstOrDefault(g => g.InstallPath is not null && executablePath.StartsWith(g.InstallPath + "\\", StringComparison.OrdinalIgnoreCase));
    }
}

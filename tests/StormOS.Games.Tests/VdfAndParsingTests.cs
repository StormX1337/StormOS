using System.Text;
using StormOS.Core.Games;
using StormOS.Games.Parsing;
using StormOS.Games.Scanners;

namespace StormOS.Games.Tests;

public sealed class VdfAndParsingTests
{
    private const string LibraryFolders = """
        "libraryfolders"
        {
            "0"
            {
                "path"      "C:\\Program Files (x86)\\Steam"
                "apps" { "730" "123" }
            }
            // secondary library
            "1"
            {
                "path"      "D:\\SteamLibrary"
            }
        }
        """;

    private const string Manifest = """
        "AppState"
        {
            "appid"     "730"
            "name"      "Counter-Strike 2"
            "StateFlags"    "4"
            "installdir"    "Counter-Strike Global Offensive"
            "LastPlayed"    "1717000000"
            "SizeOnDisk"    "36000000000"
            "buildid"   "14000000"
        }
        """;

    [Fact]
    public void Vdf_ParsesNestedNodesCommentsAndEscapes()
    {
        var root = VdfParser.Parse(LibraryFolders).Node("libraryfolders")!;

        Assert.Equal(["0", "1"], root.Nodes().Select(n => n.Key));
        Assert.Equal(@"C:\Program Files (x86)\Steam", root.Node("0")!.Value("path"));
        Assert.Equal("123", root.Node("0")!.Node("apps")!.Value("730"));
    }

    [Theory]
    [InlineData("\"a\" {")]
    [InlineData("\"a\" \"b\" }")]
    [InlineData("\"unterminated")]
    public void Vdf_RejectsMalformed(string text) => Assert.Throws<FormatException>(() => VdfParser.Parse(text));

    [Fact]
    public void Vdf_RejectsExcessiveNesting()
    {
        var text = string.Concat(Enumerable.Repeat("\"a\" {", 40)) + string.Concat(Enumerable.Repeat("}", 40));
        Assert.Throws<FormatException>(() => VdfParser.Parse(text));
    }

    [Fact]
    public void SteamManifest_BecomesGame()
    {
        var game = SteamLibraryScanner.FromManifest(VdfParser.Parse(Manifest), @"D:\SteamLibrary\steamapps")!;

        Assert.Equal("steam:730", game.GameId);
        Assert.Equal("Counter-Strike 2", game.Name);
        Assert.True(game.IsInstalled);
        Assert.Equal("steam://rungameid/730", game.LaunchUri);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1717000000), game.LastPlayed);
        Assert.EndsWith("Counter-Strike Global Offensive", game.InstallPath, StringComparison.Ordinal);
    }

    [Fact]
    public void SteamManifest_SkipsRedistributables()
    {
        var redist = Manifest.Replace("\"730\"", "\"228980\"", StringComparison.Ordinal);
        Assert.Null(SteamLibraryScanner.FromManifest(VdfParser.Parse(redist), "x"));
    }

    [Fact]
    public async Task SteamScanner_ReadsLibrariesFromDisk()
    {
        var root = Path.Combine(Path.GetTempPath(), "steam-" + Guid.NewGuid().ToString("N"));
        var steamApps = Path.Combine(root, "steamapps");
        Directory.CreateDirectory(steamApps);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(steamApps, "libraryfolders.vdf"), "\"libraryfolders\" { }", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(steamApps, "appmanifest_730.acf"), Manifest, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(steamApps, "appmanifest_1.acf"), "broken {", TestContext.Current.CancellationToken);
            var scanner = new SteamLibraryScanner(new NoRegistry(), Microsoft.Extensions.Logging.Abstractions.NullLogger<SteamLibraryScanner>.Instance, root);

            var games = await scanner.ScanAsync(TestContext.Current.CancellationToken);

            Assert.Equal("steam:730", Assert.Single(games).GameId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("cs2.exe", "CS2.EXE", true)]
    [InlineData("FiveM_*GTAProcess.exe", "FiveM_b2944_GTAProcess.exe", true)]
    [InlineData("FortniteClient-Win64-Shipping*.exe", "FortniteClient-Win64-Shipping_EAC_EOS.exe", true)]
    [InlineData("cod24*.exe", "cod.exe", false)]
    [InlineData("?s2.exe", "cs2.exe", true)]
    [InlineData("*", "", true)]
    public void Wildcard(string pattern, string text, bool expected) => Assert.Equal(expected, WildcardMatcher.IsMatch(pattern, text));

    [Fact]
    public void GamingRoot_ParsesUtf16Paths()
    {
        var payload = new List<byte>();
        payload.AddRange("RGBX"u8.ToArray());
        payload.AddRange(BitConverter.GetBytes(1));
        payload.AddRange(Encoding.Unicode.GetBytes("XboxGames\0"));

        Assert.Equal(["XboxGames"], GamingRootReader.Parse(payload.ToArray()));
        Assert.Empty(GamingRootReader.Parse("NOPE0000"u8.ToArray()));
    }

    [Fact]
    public void GamingRoot_RejectsTraversal()
    {
        var payload = new List<byte>();
        payload.AddRange("RGBX"u8.ToArray());
        payload.AddRange(BitConverter.GetBytes(1));
        payload.AddRange(Encoding.Unicode.GetBytes("..\\Windows\0"));

        Assert.Empty(GamingRootReader.Parse(payload.ToArray()));
    }

    [Fact]
    public void EpicManifest_BecomesGame()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""
            { "AppName": "Fortnite", "DisplayName": "Fortnite", "InstallLocation": "C:\\Games\\Fortnite",
              "LaunchExecutable": "FortniteGame\\Binaries\\Win64\\FortniteLauncher.exe", "AppVersionString": "++Fortnite+Release-30.00", "InstallSize": 12345 }
            """);

        var game = EpicLibraryScanner.FromManifest(document.RootElement)!;

        Assert.Equal("epic:fortnite", game.GameId);
        Assert.Equal(12345, game.SizeBytes);
        Assert.Contains("com.epicgames.launcher://apps/Fortnite", game.LaunchUri, StringComparison.Ordinal);
    }

    [Fact]
    public void EpicManifest_IncompleteInstallIgnored()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""{ "AppName": "X", "DisplayName": "X", "InstallLocation": "C:\\X", "bIsIncompleteInstall": true }""");
        Assert.Null(EpicLibraryScanner.FromManifest(document.RootElement));
    }

    [Fact]
    public void XboxConfig_BecomesGame()
    {
        var document = System.Xml.Linq.XDocument.Parse("""
            <Game configVersion="1">
              <Identity Name="Microsoft.624F8B84B80" Publisher="CN=A" Version="1.2.3.0" />
              <ExecutableList><Executable Name="gamelaunchhelper.exe" Id="Game" /></ExecutableList>
              <ShellVisuals DefaultDisplayName="Forza Horizon 5" PublisherDisplayName="Xbox Game Studios" />
            </Game>
            """);

        var game = XboxLibraryScanner.FromConfig(document, @"C:\XboxGames\Forza Horizon 5")!;

        Assert.Equal("Forza Horizon 5", game.Name);
        Assert.Equal("Xbox Game Studios", game.Publisher);
        Assert.Equal(LauncherKind.Xbox, game.Launcher);
        Assert.EndsWith("gamelaunchhelper.exe", Assert.Single(game.Executables), StringComparison.Ordinal);
    }

    [Fact]
    public void RiotYaml_ReadsInstallPath()
    {
        string[] lines = ["product_install_full_path: \"C:/Riot Games/VALORANT/live\"", "product_install_root: \"C:/Riot Games\""];
        Assert.Equal("C:/Riot Games/VALORANT/live", RiotLibraryScanner.ReadYamlValue(lines, "product_install_full_path"));
        Assert.Null(RiotLibraryScanner.ReadYamlValue(lines, "missing"));
    }

    private sealed class NoRegistry : StormOS.Windows.RegistryAccess.IRegistryAccess
    {
        public StormOS.Windows.RegistryAccess.RegistryValue? GetValue(StormOS.Windows.RegistryAccess.RegistryHive hive, string keyPath, string valueName) => null;

        public void SetValue(StormOS.Windows.RegistryAccess.RegistryHive hive, string keyPath, string valueName, StormOS.Windows.RegistryAccess.RegistryValue value) => throw new NotSupportedException();

        public void DeleteValue(StormOS.Windows.RegistryAccess.RegistryHive hive, string keyPath, string valueName) => throw new NotSupportedException();

        public IReadOnlyList<string> GetValueNames(StormOS.Windows.RegistryAccess.RegistryHive hive, string keyPath) => [];

        public IReadOnlyList<string> GetSubKeyNames(StormOS.Windows.RegistryAccess.RegistryHive hive, string keyPath) => [];
    }
}

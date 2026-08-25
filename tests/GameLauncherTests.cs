using Xunit;

namespace COTK.Launcher.Tests;

public sealed class GameLauncherTests
{
    [Fact]
    public void BuildClientConfig_ReplacesDuplicateSessionAndServerKeys()
    {
        var source = new[]
        {
            "World=None",
            " Server=192.168.1.10:20042",
            "SessionId=old",
            "  sessionid=older",
            "[Environment]",
            "Sku=2",
        };

        var result = GameLauncher.BuildClientConfig(source, "lp2.account.exp.signature");

        Assert.Equal("SessionId=lp2.account.exp.signature", result[0]);
        Assert.Equal("Server=127.0.0.1:20042", result[1]);
        Assert.Single(result, line => line.StartsWith("SessionId=", StringComparison.OrdinalIgnoreCase));
        Assert.Single(result, line => line.StartsWith("Server=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("World=None", result);
        Assert.Contains("[Environment]", result);
    }

    [Theory]
    [InlineData("BaseApp::Run - beginning to run the app", 0)]
    [InlineData("newState=GAMESTATE_TITLESCREEN", 1)]
    [InlineData("newState=cClientRunStateCharacterCreateOrDelete", 1)]
    [InlineData("newState=GAMESTATE_LOADINGSCREEN", 2)]
    [InlineData("WaitForWorldReady: Waiting for confirmation packet", 2)]
    [InlineData("newState=cClientRunStateRunning", 3)]
    [InlineData("newState=GAMESTATE_INGAME", 3)]
    [InlineData("newState=cClientRunStateRunning\nnewState=GAMESTATE_LOADINGSCREEN", 2)]
    [InlineData("newState=GAMESTATE_LOADINGSCREEN\nnewState=cClientRunStateRunning", 3)]
    public void DetectClientStage_MapsKnownClientMarkers(string log, int expectedValue)
    {
        Assert.Equal((GameLauncher.ClientStage)expectedValue, GameLauncher.DetectClientStage(log));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    [InlineData(2, false)]
    [InlineData(0, false)]
    public void AttemptResult_RetriesOnlyRecoverableFailures(int outcomeValue, bool expected)
    {
        var result = new GameLauncher.AttemptResult(
            (GameLauncher.AttemptOutcome)outcomeValue,
            GameLauncher.ClientStage.Starting,
            42,
            unchecked((int)0xC0000005),
            TimeSpan.FromSeconds(7));

        Assert.Equal(expected, result.ShouldRetry);
    }

    [Theory]
    [InlineData("Unable to authenticate with Login Server.", true)]
    [InlineData("Beginning to shutdown the game client reason WaitForCharacterList.", false)]
    public void WasAuthenticationRejected_RecognizesClientFailure(string log, bool expected)
    {
        Assert.Equal(expected, GameLauncher.WasAuthenticationRejected(log));
    }

    [Theory]
    [InlineData("0.8.2", "0.8.1", true)]
    [InlineData("0.8.1", "0.8.1", false)]
    [InlineData("0.8.0", "0.8.1", false)]
    public void IsNewer_ComparesReleaseVersions(string remote, string local, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewer(remote, local));
    }

    [Fact]
    public void ValidateManifest_AcceptsV1Manifest()
    {
        var manifest = new UpdateManifest(
            1,
            new LauncherRelease("0.8.1", "https://downloads.example.invalid/launcher.zip", new string('a', 64), 123),
            new ClientRelease("0.23.4.161178", "https://downloads.example.invalid/client.zip", new string('b', 64), 456));

        UpdateService.ValidateManifest(manifest);
    }
}

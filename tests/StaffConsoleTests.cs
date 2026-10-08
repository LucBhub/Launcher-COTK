using Xunit;

namespace COTK.Launcher.Tests;

/// <summary>Logique pure du bouton console F8 : quand proposer
/// INSTALLER / METTRE A JOUR, independamment de l'UI WinForms.</summary>
public sealed class StaffConsoleTests
{
    private static StaffConsoleInfo Info(string sha)
        => new(sha, 12345);

    [Fact]
    public void MissingConsoleMustInstall()
    {
        Assert.True(StaffConsole.ShouldInstall(null, Info(new string('a', 64))));
    }

    [Fact]
    public void SameShaMustNotInstall()
    {
        var sha = new string('b', 64);
        Assert.False(StaffConsole.ShouldInstall(sha, Info(sha)));
    }

    [Fact]
    public void SameShaDifferentCaseMustNotInstall()
    {
        Assert.False(StaffConsole.ShouldInstall(
            new string('C', 64), Info(new string('c', 64))));
    }

    [Fact]
    public void DifferentShaMustUpdate()
    {
        Assert.True(StaffConsole.ShouldInstall(
            new string('d', 64), Info(new string('e', 64))));
    }
}

using GrbLHALSender.Utility;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for reducing the assembly's informational version to a bare version string.
/// The update check parses the result with Version.TryParse to decide whether a newer
/// release exists, so a leftover suffix silently disables update notifications.
/// </summary>
public class AppVersionTests
{
    [Theory]
    [InlineData("1.0.11", "1.0.11")]
    [InlineData("1.0.11+49b5450f18e3b41e9a37cecbba6727603c51fe8c", "1.0.11")] // MSBuild appends the commit
    [InlineData("1.0.11-dev", "1.0.11")]
    [InlineData("1.0.11-rc.2+abc123", "1.0.11")]
    [InlineData("1.0", "1.0")]
    public void Normalize_StripsSuffixes(string raw, string expected)
    {
        Assert.Equal(expected, AppVersion.Normalize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+abc123")]  // nothing left once the metadata goes
    [InlineData("-dev")]
    public void Normalize_ReturnsNullWhenThereIsNoVersion(string? raw)
    {
        Assert.Null(AppVersion.Normalize(raw));
    }

    [Fact]
    public void Normalize_ProducesSomethingVersionTryParseAccepts()
    {
        // This is what the update check actually does with the value.
        var normalized = AppVersion.Normalize("1.0.11+49b5450");
        Assert.True(System.Version.TryParse(normalized, out var parsed));
        Assert.Equal(new System.Version(1, 0, 11), parsed);
    }

    [Fact]
    public void Display_NeverReturnsBlank()
    {
        // Shown in the dialog header, where an empty string would read as a bug.
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Display));
    }
}

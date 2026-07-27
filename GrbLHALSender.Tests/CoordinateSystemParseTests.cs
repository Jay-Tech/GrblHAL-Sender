using GrbLHALSender.Communication;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for parsing the axis values the controller reports in <c>$#</c>, used to read
/// back the stored G59.3 offset — the tool setter position for tool change modes 2 and 3.
/// A misparse here would show the operator a wrong position for a coordinate system they
/// are about to overwrite.
/// </summary>
public class CoordinateSystemParseTests
{
    [Fact]
    public void ParseAxisValues_ReadsThreeAxes()
    {
        var values = CommunicationManager.ParseAxisValues("483.425,-50.250,-10.000");

        Assert.NotNull(values);
        Assert.Equal([483.425, -50.250, -10.000], values);
    }

    [Fact]
    public void ParseAxisValues_ReadsMoreThanThreeAxes()
    {
        // Rotary axes are reported too on a machine configured for them.
        var values = CommunicationManager.ParseAxisValues("1.0,2.0,3.0,4.0");

        Assert.NotNull(values);
        Assert.Equal(4, values.Length);
    }

    [Fact]
    public void ParseAxisValues_ToleratesSpaces()
    {
        var values = CommunicationManager.ParseAxisValues(" 1.5 , -2.5 , 0.0 ");

        Assert.NotNull(values);
        Assert.Equal([1.5, -2.5, 0.0], values);
    }

    [Fact]
    public void ParseAxisValues_UsesDotDecimalRegardlessOfCulture()
    {
        // grblHAL always speaks dot-decimal; a comma-decimal reading would both mangle
        // the number and change the axis count.
        var values = CommunicationManager.ParseAxisValues("12.345,0.000,0.000");

        Assert.NotNull(values);
        Assert.Equal(12.345, values[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc,1.0,2.0")]
    [InlineData("1.0,,2.0")]
    [InlineData("1.0,2.0,")]
    public void ParseAxisValues_ReturnsNullRatherThanAPartialPosition(string csv)
    {
        // Half a position is worse than none: the caller would display or act on it.
        Assert.Null(CommunicationManager.ParseAxisValues(csv));
    }
}

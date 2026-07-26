using System.Threading.Tasks;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests that request/response queries refuse to send while a job is streaming.
/// The job streamer uses grblHAL's character-counting protocol, so an out-of-band
/// command both occupies the controller's RX buffer unaccounted and produces an "ok"
/// that gets credited to a streamed line that has not finished.
/// <para>
/// These assertions rely on the guard returning before it touches the comms adapter:
/// no adapter is connected here, so a query that actually tried to send would fail.
/// </para>
/// </summary>
public class StreamingGuardTests
{
    private static CommunicationManager NewManager() => new(new ConfigManager());

    [Fact]
    public void IsStreaming_TracksBeginAndEnd()
    {
        var manager = NewManager();
        Assert.False(manager.IsStreaming);

        manager.BeginStreaming();
        Assert.True(manager.IsStreaming);

        manager.EndStreaming();
        Assert.False(manager.IsStreaming);
    }

    [Fact]
    public async Task SendCommandCollectResponsesAsync_RefusesWhileStreaming()
    {
        var manager = NewManager();
        manager.BeginStreaming();

        Assert.Empty(await manager.SendCommandCollectResponsesAsync("$pinstate"));
    }

    [Fact]
    public async Task SendAsyncCommand_RefusesWhileStreaming()
    {
        var manager = NewManager();
        manager.BeginStreaming();

        Assert.False(await manager.SendAsyncCommand("$I+"));
    }

    [Fact]
    public async Task QueryPinStatesAsync_ReturnsNothingWhileStreaming()
    {
        var manager = NewManager();
        manager.BeginStreaming();

        Assert.Empty(await manager.QueryPinStatesAsync());
    }

    [Fact]
    public async Task GetSettingDescriptionAsync_ReturnsNullWhileStreaming()
    {
        var manager = NewManager();
        manager.BeginStreaming();

        Assert.Null(await manager.GetSettingDescriptionAsync(100));
    }
}

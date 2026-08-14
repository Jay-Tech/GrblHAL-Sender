using System;
using System.Net.Sockets;
using System.Text;

namespace GrbLHALSender.Pendant;

/// <summary>
/// One pendant connection, whatever is carrying it.
/// </summary>
/// <remarks>
/// The pendant speaks the same newline-delimited JSON however it arrives, so
/// everything above the transport - parsing, jogging, buttons, the status feed -
/// should not know or care which one it came in on. Before this the service was
/// written against <see cref="NetworkStream"/> directly, which made a second
/// transport a change to every one of those places rather than an addition
/// beside them.
///
/// The pendant reaches the sender two ways. Over WiFi it opens a TCP session to
/// this application. Over ESP-NOW it talks to a receiver board plugged into this
/// machine, which presents a serial port - so the sender reads a COM port and the
/// radio is invisible to it.
/// </remarks>
internal interface IPendantChannel
{
    /// <summary>False once the far end has gone; the owning loop tears down.</summary>
    bool IsOpen { get; }

    /// <summary>Where this pendant is, for the console. Not an identity.</summary>
    string Describe();

    /// <summary>
    /// Send one message, terminated. Never throws: a write failing means the
    /// peer has gone, which the reading loop notices and handles once, rather
    /// than every writer discovering it separately.
    /// </summary>
    void WriteLine(string message);

    /// <summary>Drop the connection. Safe to call twice.</summary>
    void Close();
}

/// <summary>A pendant that came in over the network.</summary>
internal sealed class TcpPendantChannel : IPendantChannel
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly string _describe;

    public TcpPendantChannel(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        // Captured now: reading RemoteEndPoint after the socket closes throws,
        // and the place this is most wanted is the message announcing that it
        // has closed.
        _describe = client.Client.RemoteEndPoint?.ToString() ?? "network";
    }

    public NetworkStream Stream => _stream;

    public bool IsOpen => _client.Connected;

    public string Describe() => _describe;

    public void WriteLine(string message)
    {
        try
        {
            var payload = Encoding.UTF8.GetBytes(message + "\n");
            _stream.Write(payload, 0, payload.Length);
        }
        catch (Exception)
        {
            // Peer has gone; the session loop notices and tears down.
        }
    }

    public void Close()
    {
        try { _client.Close(); } catch { /* already gone */ }
    }
}

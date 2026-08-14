using System;
using System.IO.Ports;
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
    ///
    /// Two threads write here - the status loop on its own tick, and the reader
    /// answering a ping - so implementations serialise. An interleaved write
    /// splits one JSON object across two lines and the pendant sees both halves
    /// as malformed.
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
    private readonly object _writeLock = new();

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
            lock (_writeLock) _stream.Write(payload, 0, payload.Length);
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

/// <summary>A pendant that came in through a receiver board on a serial port.</summary>
/// <remarks>
/// The lifetime here is not the port's, which is the whole difference from the
/// network transport. A socket exists only because a pendant opened it, so it can
/// be closed when that pendant goes. The receiver board is plugged into this
/// machine permanently and its port is open from the moment it enumerates,
/// whether or not a handheld is switched on or in range.
///
/// So this represents the pendant's session through the port, not the port
/// itself: <see cref="Close"/> ends the session and leaves the port open for the
/// next hello. Closing the port with the session would mean one flat battery took
/// the receiver down until the application was restarted.
///
/// The port belongs to the reader loop, which opens it, hands out the channel,
/// and is the only thing that disposes it.
/// </remarks>
internal sealed class SerialPendantChannel : IPendantChannel
{
    private readonly SerialPort _port;
    private readonly string _describe;
    private readonly object _writeLock = new();
    private volatile bool _closed;

    public SerialPendantChannel(SerialPort port)
    {
        _port = port;
        _describe = port.PortName;
    }

    public bool IsOpen => !_closed && _port.IsOpen;

    public string Describe() => _describe;

    public void WriteLine(string message)
    {
        if (_closed) return;

        try
        {
            // Written as bytes rather than through SerialPort.Write(string),
            // which encodes with the port's own Encoding - ASCII by default,
            // which would silently replace anything outside it. The network
            // transport puts UTF-8 on the wire and the receiver forwards bytes
            // unaltered, so this has to agree with it.
            var payload = Encoding.UTF8.GetBytes(message + "\n");
            lock (_writeLock) _port.Write(payload, 0, payload.Length);
        }
        catch (Exception)
        {
            // Cable pulled, receiver reset, or its buffer stopped draining. The
            // reader loop owns the port and rebuilds it; nothing here needs to.
        }
    }

    /// <summary>
    /// Ends this pendant's session. Deliberately leaves the port open - see the
    /// remarks on the class.
    /// </summary>
    public void Close() => _closed = true;
}

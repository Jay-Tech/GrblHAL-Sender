using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GrbLHALSender.Communication
{
    public class WebSocket : ICommsAdapter
    {
        public event EventHandler<string>? OnDataReceived;

        private WebSocketSettings _settings;
        private ClientWebSocket? _client;
        private ConcurrentQueue<byte[]> _sendQue = new();
        private CancellationTokenSource? _tokenSource;
        private string _receiveBuffer = string.Empty;

        private static readonly object _syncLock = new();
        private static readonly char[] Split = { '\r', '\n' };

        public bool IsConnected { get; set; }

        public WebSocket(WebSocketSettings settings)
        {
            TryConnect(settings);
        }

        public bool TryConnect(WebSocketSettings settings)
        {
            _settings = settings;

            try
            {
                _client = new ClientWebSocket();

                // Build the WebSocket URI (ws:// for unencrypted)
                var uri = new Uri($"ws://{_settings.IpAddress}:{_settings.PortNumber}");

                // Connect with a timeout
                var connectTask = _client.ConnectAsync(uri, CancellationToken.None);
                if (!connectTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    _client.Dispose();
                    _client = null;
                    return false;
                }

                if (_client.State != WebSocketState.Open)
                {
                    _client.Dispose();
                    _client = null;
                    return false;
                }

                IsConnected = true;

                _tokenSource = new CancellationTokenSource();
                var token = _tokenSource.Token;

                Task.Factory.StartNew(() => SendLoop(token), TaskCreationOptions.LongRunning);
                Task.Factory.StartNew(() => ReceiveLoop(token), TaskCreationOptions.LongRunning);

                return true;
            }
            catch (Exception)
            {
                _client?.Dispose();
                _client = null;
                IsConnected = false;
                return false;
            }
        }

        public void Close()
        {
            _tokenSource?.Cancel();
            Thread.Sleep(100);

            IsConnected = false;

            try
            {
                if (_client?.State == WebSocketState.Open)
                {
                    _client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing",
                        CancellationToken.None).Wait(TimeSpan.FromSeconds(2));
                }

                _client?.Dispose();
            }
            catch (Exception)
            {
                // Ignore disposal exceptions
            }

            _client = null;
        }

        public void WriteByte(byte data)
        {
            var ca = new byte[1];
            ca[0] = data;
            SendQue(ca);
        }

        public void WriteCommand(string command)
        {
            if (!IsConnected || _client == null) return;

            if (command.Length == 1)
            {
                WriteByte((byte)command.ToCharArray()[0]);
            }
            else
            {
                command += "\r";
                byte[] bytes = Encoding.UTF8.GetBytes(command);
                SendQue(bytes);
            }
        }

        private void SendQue(byte[] command)
        {
            _sendQue.Enqueue(command);
        }

        private void SendLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (_sendQue.TryDequeue(out var command))
                {
                    try
                    {
                        if (IsConnected && _client?.State == WebSocketState.Open)
                        {
                            var segment = new ArraySegment<byte>(command);
                            _client.SendAsync(segment, WebSocketMessageType.Binary,
                                true, token).Wait(token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception)
                    {
                        IsConnected = false;
                        return;
                    }

                    //Thread.Sleep(10);
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }

        private void ReceiveLoop(CancellationToken token)
        {
            var buffer = new byte[2048];
            var segment = new ArraySegment<byte>(buffer);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_client == null || _client.State != WebSocketState.Open || !IsConnected)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    var receiveTask = _client.ReceiveAsync(segment, token);
                    receiveTask.Wait(token);

                    var result = receiveTask.Result;

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        IsConnected = false;
                        return;
                    }

                    if (result.Count > 0)
                    {
                        string received = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        ProcessReceivedData(received);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    if (!token.IsCancellationRequested)
                    {
                        IsConnected = false;
                    }
                    return;
                }
            }
        }

        private void ProcessReceivedData(string received)
        {
            lock (_syncLock)
            {
                _receiveBuffer += received;

                while (true)
                {
                    var indexSlice = _receiveBuffer.IndexOfAny(Split);
                    if (indexSlice < 0) break;

                    var data = _receiveBuffer[..indexSlice];
                    if (data.Length != 0)
                    {
                        OnDataReceived?.Invoke(this, data);
                    }

                    // Skip past the line ending character(s)
                    int next = indexSlice + 1;
                    while (next < _receiveBuffer.Length &&
                           (_receiveBuffer[next] == '\r' || _receiveBuffer[next] == '\n'))
                    {
                        next++;
                    }
                    _receiveBuffer = _receiveBuffer[next..];
                }
            }
        }
    }
}

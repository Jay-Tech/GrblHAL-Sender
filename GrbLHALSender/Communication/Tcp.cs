using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GrbLHALSender.Communication
{
    public class Tcp : ObservableObject, ICommsAdapter
    {
        public event EventHandler<string>? OnDataReceived;
        public event EventHandler<byte[]>? OnRawDataReceived;

        private TcpSettings _tcpSettings;
        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private ConcurrentQueue<byte[]> _sendQue = new();
        private CancellationTokenSource? _tokenSource;
        private string _receiveBuffer = string.Empty;

        private static readonly object _syncLock = new();
        private static readonly char[] Split = { '\r', '\n' };

        public bool IsConnected
        {
            get;
            set
            {
                if (value == field) return;
                field = value;
                OnPropertyChanged();
            }
        }

        private volatile bool _userClosed = false;
        private int _reconnecting = 0;

        public Tcp(TcpSettings settings)
        {
            TryConnect(settings);
        }

        public bool TryConnect(TcpSettings settings)
        {
            _tcpSettings = settings;
            _userClosed = false;
            Interlocked.Exchange(ref _reconnecting, 0);

            // Clean up any previous connection before reconnecting
            if (_tokenSource != null)
            {
                _tokenSource.Cancel();
                Thread.Sleep(50);
                _tokenSource.Dispose();
                _tokenSource = null;
            }
            try { _networkStream?.Close(); } catch { }
            try { _tcpClient?.Close(); _tcpClient?.Dispose(); } catch { }
            _networkStream = null;
            _tcpClient = null;

            try
            {
                _tcpClient = new TcpClient();
                // Connect with a timeout so we don't hang indefinitely
                var connectTask = _tcpClient.ConnectAsync(_tcpSettings.IpAddress, _tcpSettings.PortNumber);
                if (!connectTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    _tcpClient.Dispose();
                    _tcpClient = null;
                    return false;
                }

                if (!_tcpClient.Connected)
                {
                    _tcpClient.Dispose();
                    _tcpClient = null;
                    return false;
                }

                _networkStream = _tcpClient.GetStream();
                IsConnected = true;

                _tokenSource = new CancellationTokenSource();
                var token = _tokenSource.Token;

                // Start the send loop (same pattern as Serial)
                Task.Factory.StartNew(() => SendLoop(token), TaskCreationOptions.LongRunning);

                // Start the receive loop (TCP doesn't have DataReceived events like SerialPort)
                Task.Factory.StartNew(() => ReceiveLoop(token), TaskCreationOptions.LongRunning);

                return true;
            }
            catch (Exception)
            {
                _tcpClient?.Dispose();
                _tcpClient = null;
                IsConnected = false;
                return false;
            }
        }

        public void EnterRawMode() => throw new NotSupportedException("YModem raw mode is not supported over TCP. Use FTP instead.");
        public void ExitRawMode() => throw new NotSupportedException("YModem raw mode is not supported over TCP. Use FTP instead.");
        public void WriteBytes(byte[] data, int offset, int count) => throw new NotSupportedException("YModem raw mode is not supported over TCP. Use FTP instead.");

        public void Close()
        {
            _userClosed = true;
            _tokenSource?.Cancel();
            Thread.Sleep(100);

            IsConnected = false;

            try
            {
                _networkStream?.Close();
                _tcpClient?.Close();
                _tcpClient?.Dispose();
            }
            catch (Exception)
            {
                // Ignore disposal exceptions
            }

            _networkStream = null;
            _tcpClient = null;
        }

        public void WriteByte(byte data)
        {
            var ca = new byte[1];
            ca[0] = data;
            SendQue(ca);
        }

        public void WriteCommand(string command)
        {
            if (!IsConnected || _networkStream == null) return;

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
                        if (IsConnected && _networkStream != null && _networkStream.CanWrite)
                        {
                            _networkStream.Write(command, 0, command.Length);
                            _networkStream.Flush();
                        }
                    }
                    catch (Exception)
                    {
                        IsConnected = false;
                        TriggerReconnect();
                        return;
                    }

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

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_networkStream == null || !_networkStream.CanRead || !IsConnected)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    // Check if data is available before blocking on Read
                    if (!_networkStream.DataAvailable)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    int bytesRead = _networkStream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        // Connection closed by remote host
                        IsConnected = false;
                        TriggerReconnect();
                        return;
                    }

                    string received = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    ProcessReceivedData(received);
                }
                catch (Exception)
                {
                    if (!token.IsCancellationRequested)
                    {
                        IsConnected = false;
                        TriggerReconnect();
                    }
                    return;
                }
            }
        }

        private void TriggerReconnect()
        {
            if (_userClosed) return;
            if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) == 0)
                Task.Run(ReconnectLoopAsync);
        }

        private async Task ReconnectLoopAsync()
        {
            while (!_userClosed)
            {
                await Task.Delay(3000);
                if (_userClosed) break;
                if (TryConnect(_tcpSettings))
                    break;
            }
            Interlocked.Exchange(ref _reconnecting, 0);
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

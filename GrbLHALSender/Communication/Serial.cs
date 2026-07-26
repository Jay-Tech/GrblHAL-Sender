using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GrbLHALSender.Communication
{
    public class Serial : ObservableObject, ICommsAdapter
    {

        public event EventHandler<string>? OnDataReceived;
        public event EventHandler<byte[]>? OnRawDataReceived;

        private SerialSettings _serialSettings;
        private SerialPort _serialPort;
        private ConcurrentQueue<byte[]> _sendQue = new();
        private readonly AutoResetEvent _sendEvent = new(false);
        private char[] Split = new[]
        {
            '\r',
            '\n'
        };

        private static readonly object _sncLock = new();
        private CancellationTokenSource _tokenSource;
        private string _receiveBuffer = string.Empty;
        private volatile bool _rawMode = false;

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

        public Serial(SerialSettings serialSettings)
        {
            _serialSettings = serialSettings;
            TryConnect(serialSettings);
        }

        public bool TryConnect(SerialSettings serialSettings)
        {
            _serialSettings = serialSettings;
            _userClosed = false;
            Interlocked.Exchange(ref _reconnecting, 0);

            // Clean up any previous port before reconnecting
            if (_serialPort != null)
            {
                try
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    _serialPort.ErrorReceived -= SerialPort_ErrorReceived;
                    _serialPort.DtrEnable = false;
                    if (_serialPort.IsOpen) { _serialPort.DiscardInBuffer(); _serialPort.DiscardOutBuffer(); _serialPort.Close(); }
                    _serialPort.Dispose();
                }
                catch { }
                _serialPort = null;
            }
            if (_tokenSource != null)
            {
                _tokenSource.Cancel();
                Thread.Sleep(50);
                _tokenSource.Dispose();
                _tokenSource = null;
            }

            _serialPort = new SerialPort
            {
                BaudRate = _serialSettings.BaudRate,
                DataBits = _serialSettings.DataBits,
                Handshake = Handshake.None,
                Parity = _serialSettings.Parity,
                PortName = _serialSettings.PortName,
                ReadBufferSize = _serialSettings.ReadBufferSize,
                ReadTimeout = _serialSettings.ReadTimeOut,
                ReceivedBytesThreshold = _serialSettings.ReceivedBytesThreshold,
                StopBits = _serialSettings.StopBits,
                WriteBufferSize = _serialSettings.WriteBufferSize,
            };
            try
            {
                _serialPort.Open();
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.ErrorReceived += SerialPort_ErrorReceived;
                _serialPort.DtrEnable = true;
                if (_serialPort.IsOpen)
                {
                    IsConnected = true;
                    _tokenSource = new CancellationTokenSource();
                    Task.Factory.StartNew(() => SendLoop(_tokenSource.Token), TaskCreationOptions.LongRunning);
                }
                return _serialPort.IsOpen;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void EnterRawMode()
        {
            _rawMode = true;
            if (_serialPort != null)
            {
                _serialPort.DataReceived -= SerialPort_DataReceived;
                _serialPort.DiscardInBuffer();
                _receiveBuffer = string.Empty;
                _serialPort.DataReceived += SerialPort_RawDataReceived;
            }
        }

        public void ExitRawMode()
        {
            _rawMode = false;
            if (_serialPort != null)
            {
                _serialPort.DataReceived -= SerialPort_RawDataReceived;
                _serialPort.DiscardInBuffer();
                _receiveBuffer = string.Empty;
                _serialPort.DataReceived += SerialPort_DataReceived;
            }
        }

        public void WriteBytes(byte[] data, int offset, int count)
        {
            // Snapshot the field: Close() nulls it, and it can do so between the
            // open check and the write.
            var port = _serialPort;
            if (port == null || !port.IsOpen) return;
            port.BaseStream.Write(data, offset, count);
            port.BaseStream.Flush();
        }

        private void SerialPort_RawDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            var port = _serialPort;
            if (port == null) return;
            int bytesToRead = port.BytesToRead;
            if (bytesToRead <= 0) return;
            var buffer = new byte[bytesToRead];
            int read = port.Read(buffer, 0, bytesToRead);
            if (read > 0)
            {
                var data = new byte[read];
                Array.Copy(buffer, data, read);
                OnRawDataReceived?.Invoke(this, data);
            }
        }

        public void Close()
        {
            // Set first: it stops TriggerReconnect from starting a new reconnect loop
            // while we are tearing this adapter down.
            _userClosed = true;
            if (_serialPort != null)
            {
                _serialPort.DataReceived -= SerialPort_DataReceived;
                _serialPort.DataReceived -= SerialPort_RawDataReceived;
                _serialPort.ErrorReceived -= SerialPort_ErrorReceived;
            }
            _tokenSource?.Cancel();
            Thread.Sleep(100);

            // Report the drop before releasing the port. Close() used to leave
            // IsConnected true, which left MainViewModel.Connected true — and its
            // "if (Connected) return" guard then silently refused to reconnect.
            IsConnected = false;

            var port = _serialPort;
            _serialPort = null;
            if (port == null) return;

            try
            {
                if (port.IsOpen)
                {
                    port.DtrEnable = false;
                    port.RtsEnable = false;
                    port.DiscardInBuffer();
                    port.DiscardOutBuffer();
                }
                // Always dispose, open or not — a port left undisposed keeps the OS
                // handle, and the next open of the same port fails with access denied.
                port.Dispose();
            }
            catch (Exception)
            {
                // A port whose device has already vanished throws on every one of the
                // calls above; the handle is gone either way and the caller is mid-teardown.
            }
        }

        public void WriteByte(byte data)
        {
            var ca = new byte[1];
            ca[0] = data;
            SendQue(ca);
        }
        public void WriteCommand(string command)
        {
            var port = _serialPort;
            if (port == null || !port.IsOpen) return;
            if (command.Length == 1)
                WriteByte((byte)command.ToCharArray()[0]);
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
            _sendEvent.Set(); // Wake the send loop immediately
        }

        private void SendLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // Drain all queued commands as fast as possible
                while (_sendQue.TryDequeue(out var command))
                {
                    try
                    {
                        var port = _serialPort;
                        if (port != null && port.IsOpen)
                            port.BaseStream.Write(command, 0, command.Length);
                        else
                        {
                            IsConnected = false;
                            TriggerReconnect();
                        }
                    }
                    catch (Exception)
                    {
                        IsConnected = false;
                        TriggerReconnect();
                        return;
                    }
                }

                // Block until new data is queued (or check periodically)
                _sendEvent.WaitOne(50);
            }
        }
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            lock (_sncLock)
            {
                var port = _serialPort;
                if (port == null) return;
                _receiveBuffer += port.ReadExisting();

                while (true)
                {
                    var indexSlice = _receiveBuffer.IndexOfAny(Split);
                    if (indexSlice < 0) break; // Keep partial data in buffer for next event

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

        private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            IsConnected = false;
            TriggerReconnect();
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
                if (TryConnect(_serialSettings))
                    break;
            }
            Interlocked.Exchange(ref _reconnecting, 0);
        }
    }
}
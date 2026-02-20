using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GrbLHALSender.Communication
{
    public class Serial : ICommsAdapter
    {

        public event EventHandler<string>? OnDataReceived;
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
        public bool IsConnected { get; set; }
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

        public void Close()
        {
            _userClosed = true;
            if (_serialPort != null)
            {
                _serialPort.DataReceived -= SerialPort_DataReceived;
                _serialPort.ErrorReceived -= SerialPort_ErrorReceived;
            }
            _tokenSource?.Cancel();
            Thread.Sleep(100);
            if (_serialPort == null) return;
            _serialPort.DtrEnable = false;
            _serialPort.RtsEnable = false;
            if (!_serialPort.IsOpen) return;
            _serialPort?.DiscardInBuffer();
            _serialPort?.DiscardOutBuffer();
            _serialPort?.Dispose();
        }

        public void WriteByte(byte data)
        {
            var ca = new byte[1];
            ca[0] = data;
            SendQue(ca);
        }
        public void WriteCommand(string command)
        {
            if (!_serialPort.IsOpen) return;
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
                        if (_serialPort.IsOpen)
                            _serialPort.BaseStream.Write(command, 0, command.Length);
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
                _receiveBuffer += _serialPort.ReadExisting();

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
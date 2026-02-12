
using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace GrbLHAL_Sender.Communication
{
    public class Serial : ICommsAdapter
    {

        public event EventHandler<string>? OnDataReceived;
        private SerialSettings _serialSettings;
        private SerialPort _serialPort;
        private ConcurrentQueue<byte[]> _sendQue = new();
        private CancellationToken _token;
        private char[] Split = new[]
        {
            '\r',
            '\n'
        };

        private static readonly object _sncLock = new();
        private CancellationTokenSource _tokenSource;
        private string _receiveBuffer = string.Empty;
        public bool IsConnected { get; set; }
        public Serial(SerialSettings serialSettings)
        {
            _serialSettings = serialSettings;
            TryConnect(serialSettings);
        }

        public bool TryConnect(SerialSettings serialSettings)
        {
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
                _serialPort.DtrEnable = true;
                if (_serialPort.IsOpen)
                {
                    IsConnected = true;
                    _tokenSource = new CancellationTokenSource();
                    Task.Factory.StartNew(() => SendLoop(_tokenSource.Token), TaskCreationOptions.LongRunning);
                }
                return _serialPort.IsOpen;
            }
            catch (Exception e)
            {
                return false;
            }
        }

        public void Close()
        {
            _serialPort.DataReceived -= SerialPort_DataReceived;
            _tokenSource?.Cancel();
             Thread.Sleep(100);
            _serialPort.DtrEnable = false;
            _serialPort.RtsEnable = false;
            if(!_serialPort.IsOpen) return;
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
        }

        private void SendLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (_sendQue.TryDequeue(out var command))
                {
                    if (_serialPort.IsOpen)
                        _serialPort.BaseStream.Write(command, 0, command.Length);
                    Thread.Sleep(10);
                }
                else
                {
                    Thread.Sleep(1);
                }
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
    }
}
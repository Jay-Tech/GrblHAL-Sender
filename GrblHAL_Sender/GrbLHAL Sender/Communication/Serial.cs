using Avalonia.Controls.Documents;
using Avalonia.Threading;
using DynamicData.Kernel;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Animation;

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
        public bool IsConnected { get; set; }
        public Serial(string connection, SerialSettings serialSettings = null!)
        {
            TryConnect(connection, serialSettings);
        }

        private bool TryConnect(string portName, SerialSettings serialSettings = null)
        {
            if (serialSettings == null)
            {
                _serialSettings = new SerialSettings(portName);
            }

            return TryConnect(_serialSettings);
        }
        public bool TryConnect(SerialSettings settings)
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
            _tokenSource.Cancel();
             Thread.Sleep(100);
            _serialPort.DtrEnable = false;
            _serialPort.RtsEnable = false;
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
            while (true)
            {
                if (token.IsCancellationRequested)
                    break;
                if (_sendQue.IsEmpty) continue;
                _sendQue.TryDequeue(out var command);
                if (command == null) return;
                if (_serialPort.IsOpen)
                    _serialPort.BaseStream.Write(command, 0, command.Length);

                Thread.Sleep(10);
            }
        }
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            lock (_sncLock)
            {
                var inputStream = _serialPort.ReadExisting().AsSpan();
                while (inputStream.Length > 0)
                {
                    var indexSlice = inputStream.IndexOfAny('\r', '\n');
                    if (indexSlice < 0) return;
                    var data = inputStream[..indexSlice];
                    if (data.Length != 0)
                    {
                        OnDataReceived?.Invoke(this, data.ToString() ?? string.Empty);
                    }
                   
                    inputStream = inputStream.Slice(indexSlice).Trim(Split);
                }
            }
        }
    }
}
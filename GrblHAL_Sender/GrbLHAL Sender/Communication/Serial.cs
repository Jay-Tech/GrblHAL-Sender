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
using Avalonia.Animation;

namespace GrbLHAL_Sender.Communication
{
    public class Serial : ICommsAdapter
    {

        public event EventHandler<string>? OnDataReceived;
        private SerialSettings _serialSettings;
        private SerialPort _serialPort;
        private ConcurrentQueue<byte[]> _sendQue = new();
        private char[] Split = new[]
        {
            '\r',
            '\n'
        };

        private static readonly object _sncLock = new();
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
            _serialPort.DtrEnable = false;
            _serialPort.RtsEnable = false;
            _serialPort?.DiscardInBuffer();
            _serialPort?.DiscardOutBuffer();
            _serialPort?.Dispose();
        }

        public void WriteByte(byte data)
        {
            if (_serialPort.IsOpen)
                _serialPort.BaseStream.Write([data], 0, 1);
        }

        public void WriteBytes(byte[] bytes, int len)
        {
            _serialPort.BaseStream.WriteAsync(bytes, 0, len);
        }

        public void WriteString(string data)
        {
            var bytes = Encoding.Default.GetBytes(data);
            WriteBytes(bytes, bytes.Length);
        }

        public void WriteCommand(string command)
        {
            //lock (_sncLock)
            //{
            if (!_serialPort.IsOpen) return;
            if (command.Length == 1)
                WriteByte((byte)command.ToCharArray()[0]);
            else
            {
                command += "\r";
                byte[] bytes = Encoding.UTF8.GetBytes(command);
                if (_serialPort.IsOpen)
                    _serialPort.BaseStream.Write(bytes, 0, bytes.Length);
            }
            //}
        }

        private void SendQue(byte[] command)
        {
            _sendQue.Enqueue(command);
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
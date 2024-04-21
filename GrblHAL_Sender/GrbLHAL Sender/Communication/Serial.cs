using Avalonia.Controls.Documents;
using Avalonia.Threading;
using DynamicData.Kernel;
using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace GrbLHAL_Sender.Communication
{
    public class Serial : ICommsAdapter
    {
       
        public event EventHandler<string>? OnDataReceived;
        private  SerialSettings _serialSettings;
        private SerialPort _serialPort;
      

        public Serial(SerialSettings serialSettings, SerialPort serialPort)
        {
            _serialSettings = serialSettings;
            this._serialPort = serialPort;
        }
        public Serial(string connection)
        {
             TryConnect(connection);
        }

        private bool TryConnect(string portName)
        {
            _serialSettings = new SerialSettings(portName);
           return TryConnect(_serialSettings);
        }
        public bool TryConnect(SerialSettings settings)
        {
            _serialSettings = settings;
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
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();
            _serialPort.Dispose();
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
        }

        private  char[] Split = new []
        {
            '\r',
            '\n'
        };
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
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
                //Debug.WriteLine( data.ToString() );
                //Debug.WriteLine("************************"+ inputStream.Length +"********************");
            }
        }

        //private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        //{
        //    var inputStream = _serialPort.ReadExisting();
        //    while (inputStream.Length >0)
        //    {
        //        var index= inputStream.IndexOfAny(Split);
        //        if(index == -1)continue;
        //        var data = inputStream.Substring(0, index);
        //        inputStream = inputStream.Remove(0,index + 1);

        //        if (data.Length != 0)
        //        {
        //            OnDataReceived?.Invoke(this,  data.ToString() ?? string.Empty);
        //        }
        //    }
        //}

    }
}
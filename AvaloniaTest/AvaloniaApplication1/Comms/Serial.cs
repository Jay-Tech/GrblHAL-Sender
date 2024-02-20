using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using Avalonia.Threading;
using AvaloniaApplication1.ViewModels;
using ReactiveUI;
using System.Collections.ObjectModel;

namespace AvaloniaApplication1.Comms
{
    public class SerialStream : StreamComms
    {
        private SerialPort serialPort = null;
        private byte[] buffer = new byte[Comms.RXBUFFERSIZE];
        private StringBuilder input = new StringBuilder(Comms.RXBUFFERSIZE);
        private volatile Comms.State state = Comms.State.ACK;
        private Dispatcher Dispatcher { get; set; }

        public event DataReceivedHandler DataReceived;

#if RESPONSELOG
        StreamWriter log = null;
#endif
        public SerialStream(string PortParams, int ResetDelay, Dispatcher dispatcher)
        {
            Comms.com = this;
            Dispatcher = dispatcher;
            Reply = string.Empty;

            if (PortParams.IndexOf(":") < 0)
                PortParams += ":115200,N,8,1";

            string[] parameter = PortParams.Substring(PortParams.IndexOf(":") + 1).Split(',');

            if (parameter.Count() < 4)
            {

                Environment.Exit(2);
            }

            serialPort = new SerialPort();
            serialPort.PortName = PortParams.Substring(0, PortParams.IndexOf(":"));
            serialPort.BaudRate = int.Parse(parameter[0]);
            serialPort.Parity = ParseParity(parameter[1]);
            serialPort.DataBits = int.Parse(parameter[2]);
            serialPort.StopBits = int.Parse(parameter[3]) == 1 ? StopBits.One : StopBits.Two;
            serialPort.ReceivedBytesThreshold = 1;
            serialPort.ReadTimeout = 50;
            serialPort.ReadBufferSize = Comms.RXBUFFERSIZE;
            serialPort.WriteBufferSize = Comms.TXBUFFERSIZE;

            if (parameter.Count() > 4) switch (parameter[4])
                {
                    case "P": // Cannot be used With ESP32!
                        serialPort.Handshake = Handshake.RequestToSend;
                        break;

                    case "X":
                        serialPort.Handshake = Handshake.XOnXOff;
                        break;
                }

            try
            {
                serialPort.Open();
            }
            catch
            {
            }

            if (serialPort.IsOpen)
            {
                serialPort.DtrEnable = true;

                Comms.ResetMode ResetMode = Comms.ResetMode.None;

                PurgeQueue();
                serialPort.DataReceived += new SerialDataReceivedEventHandler(SerialPort_DataReceived);

                if (parameter.Count() > 5)
                    Enum.TryParse(parameter[5], true, out ResetMode);

                switch (ResetMode)
                {
                    case Comms.ResetMode.RTS:
                        /* For resetting ESP32 */
                        serialPort.RtsEnable = true;
                        System.Threading.Thread.Sleep(5);
                        serialPort.RtsEnable = false;
                        if (ResetDelay > 0)
                            System.Threading.Thread.Sleep(ResetDelay);
                        break;

                    case Comms.ResetMode.DTR:
                        /* For resetting Arduino */
                        serialPort.DtrEnable = false;
                        System.Threading.Thread.Sleep(5);
                        serialPort.DtrEnable = true;
                        if (ResetDelay > 0)
                            System.Threading.Thread.Sleep(ResetDelay);
                        break;
                }

#if RESPONSELOG
                if (Resources.DebugFile != string.Empty) try
                    {
                        log = new StreamWriter(Resources.DebugFile);
                    }
                    catch
                    {
                        MessageBox.Show("Unable to open log file: " + Resources.DebugFile, "ioSender");
                    }
#endif
            }
        }

        ~SerialStream()
        {
#if RESPONSELOG
            if (log != null) try
                {
                    log.Close();
                    log = null;
                }
                catch { }
#endif
            if (!IsClosing && IsOpen)
                Close();
        }

        public Comms.StreamType StreamType { get { return Comms.StreamType.Serial; } }
        public Comms.State CommandState { get { return state; } set { state = value; } }
        public string Reply { get; private set; }
        public bool IsOpen { get { return serialPort != null && serialPort.IsOpen; } }
        public bool IsClosing { get; private set; }
        public int OutCount { get { return serialPort.BytesToWrite; } }
        public bool EventMode { get; set; } = true;
        public Action<int> ByteReceived { get; set; }

        public void PurgeQueue()
        {
            if (serialPort != null)
            {
                if (serialPort.IsOpen)
                {
                    serialPort.DiscardInBuffer();
                    serialPort.DiscardOutBuffer();
                }
            }
            Reply = string.Empty;
            if (!EventMode)
                input.Clear();
        }

        private Parity ParseParity(string parity)
        {
            Parity res = Parity.None;

            switch (parity)
            {
                case "E":
                    res = Parity.Even;
                    break;

                case "O":
                    res = Parity.Odd;
                    break;

                case "M":
                    res = Parity.Mark;
                    break;

                case "S":
                    res = Parity.Space;
                    break;
            }

            return res;
        }

        public void Close()
        {
            if (!IsClosing && IsOpen)
            {
                IsClosing = true;
                try
                {
                    serialPort.DataReceived -= SerialPort_DataReceived;
                    serialPort.DtrEnable = false;
                    serialPort.RtsEnable = false;
                    serialPort.DiscardInBuffer();
                    serialPort.DiscardOutBuffer();
                    System.Threading.Thread.Sleep(100);
                    serialPort.Close();
                    serialPort = null;
                }
                catch { }
                IsClosing = false;
            }
        }

        public int ReadByte()
        {
            int c = input.Length == 0 ? -1 : input[0];

            if (c != -1)
                input.Remove(0, 1);

            return c;
        }

        public void WriteByte(byte data)
        {
            if (serialPort != null && serialPort.IsOpen)

                serialPort.BaseStream.Write(new byte[1] { data }, 0, 1);
        }

        public void WriteBytes(byte[] bytes, int len)
        {
            serialPort.BaseStream.WriteAsync(bytes, 0, len);
        }

        public void WriteString(string data)
        {
            byte[] bytes = Encoding.Default.GetBytes(data);
            WriteBytes(bytes, bytes.Length);
        }

        public void WriteCommand(string command)
        {
            state = Comms.State.AwaitAck;
            if (serialPort == null) return;
            if (command.Length == 1 && command != GrblConstants.CMD_PROGRAM_DEMARCATION)
                WriteByte((byte)command.ToCharArray()[0]);
            else
            {
                command += "\r";
                byte[] bytes = Encoding.UTF8.GetBytes(command);
                if (serialPort.IsOpen)
                    serialPort.BaseStream.Write(bytes, 0, bytes.Length);
            }
        }

        public void AwaitAck()
        {
            // while (Comms.com.CommandState == Comms.State.DataReceived || Comms.com.CommandState == Comms.State.AwaitAck)
            //EventUtils.DoEvents();
        }

        public void AwaitAck(string command)
        {
            PurgeQueue();
            Reply = string.Empty;
            WriteCommand(command);

            //while (Comms.com.CommandState == Comms.State.DataReceived || Comms.com.CommandState == Comms.State.AwaitAck)
            // EventUtils.DoEvents();
        }

        public void AwaitResponse()
        {
            // while (Comms.com.CommandState == Comms.State.AwaitAck)
            // EventUtils.DoEvents();
        }

        public void AwaitResponse(string command)
        {
            PurgeQueue();
            Reply = string.Empty;
            WriteCommand(command);

            while (Comms.com.CommandState == Comms.State.AwaitAck)
                System.Threading.Thread.Sleep(15);
        }

        public string GetReply(string command)
        {
            Reply = string.Empty;
            WriteCommand(command);

            AwaitResponse();

            return Reply;
        }

        private int gp()
        {
            int pos = 0; bool found = false;

            while (!found && pos < input.Length)
                found = input[pos++] == '\n';

            return found ? pos - 1 : 0;
        }


        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            int pos = 0;

            lock (input)
            {
                input.Append(serialPort.ReadExisting());

                if (EventMode)
                {
                    while (input.Length > 0 && (pos = gp()) > 0)
                    {
                        Reply = pos == 0 ? string.Empty : input.ToString(0, pos - 1);
                        input.Remove(0, pos + 1);
#if RESPONSELOG
                        if (log != null)
                        {
                            log.WriteLine(Reply);
                            log.Flush();
                        }
#endif
                        if (Reply.Length != 0 && DataReceived != null)
                        {
                            DataReceived?.Invoke(Reply);
                        }
                        // Dispatcher.BeginInvoke(DataReceived, Reply);
                        //                            Dispatcher.Invoke(addEdge, Reply);

                        state = Reply == "ok" ? Comms.State.ACK : Reply.StartsWith("error") ? Comms.State.NAK : Comms.State.DataReceived;
                    }
                }
                else
                    ByteReceived?.Invoke(ReadByte());

            }
        }
    }

    public class ConnectMode : ViewModelBase
    {
        public ConnectMode(Comms.ResetMode mode, string name)
        {
            Mode = mode;
            Name = name;
        }

        public Comms.ResetMode Mode { get; private set; }

        public string Name { get; private set; }
    }

    public class ComPort
    {
        public ComPort()
        {
        }

        public ComPort(string name)
        {
            Name = FullName = name;
        }

        public string Name { get; set; }
        public string FullName { get; set; }
    }

    public class SerialPorts : ViewModelBase
    {
        string _selected = string.Empty;
        string _baud = "115200";
        private ConnectMode _mode = null;

        public SerialPorts()
        {
            Refresh();

            if (Ports.Count > 0)
                _selected = Ports[0].Name;

            Baud.Add(_baud);
            Baud.Add("230400");
            Baud.Add("460800");
            Baud.Add("921600");

            ConnectModes.Add(new ConnectMode(Comms.ResetMode.None, "No action"));
            ConnectModes.Add(new ConnectMode(Comms.ResetMode.DTR, "Toggle DTR"));
            ConnectModes.Add(new ConnectMode(Comms.ResetMode.RTS, "Toggle RTS"));

            SelectedMode = ConnectModes[0];
        }

        public void Refresh()
        {
            var _portnames = SerialPort.GetPortNames();

            Ports.Clear();

            if (_portnames.Length > 0)
            {
                Array.Sort(_portnames);

                if (_portnames.Contains("COM1"))
                {
                    var pn = _portnames.ToList();
                    pn.Remove("COM1");
                    _portnames = pn.ToArray();
                }

                //using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption like '%(COM%'")) try
                //    {
                //        var ports = searcher.Get().Cast<ManagementBaseObject>().ToList().Select(p => p["Caption"].ToString());
                //var portList = _portnames.Select(n => ports.FirstOrDefault(s => s.Contains('(' + n + ')'))).ToList();
                foreach (var fullname in _portnames)
                {
                    var name = fullname.Substring(fullname.IndexOf("(COM") + 1).Trim().TrimEnd(')');
                    var port = new ComPort(name);

                    port.FullName = name + " - " + fullname.Replace('(' + name + ')', string.Empty).Trim();

                    Ports.Add(port);
                }
                // }


                if (Ports.Count != _portnames.Length)
                {
                    foreach (var port in _portnames)
                    {
                        if (port.StartsWith("COM") && Ports.Where(n => n.Name == port).FirstOrDefault() == null)
                            Ports.Add(new ComPort(port));
                    }
                }

                if (Ports.Count > 0)
                    SelectedPort = Ports[0].Name;
            }
        }

        public ObservableCollection<ComPort> Ports { get; private set; } = new ObservableCollection<ComPort>();
        public ObservableCollection<ConnectMode> ConnectModes { get; private set; } = new ObservableCollection<ConnectMode>();
        public ObservableCollection<string> Baud { get; private set; } = new ObservableCollection<string>();

        public string SelectedPort
        {
            get { return _selected; }
            set
            {
                if (_selected != value)
                {
                    _selected = value;
                    // OnPropertyChanged();
                }
            }
        }

        public string SelectedBaud
        {
            get { return _baud; }
            set
            {
                if (_baud != value)
                {
                    _baud = value;
                    //OnPropertyChanged();
                }
            }
        }

        public ConnectMode SelectedMode
        {
            get { return _mode; }
            set
            {
                if (_mode != value)
                {
                    _mode = value;
                    //OnPropertyChanged();
                }
            }
        }
    }
}

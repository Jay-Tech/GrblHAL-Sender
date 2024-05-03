using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using GrbLHAL_Sender.Communication;
using GrbLHAL_Sender.Configuration;
using GrbLHAL_Sender.Probe;
using GrbLHAL_Sender.Utility;
using Microsoft.CodeAnalysis.Operations;
using ReactiveUI;

namespace GrbLHAL_Sender.ViewModels
{
    public class ProbeViewModel : ViewModelBase
    {
        private readonly CommunicationManager _communicationManager;
        private readonly ConfigManager _configManager;
        private ReactiveCommand<object, Unit> _doubleTapProbeControlCommand;
        private bool _showProbeControl;
        private double _probeDiameter =2;
        private double _rapidRate =0;
        private double _searchRate =100;
        private double _latchRate = 20;
        private double _probeDistance = 10;
        private double _latchDistance =1;
        private int _index = 0;
        private ProbeJobBuilder _probeJob;
        private double _tlo;
        private bool _tlr;

        public bool TLR
        {
            get => _tlr;
            set
            {
                if (_tlr != value)
                {
                    _tlr = value;
                }
            }
        }

        public ICommand OpenProbePanel { get; }
        public ICommand ProbeCommand { get; }
        public bool ShowProbeControl
        {
            get => _showProbeControl;
            set => this.RaiseAndSetIfChanged(ref _showProbeControl, value);
        }

        public List<string> ProbeJob { get; set; }
        public ReactiveCommand<object, Unit> DoubleTapProbeControlCommand
        {
            get => _doubleTapProbeControlCommand;
            set => _doubleTapProbeControlCommand = value;
        }

        public double ProbeDiameter 
        {
            get => _probeDiameter;
            set => this.RaiseAndSetIfChanged(ref _probeDiameter, value);
        }

        public double RapidRate
        {
            get => _rapidRate;
            set => this.RaiseAndSetIfChanged(ref _rapidRate, value);
        }

        public double SearchRate
        {
            get => _searchRate;
            set => this.RaiseAndSetIfChanged(ref _searchRate, value);
        }

        public double LatchRate
        {
            get => _latchRate;
            set => this.RaiseAndSetIfChanged(ref _latchRate, value);
        }

        public double ProbeDistance
        {
            get => _probeDistance;
            set => this.RaiseAndSetIfChanged(ref _probeDistance, value);
        }

        public double LatchDistance
        {
            get => _latchDistance;
            set => this.RaiseAndSetIfChanged(ref _latchDistance, value);
        }
        public double TLO
        {
            get => _tlo;
            set => _tlo = value;
        }

        public ProbeType ProbeType { get; set; }
        public ICommand ProbeZCommand { get; }

        public ProbeViewModel(CommunicationManager communicationManager, ConfigManager configManager)
        {
            _communicationManager = communicationManager;
            _configManager = configManager;
            DoubleTapProbeControlCommand = ReactiveCommand.Create<object>(DoubleTap);
            OpenProbePanel = ReactiveCommand.Create(OpenProbe);
            ProbeCommand = ReactiveCommand.Create(StartProbe);
            ProbeZCommand = ReactiveCommand.Create(ProbeZ);
        }

        private void ProbeZ()
        {
            ProbeType = ProbeType.ProbeZ;
        }

        private void StartProbe()
        {


            ProbeJob = [];
             _probeJob = new ProbeJobBuilder();
            
            ProbeJob = _probeJob.ProbeZ(ProbeDiameter.ToString(), RapidRate.ToString(),
                ProbeDistance.ToString(), SearchRate.ToString(), LatchRate.ToString(), LatchDistance.ToString());
            _communicationManager.StartJob(StartProbeJob);
            StartProbeJob("start");
        }

        private void StartProbeJob(string obj)
        {
            ListenToState(true);
            _communicationManager.StartJob(SendJobLoop);
            SendJobLoop("start");
        }
        public void SendJobLoop(string lineProcessed)
        {
            if (_index <= ProbeJob.Count - 1)
            {
                _communicationManager.SendCommand(ProbeJob[_index]);
                _index++;
            }
            else
            {
                JobCompete();
            }
        }
        
        private void JobCompete()
        {
            _communicationManager.EndJob();
            _index = 0;
            ListenToState(false);
            ProProbeOffsets();
        }

        private void ProProbeOffsets()
        {
            if (!TLR ||  double.IsNaN(TLO))
            {
                TLO = _probeJob.ProbeState.ZOffset.StringToDouble();
                _communicationManager.SendCommand("G49");
                _communicationManager.SendCommand("$TLR");
                TLR = _probeJob.ProbeState.ProbeSuccessful;
            }
            else
            {
                var tlo =  _probeJob.ProbeState.ZOffset.StringToDouble();
                tlo -= TLO;
                _communicationManager.SendCommand($"G43.1Z{tlo}");
            }
           
        }

        private void ListenToState(bool b)
        {
            if (b)
                _communicationManager.OnProbeResults += _communicationManager_OnProbeResults;
            else
            {
                _communicationManager.OnProbeResults -= _communicationManager_OnProbeResults;
            }
        }

        private void _communicationManager_OnProbeResults(object? sender, Probe.ProbeState e)
        {
            _probeJob.ProbeState = e;
        }

        private void _commsManager_OnStateReceived(object? sender, RealTImeState e)
        {
            //var state = e.GrblHalState;
            //ProbeJob = state switch
            //{
            //    "Hold" => JobState.Pause,
            //    "Tool" => JobState.Tool,
            //    "Running" => JobState.Running,
            //    "Alarm" => JobState.Alarm,
            //    "Stop" => JobState.Stop,
            //    _ => JobState
            //};
        }
        private void OpenProbe()
        {
            ShowProbeControl =! ShowProbeControl;
        }

        private void DoubleTap(object p)
        {
            ShowProbeControl = !Convert.ToBoolean(p);
        }

    }
    public enum ProbeType
    {
        ProbeZ,
        InsideCenter,
        OutSideCenter,
        InsideCorner,
        OutSideCorner,

    }

}

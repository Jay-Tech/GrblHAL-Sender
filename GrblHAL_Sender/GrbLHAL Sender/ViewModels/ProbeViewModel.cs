using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using GrbLHAL_Sender.Communication;
using GrbLHAL_Sender.Configuration;
using ReactiveUI;

namespace GrbLHAL_Sender.ViewModels
{
    public class ProbeViewModel : ViewModelBase
    {
        private readonly CommunicationManager _communicationManager;
        private readonly ConfigManager _configManager;
        private ReactiveCommand<object, Unit> _doubleTapProbeControlCommand;
        private bool _showProbeControl;

        public ICommand OpenProbePanel { get; }
        public bool ShowProbeControl
        {
            get => _showProbeControl;
            set => this.RaiseAndSetIfChanged(ref _showProbeControl, value);
        }
        public ReactiveCommand<object, Unit> DoubleTapProbeControlCommand
        {
            get => _doubleTapProbeControlCommand;
            set => _doubleTapProbeControlCommand = value;
        }
        public ProbeViewModel(CommunicationManager communicationManager, ConfigManager configManager)
        {
            _communicationManager = communicationManager;
            _configManager = configManager;
            DoubleTapProbeControlCommand = ReactiveCommand.Create<object>(DoubleTap);
            OpenProbePanel = ReactiveCommand.Create(OpenProbe);
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
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GrbLHAL_Sender.Communication;
using GrbLHAL_Sender.Configuration;

namespace GrbLHAL_Sender.ViewModels
{
    public class ProbeViewModel : ViewModelBase
    {
        private readonly CommunicationManager _communicationManager;
        private readonly ConfigManager _configManager;

        public ProbeViewModel(CommunicationManager communicationManager, ConfigManager configManager)
        {
            _communicationManager = communicationManager;
            _configManager = configManager;
        }
    }
}

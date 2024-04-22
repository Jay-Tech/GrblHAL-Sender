using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using GrbLHAL_Sender.Communication;
using GrbLHAL_Sender.Configuration;
using GrbLHAL_Sender.Settings;
using ReactiveUI;

namespace GrbLHAL_Sender.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly CommunicationManager _commManager;
        private readonly ConfigManager _configManager;
        private ObservableCollection<GrblHalSetting> _settingsCollection = new();
        public ICommand CommandSave { get; }

        public ObservableCollection<GrblHalSetting> SettingCollection
        {
            get => _settingsCollection;
            set => this.RaiseAndSetIfChanged(ref _settingsCollection, value);
        }
   
        public SettingsViewModel(CommunicationManager commManager, ConfigManager configManager)
        {
            _commManager = commManager;
            _configManager = configManager;
            _commManager.onSettingUpdated += _commManager_onSettingUpdated;
            CommandSave = ReactiveCommand.Create(SaveSettings);
        }

        private void SaveSettings()
        {
            var needSaving = SettingCollection.Where(x => x.NeedsSaving).ToList();
            var t = Task.Factory.StartNew(async () =>
            {
                foreach (var command in needSaving.Select(item => $"${item.Id}={item.SettingValue}"))
                {
                    _commManager.SendCommand(command);
                    await Task.Delay(200);
                }
            });
        }
        private void _commManager_onSettingUpdated(object? sender, List<GrblHalSetting> e)
        {
            SettingCollection = new ObservableCollection<GrblHalSetting>(e);
        }
    }
}

using Avalonia.Platform.Storage;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Settings;
using ReactiveUI;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels
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

        public ICommand ImportSettingsCommand { get; }
        public ICommand ExportSettingsCommand { get; }

        /// <summary>
        /// Interaction to open a file picker for importing settings.
        /// View registers a handler that returns the selected file.
        /// </summary>
        public Core.Interaction<string, IStorageFile?> ImportFileInteraction { get; } = new();

        /// <summary>
        /// Interaction to open a save file picker for exporting settings.
        /// View registers a handler that returns the target save file.
        /// </summary>
        public Core.Interaction<string, IStorageFile?> ExportFileInteraction { get; } = new();

        public SettingsViewModel(CommunicationManager commManager, ConfigManager configManager)
        {
            _commManager = commManager;
            _configManager = configManager;
            _commManager.onSettingUpdated += _commManager_onSettingUpdated;
            CommandSave = ReactiveCommand.Create(SaveSettings);
            ImportSettingsCommand = ReactiveCommand.CreateFromTask(ImportSettings);
            ExportSettingsCommand = ReactiveCommand.CreateFromTask(ExportSettings);
        }

        private async Task ExportSettings()
        {
            if (SettingCollection.Count == 0) return;

            var file = await ExportFileInteraction.HandleAsync("Export GrblHAL Settings");
            if (file == null) return;

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);

            foreach (var setting in SettingCollection.OrderBy(s => s.Id))
            {
                await writer.WriteLineAsync($"${setting.Id}={setting.SettingValue}");
            }
        }

        private async Task ImportSettings()
        {
            var file = await ImportFileInteraction.HandleAsync("Import GrblHAL Settings");
            if (file == null) return;

            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);

            var lines = new List<string>();
            while (await reader.ReadLineAsync() is { } line)
            {
                var trimmed = line.Trim();
                // Only process lines in $id=value format
                if (trimmed.StartsWith('$') && trimmed.Contains('='))
                {
                    lines.Add(trimmed);
                }
            }

            if (lines.Count == 0) return;

            // Send each setting to the controller
            await Task.Run(async () =>
            {
                foreach (var command in lines)
                {
                    _commManager.SendCommand(command);
                    await Task.Delay(200);
                }
            });
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

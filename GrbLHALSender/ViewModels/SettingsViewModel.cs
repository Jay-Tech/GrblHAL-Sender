using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Settings;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly CommunicationManager _commManager;
        private readonly ConfigManager _configManager;
        private ObservableCollection<GrblHalSetting> _settingsCollection = new();
        private ObservableCollection<SettingGroupViewModel> _groups = new();
        private string _filterText = string.Empty;

        // Descriptions cost one $SED= round trip each, so they are fetched per group
        // on first expand and cached here for the rest of the session.
        private readonly HashSet<string> _descriptionsLoaded = new();
        private bool _descriptionsUnsupported;

        // Responses are collected off the shared receive stream, so only one $SED=
        // may be outstanding at a time — concurrent sweeps interleave their replies
        // and land descriptions on the wrong settings.
        private readonly SemaphoreSlim _descriptionGate = new(1, 1);

        public ICommand CommandSave { get; }

        public ObservableCollection<GrblHalSetting> SettingCollection
        {
            get => _settingsCollection;
            set => this.RaiseAndSetIfChanged(ref _settingsCollection, value);
        }

        /// <summary>Collapsible sections built from the firmware's own group names.</summary>
        public ObservableCollection<SettingGroupViewModel> Groups
        {
            get => _groups;
            private set => this.RaiseAndSetIfChanged(ref _groups, value);
        }

        public string FilterText
        {
            get => _filterText;
            set => this.RaiseAndSetIfChanged(ref _filterText, value);
        }

        public ICommand ClearFilterCommand { get; }
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
            ClearFilterCommand = ReactiveCommand.Create(() => FilterText = string.Empty);

            // Same approach as CheatSheetViewModel: ~200 rows rebuild fast enough to do
            // it synchronously per keystroke, even on the Pi.
            this.WhenAnyValue(x => x.FilterText).Subscribe(_ => RebuildGroups());
        }

        /// <summary>
        /// Projects the flat setting list into collapsible sections, applying the current
        /// filter. While filtering, matching groups auto-expand and empty ones disappear,
        /// so a search lands directly on results instead of a wall of collapsed headers.
        /// </summary>
        private void RebuildGroups()
        {
            var query = FilterText?.Trim() ?? string.Empty;
            var filtering = query.Length > 0;

            var grouped = SettingCollection
                .Where(s => !filtering || Matches(s, query))
                .GroupBy(s => string.IsNullOrWhiteSpace(s.GroupName) ? $"Group {s.GroupId}" : s.GroupName)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new SettingGroupViewModel(g.Key, g.OrderBy(s => s.Id), OnGroupExpanded))
                .ToList();

            // Expand everything on a search; otherwise open only the first section so the
            // page starts as a browsable index rather than a 200-row scroll.
            for (var i = 0; i < grouped.Count; i++)
                grouped[i].IsExpanded = filtering || i == 0;

            Groups = new ObservableCollection<SettingGroupViewModel>(grouped);

            // The first section is open without anyone tapping it, so fetch its
            // descriptions here. Search does not: expanding twenty groups at once is
            // not a reason to put hundreds of commands on the wire.
            if (!filtering && grouped.Count > 0)
                _ = EnsureDescriptionsAsync(grouped[0]);
        }

        private static bool Matches(GrblHalSetting setting, string query)
        {
            static bool Has(string? value, string q) =>
                value != null && value.Contains(q, StringComparison.OrdinalIgnoreCase);

            return setting.Id.ToString().Contains(query, StringComparison.Ordinal)
                   || Has(setting.Name, query)
                   || Has(setting.Unit, query)
                   || Has(setting.GroupName, query)
                   || Has(setting.Description, query);
        }

        private void OnGroupExpanded(SettingGroupViewModel group)
        {
            // Accordion: one section open at a time. With ~20 groups of tall rows,
            // leaving several open pushes everything else far down the scroll and makes
            // headers hard to hit. Only user toggles land here — a filter deliberately
            // expands every match, since showing all results is the point of a search.
            foreach (var other in Groups)
            {
                if (!ReferenceEquals(other, group))
                    other.IsExpanded = false;
            }

            _ = EnsureDescriptionsAsync(group);
        }

        /// <summary>
        /// Fills in descriptions for one group. grblHAL serves these one setting at a
        /// time, so this is deliberately per-group and cached rather than a bulk fetch.
        /// </summary>
        private async Task EnsureDescriptionsAsync(SettingGroupViewModel group)
        {
            if (_descriptionsUnsupported) return;

            // Checked before marking the group loaded, so it is retried once the job
            // finishes. $SED queries are refused while streaming, and a refusal looks
            // exactly like "this firmware has no descriptions" — which would switch
            // descriptions off for the rest of the session.
            if (_commManager.IsStreaming) return;

            if (!_descriptionsLoaded.Add(group.Name)) return;

            await _descriptionGate.WaitAsync();
            try
            {
                // Collected first, applied in one batch below. Assigning each
                // description as it arrives grows that row immediately, so a group
                // would creep downward for several seconds after being opened and
                // every header below it would slide out from under the user's finger.
                var fetched = new List<(GrblHalSetting Setting, string Text)>();
                var first = true;

                foreach (var setting in group.Settings.ToList())
                {
                    if (_descriptionsUnsupported) return;
                    // A job can start while this group is still being fetched; stop
                    // asking rather than reading the refusals as missing descriptions.
                    if (_commManager.IsStreaming)
                    {
                        _descriptionsLoaded.Remove(group.Name);
                        break;
                    }
                    if (setting.Description != null) continue;

                    var text = await _commManager.GetSettingDescriptionAsync(setting.Id);

                    // A build without descriptions answers nothing for every setting.
                    // Detect that on the very first query and stop, rather than paying
                    // ~200 futile round trips one group at a time.
                    if (first && text == null)
                    {
                        _descriptionsUnsupported = true;
                        return;
                    }
                    first = false;

                    if (text != null)
                        fetched.Add((setting, text));
                }

                if (fetched.Count > 0)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var (setting, text) in fetched)
                            setting.Description = text;
                    });
                }
            }
            finally
            {
                _descriptionGate.Release();
            }
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
                foreach (var command in needSaving.Select(item => $"${item.Id}={item.SettingValue?.Trim()}"))
                {
                    _commManager.SendCommand(command);
                    await Task.Delay(200);
                }
            });
        }
        private void _commManager_onSettingUpdated(object? sender, List<GrblHalSetting> e)
        {
            SettingCollection = new ObservableCollection<GrblHalSetting>(e);

            // A reconnect can be against different firmware, so description support and
            // any cached text must not carry over.
            _descriptionsLoaded.Clear();
            _descriptionsUnsupported = false;
            RebuildGroups();
        }
    }
}

using GrbLHALSender.Configuration;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels;

/// <summary>
/// Editor for the user's G-code event rules (the "G-code Events" config tab).
/// Rules are edited on clones and only copied back into the config on
/// <see cref="Save"/>, so closing the dialog without saving discards the edits.
/// </summary>
public class GcodeEventViewModel : ViewModelBase, ISavableViewModel
{
    private readonly ConfigManager _configManager;
    private GcodeEventHook? _selectedPreset;

    /// <summary>Templates offered in the "add" dropdown.</summary>
    public ObservableCollection<GcodeEventHook> Presets { get; } = new(GcodeEventHook.Presets);

    /// <summary>The rules being edited, one row each in the config list.</summary>
    public ObservableCollection<GcodeEventHook> Items { get; } = [];

    public GcodeEventHook? SelectedPreset
    {
        get => _selectedPreset;
        set => this.RaiseAndSetIfChanged(ref _selectedPreset, value);
    }

    public ICommand AddPresetCommand { get; }
    public ICommand RemoveCommand { get; }

    public GcodeEventViewModel(ConfigManager configManager)
    {
        _configManager = configManager;
        AddPresetCommand = ReactiveCommand.Create(AddSelectedPreset);
        RemoveCommand = ReactiveCommand.Create<GcodeEventHook>(Remove);
        _configManager.OnConfigLoaded += (_, cfg) => Load(cfg);

        if (_configManager.GHalSenderConfig != null)
            Load(_configManager.GHalSenderConfig);
    }

    private void Load(GHalSenderConfig config)
    {
        Items.Clear();
        foreach (var hook in config.GcodeEvents)
            Items.Add(hook.Clone());
    }

    private void AddSelectedPreset()
    {
        // No selection yet is the common case on a fresh dialog — start a blank rule
        // rather than doing nothing, so the Add button is never a dead button.
        var source = SelectedPreset ?? GcodeEventHook.Presets.Last();
        Items.Add(source.Clone());
    }

    private void Remove(GcodeEventHook hook) => Items.Remove(hook);

    public void Save()
    {
        if (_configManager.GHalSenderConfig == null) return;

        // Rules with no trigger can never fire; dropping them here keeps the config
        // file free of the blank rows a user added and then abandoned.
        _configManager.GHalSenderConfig.GcodeEvents = Items
            .Where(h => !string.IsNullOrWhiteSpace(h.Trigger))
            .Select(h => h.Clone())
            .ToList();
    }
}

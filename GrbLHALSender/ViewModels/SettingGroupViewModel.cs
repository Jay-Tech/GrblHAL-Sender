using GrbLHALSender.Settings;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels;

/// <summary>
/// One collapsible section on the settings page. Expanding a section is what triggers
/// its descriptions to be fetched — see <see cref="SettingsViewModel"/> — because
/// grblHAL only serves descriptions one setting at a time.
/// </summary>
public class SettingGroupViewModel : ViewModelBase
{
    private bool _isExpanded;

    public SettingGroupViewModel(string name, IEnumerable<GrblHalSetting> settings,
        Action<SettingGroupViewModel>? onExpanded = null)
    {
        Name = name;
        Settings = new ObservableCollection<GrblHalSetting>(settings);

        // Fetching hangs off the explicit toggle, NOT off IsExpanded. Filtering
        // auto-expands every matching group, and firing a $SED= sweep per group from
        // a keystroke would put hundreds of commands on the wire at once.
        ToggleCommand = ReactiveCommand.Create(() =>
        {
            IsExpanded = !IsExpanded;
            if (IsExpanded) onExpanded?.Invoke(this);
        });
    }

    public string Name { get; }

    public ObservableCollection<GrblHalSetting> Settings { get; }

    public int Count => Settings.Count;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }

    public ICommand ToggleCommand { get; }
}

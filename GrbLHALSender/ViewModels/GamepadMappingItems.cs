using ReactiveUI;

namespace GrbLHALSender.ViewModels;

public class ButtonMappingItem : ReactiveObject
{
    private string _selectedAction = "None";

    public string ButtonName { get; init; } = "";
    public string DisplayName { get; init; } = "";

    public string SelectedAction
    {
        get => _selectedAction;
        set => this.RaiseAndSetIfChanged(ref _selectedAction, value);
    }
}

public class AxisMappingItem : ReactiveObject
{
    private string _selectedCncAxis = "None";
    private bool _isInverted;

    public string AxisName { get; init; } = "";
    public string DisplayName { get; init; } = "";

    public string SelectedCncAxis
    {
        get => _selectedCncAxis;
        set => this.RaiseAndSetIfChanged(ref _selectedCncAxis, value);
    }

    public bool IsInverted
    {
        get => _isInverted;
        set => this.RaiseAndSetIfChanged(ref _isInverted, value);
    }
}

public class TriggerMappingItem : ReactiveObject
{
    private string _selectedAction = "None";

    public string TriggerName { get; init; } = "";
    public string DisplayName { get; init; } = "";

    public string SelectedAction
    {
        get => _selectedAction;
        set => this.RaiseAndSetIfChanged(ref _selectedAction, value);
    }
}

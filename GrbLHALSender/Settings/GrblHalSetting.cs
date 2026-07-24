using ReactiveUI;
using System;
using System.Globalization;

namespace GrbLHALSender.Settings;

public partial class GrblHalSetting : ReactiveObject
{
    private string _settingValue;
    private int _id;
    private int _groupId;
    private string _name;
    private string _unit;
    private DataTypes _dataType;
    private string _format;
    private double _min;
    private double _max;
    private bool _allowNull;
    private bool _rebootRequired;
    private bool _needsSaving;
    private string? _description;
    private string _groupName = string.Empty;

    public int Id
    {
        get => _id;
        set => this.RaiseAndSetIfChanged(ref _id, value);
    }

    public int GroupId
    {
        get => _groupId;
        set => this.RaiseAndSetIfChanged(ref _groupId, value);
    }

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public string Unit
    {
        get => _unit;
        set => this.RaiseAndSetIfChanged(ref _unit, value);
    }

    public DataTypes DataType
    {
        get => _dataType;
        set => this.RaiseAndSetIfChanged(ref _dataType, value);
    }

    public string Format
    {
        get => _format;
        set => this.RaiseAndSetIfChanged(ref _format, value);
    }

    public double Min
    {
        get => _min;
        set => this.RaiseAndSetIfChanged(ref _min, value);
    }

    public double Max
    {
        get => _max;
        set => this.RaiseAndSetIfChanged(ref _max, value);
    }

    public bool AllowNull
    {
        get => _allowNull;
        internal set => this.RaiseAndSetIfChanged(ref _allowNull, value);
    }

    public bool RebootRequired
    {
        get => _rebootRequired;
        internal set => this.RaiseAndSetIfChanged(ref _rebootRequired, value);
    }

    public bool NeedsSaving
    {
        get => _needsSaving;
        set => this.RaiseAndSetIfChanged(ref _needsSaving, value);
    }

    public string SettingValue
    {
        get => _settingValue;
        set => this.RaiseAndSetIfChanged(ref _settingValue, value);
    }

    /// <summary>
    /// Explanatory text from <c>$SED=&lt;id&gt;</c>. Null until the owning group is
    /// expanded, and stays null on firmware built without descriptions.
    /// </summary>
    public string? Description
    {
        get => _description;
        set => this.RaiseAndSetIfChanged(ref _description, value);
    }

    /// <summary>Resolved group name, filled in from <c>$EG</c> so rows and search can use it.</summary>
    public string GroupName
    {
        get => _groupName;
        set => this.RaiseAndSetIfChanged(ref _groupName, value);
    }

    public GrblHalSetting(int id, string value)
    {
        Id = id;
        SettingValue = value;
    }

    public GrblHalSetting(Span<string> data)
    {
        // Parse "SETTINGTYPE:id" from data[0] without mutating the caller's span
        var settingName = data[0].Split(':');
        var idStr = settingName.Length > 1 ? settingName[1] : settingName[0];

        Id = int.Parse(idStr);
        GroupId = int.Parse(data[1]);
        Name = data[2];

        if (data.Length > 3)
            Unit = data[3];
        if (data.Length > 4)
            DataType = string.IsNullOrEmpty(data[4])
                ? DataTypes.TEXT
                : Enum.TryParse<DataTypes>(data[4], true, out var dt) ? dt : DataTypes.TEXT;
        if (data.Length > 5)
            Format = data[5];
        if (data.Length > 6)
            Min = string.IsNullOrEmpty(data[6]) ? double.NaN : double.Parse(data[6], CultureInfo.InvariantCulture);
        if (data.Length > 7)
            Max = string.IsNullOrEmpty(data[7]) ? double.NaN : double.Parse(data[7], CultureInfo.InvariantCulture);
        if (data.Length > 8)
            RebootRequired = data[8] == "1";
        if (data.Length > 9)
            AllowNull = data[9] == "1";
    }

    public enum PendingMessageSet
    {
        NotPending = 0,
        Options = 1,
        Setting = 2
    }

    public enum DataTypes
    {
        BOOL = 0,
        BITFIELD,
        XBITFIELD,
        RADIOBUTTONS,
        AXISMASK,
        INTEGER,
        FLOAT,
        TEXT,
        PASSWORD,
        IP4
    };
}

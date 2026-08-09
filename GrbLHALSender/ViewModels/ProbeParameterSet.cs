using GrbLHALSender.Probe;
using GrbLHALSender.Utility;
using ReactiveUI;
using System.Runtime.CompilerServices;

namespace GrbLHALSender.ViewModels;

/// <summary>
/// One probe operation's editable rates and distances.
/// <para>
/// Exists so each tab can own its values without the view model growing a flat property per
/// field per operation. That mattered less when there was one shared set; with four it would
/// be thirty-two properties whose only difference is which cycle reads them, and the reason
/// they are separate at all is that mixing them was unsafe - a corner setup rewrote the
/// numbers that had established the tool length reference, and persisted the change.
/// </para>
/// <para>
/// Values are held as text and parsed on demand, matching how the view binds them: a field
/// mid-edit is not a number, and forcing it to be one turns an empty box into a zero. Zero is
/// harmless in some places and not in others - a zero clearance drags the tool across the job -
/// so the parse stays where the caller can validate it first.
/// </para>
/// </summary>
public class ProbeParameterSet : ReactiveObject
{
    private string _searchRateText = "250";
    private string _latchRateText = "125";
    private string _probeDistanceText = "12";
    private string _latchDistanceText = "6";
    private string _clearanceHeightText = "12";
    private string _probeDepthText = "6";
    private string _approxWidthText = "100";
    private string _approxHeightText = "200";

    public string SearchRateText
    {
        get => _searchRateText;
        set => Set(ref _searchRateText, value, nameof(SearchRate));
    }

    public string LatchRateText
    {
        get => _latchRateText;
        set => Set(ref _latchRateText, value, nameof(LatchRate));
    }

    public string ProbeDistanceText
    {
        get => _probeDistanceText;
        set => Set(ref _probeDistanceText, value, nameof(ProbeDistance));
    }

    public string LatchDistanceText
    {
        get => _latchDistanceText;
        set => Set(ref _latchDistanceText, value, nameof(LatchDistance));
    }

    public string ClearanceHeightText
    {
        get => _clearanceHeightText;
        set => Set(ref _clearanceHeightText, value, nameof(ClearanceHeight));
    }

    public string ProbeDepthText
    {
        get => _probeDepthText;
        set => Set(ref _probeDepthText, value, nameof(ProbeDepth));
    }

    public string ApproxWidthText
    {
        get => _approxWidthText;
        set => Set(ref _approxWidthText, value, nameof(ApproxWidth));
    }

    public string ApproxHeightText
    {
        get => _approxHeightText;
        set => Set(ref _approxHeightText, value, nameof(ApproxHeight));
    }

    public double SearchRate => _searchRateText.StringToDouble();
    public double LatchRate => _latchRateText.StringToDouble();
    public double ProbeDistance => _probeDistanceText.StringToDouble();
    public double LatchDistance => _latchDistanceText.StringToDouble();
    public double ClearanceHeight => _clearanceHeightText.StringToDouble();
    public double ProbeDepth => _probeDepthText.StringToDouble();
    public double ApproxWidth => _approxWidthText.StringToDouble();
    public double ApproxHeight => _approxHeightText.StringToDouble();

    /// <summary>
    /// Defaults for a display unit. Every pair is the same measurement in both, so switching
    /// units leaves the defaults agreeing rather than one set reading as the other.
    /// </summary>
    public void ApplyUnitDefaults(bool metric)
    {
        SearchRateText = metric ? "250" : "10";
        LatchRateText = metric ? "125" : "5";
        ProbeDistanceText = metric ? "12" : ".5";
        LatchDistanceText = metric ? "6" : ".25";
        ClearanceHeightText = metric ? "12" : ".5";
        ProbeDepthText = metric ? "6" : ".25";
        ApproxWidthText = metric ? "100" : "4";
        ApproxHeightText = metric ? "200" : "8";
    }

    /// <summary>Converts every field, so each keeps its physical size across a unit change.</summary>
    public void Rescale(System.Func<string, string> convert)
    {
        SearchRateText = convert(SearchRateText);
        LatchRateText = convert(LatchRateText);
        ProbeDistanceText = convert(ProbeDistanceText);
        LatchDistanceText = convert(LatchDistanceText);
        ClearanceHeightText = convert(ClearanceHeightText);
        ProbeDepthText = convert(ProbeDepthText);
        ApproxWidthText = convert(ApproxWidthText);
        ApproxHeightText = convert(ApproxHeightText);
    }

    public void Load(ProbeParameters source)
    {
        SearchRateText = source.SearchRate.ToInvariantString();
        LatchRateText = source.LatchRate.ToInvariantString();
        ProbeDistanceText = source.ProbeDistance.ToInvariantString();
        LatchDistanceText = source.LatchDistance.ToInvariantString();
        ClearanceHeightText = source.ClearanceHeight.ToInvariantString();
        ProbeDepthText = source.ProbeDepth.ToInvariantString();
        ApproxWidthText = source.ApproxWidth.ToInvariantString();
        ApproxHeightText = source.ApproxHeight.ToInvariantString();
    }

    public void Save(ProbeParameters target)
    {
        target.SearchRate = SearchRate;
        target.LatchRate = LatchRate;
        target.ProbeDistance = ProbeDistance;
        target.LatchDistance = LatchDistance;
        target.ClearanceHeight = ClearanceHeight;
        target.ProbeDepth = ProbeDepth;
        target.ApproxWidth = ApproxWidth;
        target.ApproxHeight = ApproxHeight;
        target.Initialized = true;
    }

    private void Set(ref string field, string value, string numericName,
        [CallerMemberName] string? textName = null)
    {
        if (field == value) return;
        field = value;
        this.RaisePropertyChanged(textName);
        this.RaisePropertyChanged(numericName);
    }
}

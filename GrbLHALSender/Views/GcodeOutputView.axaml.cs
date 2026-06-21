using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using GrbLHALSender.ViewModels;
using System;
using System.ComponentModel;
using System.Text;
using System.Xml;

namespace GrbLHALSender.Views;

public partial class GcodeOutputView : UserControl
{
    private DispatcherTimer? _scrollTimer;
    private int _lastScrolledIndex = -1;
    private int _lastBuiltCount = -1;
    private readonly AckLineNumberMargin _ackLineNumberMargin = new();
    private JobViewModel? _subscribedVm;

    public GcodeOutputView()
    {
        InitializeComponent();

        LoadGcodeHighlighting();

        // Install custom line-number margin that colors acked lines white.
        GCodeEditor.TextArea.LeftMargins.Add(_ackLineNumberMargin);

        // Click on a gcode line -> highlight the matching segment in the 3D view.
        // AvaloniaEdit handles pointer events internally for caret positioning,
        // so we must subscribe with handledEventsToo to still receive them.
        GCodeEditor.TextArea.AddHandler(
            InputElement.PointerReleasedEvent,
            OnEditorPointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);

        DataContextChanged += OnDataContextChanged;

        // Throttled refresh/scroll timer — at ~4 Hz, rebuilds Document.Text whenever the
        // VM's GCodeOutPut count changes, then scrolls/highlights the current line.
        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _scrollTimer.Tick += ScrollTimerTick;
        _scrollTimer.Start();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm != null)
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;

        _subscribedVm = DataContext as JobViewModel;

        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged += OnVmPropertyChanged;
            ApplyAckedLineIndex(_subscribedVm.AckedLineIndex);
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(JobViewModel.AckedLineIndex) && sender is JobViewModel vm)
        {
            ApplyAckedLineIndex(vm.AckedLineIndex);
        }
    }

    private void ApplyAckedLineIndex(int index)
    {
        _ackLineNumberMargin.AckedLineIndex = index;
    }

    private void OnEditorPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_subscribedVm == null) return;
        if (e.InitialPressMouseButton != MouseButton.Left) return;

        int caretLine = GCodeEditor.TextArea.Caret.Line; // 1-based
        if (caretLine <= 0) return;

        _subscribedVm.SelectGcodeLine(caretLine - 1);
    }

    private void LoadGcodeHighlighting()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://GrbLHALSender/Assets/Gcode.xshd"));
            using var reader = new XmlTextReader(stream);
            var highlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            GCodeEditor.SyntaxHighlighting = highlighting;
        }
        catch
        {
            // Highlighting is cosmetic; if the resource fails to load just render plain text.
        }
    }

    private void ScrollTimerTick(object? sender, EventArgs e)
    {
        if (DataContext is not JobViewModel vm) return;

        var count = vm.GCodeOutPut.Count;
        if (count != _lastBuiltCount)
        {
            _lastBuiltCount = count;
            _lastScrolledIndex = -1;
            RebuildDocument(vm);
        }

        if (!IsEffectivelyVisible) return;

        var index = vm.GcodeFileIndex;
        if (index == _lastScrolledIndex) return;
        if (index < 0 || index >= count) return;

        _lastScrolledIndex = index;
        GCodeEditor.ScrollToLine(index + 1); // 1-based
        var line = GCodeEditor.Document.GetLineByNumber(index + 1);
        GCodeEditor.Select(line.Offset, line.Length);
    }

    private void RebuildDocument(JobViewModel vm)
    {
        var sb = new StringBuilder(vm.GCodeOutPut.Count * 24);
        for (int i = 0; i < vm.GCodeOutPut.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(vm.GCodeOutPut[i].Text);
        }
        GCodeEditor.Document.Text = sb.ToString();
    }
}

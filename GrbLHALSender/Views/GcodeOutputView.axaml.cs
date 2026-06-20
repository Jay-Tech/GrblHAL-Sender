using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using GrbLHALSender.ViewModels;
using System;
using System.Text;
using System.Xml;

namespace GrbLHALSender.Views;

public partial class GcodeOutputView : UserControl
{
    private DispatcherTimer? _scrollTimer;
    private int _lastScrolledIndex = -1;
    private int _lastBuiltCount = -1;

    public GcodeOutputView()
    {
        InitializeComponent();

        LoadGcodeHighlighting();

        // Throttled refresh/scroll timer — at ~4 Hz, rebuilds Document.Text whenever the
        // VM's GCodeOutPut count changes, then scrolls/highlights the current line.
        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _scrollTimer.Tick += ScrollTimerTick;
        _scrollTimer.Start();
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

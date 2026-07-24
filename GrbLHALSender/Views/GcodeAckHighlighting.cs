using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using GrbLHALSender.Theming;
using System.Globalization;

namespace GrbLHALSender.Views;

/// <summary>
/// Line-number margin that colors line numbers white once their line has been
/// acknowledged by grblHAL (1-based line number &lt;= <see cref="AckedLineIndex"/>),
/// and uses the default foreground otherwise.
/// </summary>
public sealed class AckLineNumberMargin : LineNumberMargin
{
    public static readonly StyledProperty<int> AckedLineIndexProperty =
        AvaloniaProperty.Register<AckLineNumberMargin, int>(nameof(AckedLineIndex));

    public int AckedLineIndex
    {
        get => GetValue(AckedLineIndexProperty);
        set => SetValue(AckedLineIndexProperty, value);
    }

    public IBrush AckedForeground { get; set; } =
        new SolidColorBrush(ThemeService.Current.TextPrimary);

    // Dim default so the acked lines stand out against the not-yet-sent ones.
    public IBrush DefaultForeground { get; set; } =
        new SolidColorBrush(ThemeService.Current.TextMuted);

    static AckLineNumberMargin()
    {
        AffectsRender<AckLineNumberMargin>(AckedLineIndexProperty);
    }

    public override void Render(DrawingContext drawingContext)
    {
        var textView = TextView;
        var renderSize = Bounds.Size;
        if (textView == null || !textView.VisualLinesValid) return;

        var typeface = new Typeface(
            TextElement.GetFontFamily(this),
            TextElement.GetFontStyle(this),
            TextElement.GetFontWeight(this));
        var fontSize = TextElement.GetFontSize(this);
        var ackedIdx = AckedLineIndex;

        foreach (var line in textView.VisualLines)
        {
            int lineNumber = line.FirstDocumentLine.LineNumber;
            var brush = lineNumber <= ackedIdx ? AckedForeground : DefaultForeground;
            var formatted = new FormattedText(
                lineNumber.ToString(CultureInfo.CurrentCulture),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                brush);
            var y = line.VisualTop - textView.VerticalOffset;
            drawingContext.DrawText(
                formatted,
                new Point(renderSize.Width - formatted.Width, y));
        }
    }
}


using Avalonia.Controls;
using Avalonia.Threading;
using ReactiveUI;
using System;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels;

public class VirtualKeyboardViewModel : ViewModelBase, IDialogCloseable
{
    private TextBox? _targetTextBox;

    public ICommand KeyPressCommand { get; }
    public ICommand CloseCommand { get; }
    public Action? CloseAction { get; set; }

    public VirtualKeyboardViewModel()
    {
        KeyPressCommand = ReactiveCommand.Create<string>(OnKeyPress);
        CloseCommand = ReactiveCommand.Create(() => CloseAction?.Invoke());
    }

    /// <summary>
    /// Sets the target TextBox that receives keyboard input, with the caret at the end and
    /// nothing selected.
    /// <para>
    /// The double-tap that opens the keyboard is a word-select as far as the TextBox is
    /// concerned, and it leaves the caret wherever the tap landed. Clearing that matters
    /// beyond the caret position on a touchscreen: while text is selected Avalonia shows drag
    /// handles either side of it, which sit over the neighbouring fields and are fiddly to
    /// dismiss with a finger. Setting CaretIndex alone does not clear a selection.
    /// </para>
    /// </summary>
    public void SetTarget(TextBox textBox)
    {
        _targetTextBox = textBox;
        MoveCaretToEnd(textBox);

        // And again once the input has finished being handled. The TextBox applies its own
        // word-select as part of the same gesture, after this has run, so clearing it only
        // here gets overwritten and the text stays highlighted.
        Dispatcher.UIThread.Post(() => MoveCaretToEnd(textBox), DispatcherPriority.Background);
    }

    private static void MoveCaretToEnd(TextBox textBox)
    {
        var end = textBox.Text?.Length ?? 0;
        textBox.SelectionStart = end;
        textBox.SelectionEnd = end;
        textBox.CaretIndex = end;
    }

    private void OnKeyPress(string key)
    {
        if (_targetTextBox == null) return;

        var text = _targetTextBox.Text ?? string.Empty;
        var caretIndex = Math.Clamp(_targetTextBox.CaretIndex, 0, text.Length);

        // Whatever comes next replaces a live selection, the way a hardware keyboard behaves.
        // Without this the key was inserted at the caret and the highlighted text was left
        // sitting there untouched, which is what made input look like it landed somewhere
        // unrelated to what was selected.
        var from = Math.Clamp(Math.Min(_targetTextBox.SelectionStart, _targetTextBox.SelectionEnd),
            0, text.Length);
        var to = Math.Clamp(Math.Max(_targetTextBox.SelectionStart, _targetTextBox.SelectionEnd),
            0, text.Length);

        if (to > from && key is not ("Left" or "Right"))
        {
            text = text.Remove(from, to - from);
            caretIndex = from;
            Apply(text, caretIndex);

            // On a selection, these have done their work by removing it.
            if (key is "BackSpace" or "Del")
            {
                RestoreTargetFocus();
                return;
            }
        }

        switch (key)
        {
            case "BackSpace":
                if (caretIndex > 0)
                    Apply(text.Remove(caretIndex - 1, 1), caretIndex - 1);
                break;

            case "Enter":
                Apply(text.Insert(caretIndex, "\n"), caretIndex + 1);
                break;

            case "Tab":
                Apply(text.Insert(caretIndex, "\t"), caretIndex + 1);
                break;

            case "Space":
                Apply(text.Insert(caretIndex, " "), caretIndex + 1);
                break;

            case "Left":
                Apply(text, Math.Max(0, caretIndex - 1));
                break;

            case "Right":
                Apply(text, Math.Min(text.Length, caretIndex + 1));
                break;

            case "Del":
                if (caretIndex < text.Length)
                    Apply(text.Remove(caretIndex, 1), caretIndex);
                break;

            default:
                // Regular keys and CNC shortcuts (G0, G1, G90, G91, X0, Y0, etc.)
                Apply(text.Insert(caretIndex, key), caretIndex + key.Length);
                break;
        }

        RestoreTargetFocus();
    }

    /// <summary>
    /// Writes the text back and leaves the caret sitting at a point, never a range. Going
    /// through one place means no key can leave a selection behind for the next one to trip on.
    /// </summary>
    private void Apply(string text, int caretIndex)
    {
        if (_targetTextBox == null) return;

        _targetTextBox.Text = text;

        var caret = Math.Clamp(caretIndex, 0, text.Length);
        _targetTextBox.SelectionStart = caret;
        _targetTextBox.SelectionEnd = caret;
        _targetTextBox.CaretIndex = caret;
    }

    /// <summary>
    /// Hands focus back to the target TextBox's window after every key press.
    /// Touching the keyboard window activates it (OS-level, we can't fully
    /// prevent it on every WM); re-activating the main window here means the
    /// next touch on the main screen works first time instead of needing one
    /// touch to refocus and a second to act.
    /// </summary>
    private void RestoreTargetFocus()
    {
        if (_targetTextBox == null) return;

        if (TopLevel.GetTopLevel(_targetTextBox) is Window window && !window.IsActive)
            window.Activate();

        _targetTextBox.Focus();

        // Focusing a TextBox can select its contents, so the highlight came back after every
        // key even once the double-tap's own selection had been cleared. Collapse at the caret
        // rather than at the end, or the arrow keys would have nothing to move.
        var caret = _targetTextBox.CaretIndex;
        _targetTextBox.SelectionStart = caret;
        _targetTextBox.SelectionEnd = caret;
    }
}

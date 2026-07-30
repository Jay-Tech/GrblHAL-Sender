using Avalonia.Controls;
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
    /// concerned, and it leaves the caret wherever the tap landed. Collapsing the selection
    /// matters beyond the caret position on a touchscreen: while text is selected Avalonia
    /// shows drag handles either side of it, which sit over the neighbouring fields and are
    /// fiddly to dismiss with a finger. Setting CaretIndex alone does not clear a selection.
    /// </para>
    /// </summary>
    public void SetTarget(TextBox textBox)
    {
        _targetTextBox = textBox;

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

        switch (key)
        {
            case "BackSpace":
                if (caretIndex > 0)
                {
                    _targetTextBox.Text = text.Remove(caretIndex - 1, 1);
                    _targetTextBox.CaretIndex = caretIndex - 1;
                }
                break;

            case "Enter":
                InsertText("\n", text, caretIndex);
                break;

            case "Tab":
                InsertText("\t", text, caretIndex);
                break;

            case "Space":
                InsertText(" ", text, caretIndex);
                break;

            case "Left":
                _targetTextBox.CaretIndex = Math.Max(0, caretIndex - 1);
                break;

            case "Right":
                _targetTextBox.CaretIndex = Math.Min(text.Length, caretIndex + 1);
                break;

            case "Del":
                if (caretIndex < text.Length)
                {
                    _targetTextBox.Text = text.Remove(caretIndex, 1);
                    _targetTextBox.CaretIndex = caretIndex;
                }
                break;

            default:
                // Regular keys and CNC shortcuts (G0, G1, G90, G91, X0, Y0, etc.)
                InsertText(key, text, caretIndex);
                break;
        }

        RestoreTargetFocus();
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
    }

    private void InsertText(string insert, string currentText, int caretIndex)
    {
        if (_targetTextBox == null) return;

        _targetTextBox.Text = currentText.Insert(caretIndex, insert);
        _targetTextBox.CaretIndex = caretIndex + insert.Length;
    }
}

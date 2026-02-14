using System;
using System.Windows.Input;
using Avalonia.Controls;
using ReactiveUI;

namespace GrbLHALSender.ViewModels;

public class VirtualKeyboardViewModel : ViewModelBase
{
    private TextBox? _targetTextBox;

    public ICommand KeyPressCommand { get; }

    public VirtualKeyboardViewModel()
    {
        KeyPressCommand = ReactiveCommand.Create<string>(OnKeyPress);
    }

    /// <summary>
    /// Sets the target TextBox that receives keyboard input.
    /// </summary>
    public void SetTarget(TextBox textBox)
    {
        _targetTextBox = textBox;
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
                return;

            case "Enter":
                InsertText("\n", text, caretIndex);
                return;

            case "Tab":
                InsertText("\t", text, caretIndex);
                return;

            case "Space":
                InsertText(" ", text, caretIndex);
                return;

            default:
                // Regular keys and CNC shortcuts (G0, G1, G90, G91, X0, Y0, etc.)
                InsertText(key, text, caretIndex);
                return;
        }
    }

    private void InsertText(string insert, string currentText, int caretIndex)
    {
        if (_targetTextBox == null) return;

        _targetTextBox.Text = currentText.Insert(caretIndex, insert);
        _targetTextBox.CaretIndex = caretIndex + insert.Length;
    }
}

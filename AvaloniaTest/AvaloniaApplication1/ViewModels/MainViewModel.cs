using System;
using System.Security.Cryptography;
using System.Xml.Linq;
using AvaloniaApplication1.GrblCore;

namespace AvaloniaApplication1.ViewModels;

public class MainViewModel : ViewModelBase
{
    private bool _isPhysicalFileLoaded;
    private bool _isFileLoaded;

    public bool IsPhysicalFileLoaded
    {
        get => _isPhysicalFileLoaded;
        set
        {
            if (_isPhysicalFileLoaded != value)
            {
                _isPhysicalFileLoaded = value;
                OnPropertyChanged(nameof(IsPhysicalFileLoaded));
            }
        } 
    }
    public bool IsFileLoaded
    {
        get => _isFileLoaded;
        set
        {
            if (_isFileLoaded != value)
            {
                _isFileLoaded = value;
                OnPropertyChanged(nameof(IsFileLoaded));
            }
        }
    }
    public void ExecuteMacro(string macro)
    {
        if (macro != null && macro != string.Empty)
        {
            bool ok = true;
            var commands = macro.Split('\n');

            var parser = new GCodeParser();

            for (int i = 0; i < commands.Length; i++)
            {
                try
                {
                    commands[i] = commands[i].Replace("\r", "");
                    parser.ParseBlock(ref commands[i], false);
                }
                catch (Exception e)
                {
                    //if (!(ok = System.Windows.MessageBox.Show(string.Format(LibStrings.FindResource("LoadError").Replace("\\n", "\r"), e.Message, i + 1, commands[i]), "ioSender", System.Windows.MessageBoxButton.YesNo) == System.Windows.MessageBoxResult.Yes))
                    //    break;
                }
            }

            if (!ok) return;
            foreach (var command in commands)
            {
                ApplyCommand(command);
                //if (ResponseLogVerbose && !string.IsNullOrEmpty(command))
                //    ResponseLog.Add(command);
            }
        }
    }
    private void ApplyCommand(string command)
    {
        bool ok;
        string cmd = command.ToUpper();

        //if ((ok = !(GrblState.State == GrblStates.Tool && !(ucmd.StartsWith("$J=") || cmd == "$TPW" || cmd.Contains("G10L20")))))
        //   // MDI = command;
        //else
           // Message = LibStrings.FindResource("JoggingOnly");

        //return ok;
    }

    //public GrblStateModel GreStateModel { get; set; }
}

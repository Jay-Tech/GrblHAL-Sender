using System;
using System.Diagnostics.Tracing;
using ReactiveUI;
using System.Windows.Input;
using Avalonia.Xaml.Interactivity;
using GrbLHALSender.Communication;

namespace GrbLHALSender.ViewModels
{
   
    public  class MdiViewModel : ViewModelBase
    {
        public event Action<string> MidiTextCommitted;
        private string _mdiText;
        public string MdiText
        {
            get => _mdiText;
            set => this.RaiseAndSetIfChanged(ref _mdiText, value);
        }
        public ICommand KeyPressCommand { get; }
        public ICommand MdiTextCommand { get; }
        public MdiViewModel()
        {
            KeyPressCommand = ReactiveCommand.Create<string>(KeyPressed);
            MdiTextCommand = ReactiveCommand.Create<string>(CommitMdiText);
        }

        private void KeyPressed(string key)
        {
            string text;
            switch (key)
            {
                case "Ent":
                    text = "\r\n";
                    break;
                case "Spc":
                    text = " ";
                    break;
                case "Del":
                {
                    if (MdiText.EndsWith("\r\n"))
                    {
                        MdiText = MdiText.TrimEnd();
                        return;
                    }

                    if (MdiText.Length >= 1)
                        MdiText = MdiText.Remove(MdiText.Length - 1);
                    return;

                }
                default:
                    text = key;
                    break;
            }

            MdiText += text;
        }
        private void CommitMdiText(string command)
        {
            MidiTextCommitted?.Invoke(command);
        }

    }
}

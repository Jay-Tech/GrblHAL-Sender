using ReactiveUI;
using System;
using System.Windows.Input;

namespace GrbLHALSender.ViewModels
{

    public class MdiViewModel : ViewModelBase
    {
        public event Action<string> MidiTextCommitted;
        private string _mdiText;
        private int _caretIndex;

        public string MdiText
        {
            get => _mdiText;
            set => this.RaiseAndSetIfChanged(ref _mdiText, value);
        }
        public int CaretIndex
        {
            get => _caretIndex;
            set => this.RaiseAndSetIfChanged(ref _caretIndex, value);
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
            CaretIndex = MdiText.Length;
        }



        private void CommitMdiText(string command)
        {
            MidiTextCommitted?.Invoke(command);
            MdiText = string.Empty;
        }

    }
}

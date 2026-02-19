using ReactiveUI;

namespace GrbLHALSender.Configuration
{
    public class AtcConfig : ReactiveObject
    {
        private string? _unloadToolMacroName;
        private string? _tlrMacroName;
        private bool _enableAtc;

        public bool EnableAtc
        {
            get => _enableAtc;
            set => this.RaiseAndSetIfChanged(ref _enableAtc, value);
        }

        public string? TlrMacroName
        {
            get => _tlrMacroName;
            set => this.RaiseAndSetIfChanged(ref _tlrMacroName, value);
        }

        public string? UnloadToolMacroName
        {
            get => _unloadToolMacroName;
            set => this.RaiseAndSetIfChanged(ref _unloadToolMacroName, value);
        }

        public AtcConfig()
        {
            EnableAtc = false;
            TlrMacroName = string.Empty;
            UnloadToolMacroName = string.Empty;
        }
    }
}

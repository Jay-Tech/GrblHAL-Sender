using Avalonia.Platform.Storage;
using GrbLHALSender.Firmware;
using GrbLHALSender.States;
using ReactiveUI;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace GrbLHALSender.ViewModels;

public class FirmwareViewModel : ViewModelBase
{
    private readonly FirmwareInstallService _firmwareService;
    private readonly MachineStateService _machineStateService;

    private string _hexFilePath = "";
    private string _hexFileInfo = "";
    private bool _isInstalling;
    private double _installProgress;
    private string _phase = "";
    private string _statusMessage = "";
    private bool _isConnected;
    private CancellationTokenSource? _installCts;

    public FirmwareViewModel(FirmwareInstallService firmwareService,
        MachineStateService machineStateService)
    {
        _firmwareService = firmwareService;
        _machineStateService = machineStateService;

        IsConnected = _machineStateService.Connected;
        _machineStateService.PropertyChanged += OnMachineStateChanged;

        BrowseCommand = ReactiveCommand.CreateFromTask(BrowseAsync);
        OpenWebBuilderCommand = ReactiveCommand.Create(OpenWebBuilder);
        CancelInstallCommand = ReactiveCommand.Create(CancelInstall);

        var canInstall = this.WhenAnyValue(
            x => x.HexFilePath, x => x.IsInstalling,
            (path, installing) => !string.IsNullOrEmpty(path) && !installing);
        InstallCommand = ReactiveCommand.CreateFromTask(InstallAsync, canInstall);
    }

    // ---- Properties ----

    public string HexFilePath
    {
        get => _hexFilePath;
        set => this.RaiseAndSetIfChanged(ref _hexFilePath, value);
    }

    public string HexFileInfo
    {
        get => _hexFileInfo;
        set => this.RaiseAndSetIfChanged(ref _hexFileInfo, value);
    }

    public bool IsInstalling
    {
        get => _isInstalling;
        set => this.RaiseAndSetIfChanged(ref _isInstalling, value);
    }

    public double InstallProgress
    {
        get => _installProgress;
        set => this.RaiseAndSetIfChanged(ref _installProgress, value);
    }

    public string Phase
    {
        get => _phase;
        set => this.RaiseAndSetIfChanged(ref _phase, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => this.RaiseAndSetIfChanged(ref _isConnected, value);
    }

    // ---- Commands ----

    public ReactiveCommand<Unit, Unit> BrowseCommand { get; }
    public ReactiveCommand<Unit, Unit> InstallCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenWebBuilderCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelInstallCommand { get; }

    // ---- Interactions (file picker handled by the View) ----

    public Core.Interaction<string, IStorageFile?> SelectHexFileInteraction { get; } = new();

    // ---- Implementations ----

    private async Task BrowseAsync()
    {
        var file = await SelectHexFileInteraction.HandleAsync("Select firmware (.hex) file");
        if (file == null) return;

        var path = file.Path.LocalPath;
        try
        {
            // Parse up-front so a bad file is rejected at selection time,
            // not after the controller has already been rebooted into DFU.
            HexFileInfo = await Task.Run(() => FirmwareInstallService.DescribeHexFile(path));
            HexFilePath = path;
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            HexFilePath = "";
            HexFileInfo = "";
            StatusMessage = $"Invalid firmware file: {ex.Message}";
        }
    }

    private async Task InstallAsync()
    {
        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();

        IsInstalling = true;
        InstallProgress = 0;
        StatusMessage = "";

        var progress = new Progress<FirmwareProgress>(p =>
        {
            Phase = p.Phase;
            InstallProgress = p.Percent;
            StatusMessage = p.Message;
        });

        try
        {
            await _firmwareService.InstallAsync(HexFilePath, progress, _installCts.Token);
            Phase = "";
        }
        catch (OperationCanceledException)
        {
            Phase = "";
            StatusMessage = "Installation cancelled. If the flash was already erased, the board " +
                            "stays in DFU mode — run Install again to recover.";
        }
        catch (Exception ex)
        {
            Phase = "";
            StatusMessage = ex.Message;
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private void CancelInstall()
    {
        _installCts?.Cancel();
    }

    private void OpenWebBuilder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(FirmwareInstallService.WebBuilderUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open browser: {ex.Message}";
        }
    }

    private void OnMachineStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MachineStateService.Connected))
            IsConnected = _machineStateService.Connected;
    }
}

using Avalonia.Platform.Storage;
using GrbLHALSender.SdCard;
using GrbLHALSender.States;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace GrbLHALSender.ViewModels;

public class SdCardViewModel : ViewModelBase
{
    private readonly SdCardService _sdCardService;
    private readonly MachineStateService _machineStateService;

    private ObservableCollection<SdCardFileInfo> _files = new();
    private SdCardFileInfo? _selectedFile;
    private bool _isTransferring;
    private double _transferProgress;
    private string _transferStatus = "";
    private string _statusMessage = "";
    private bool _isConnected;
    private string _transferModeDisplay = "N/A";
    private CancellationTokenSource? _transferCts;

    public SdCardViewModel(SdCardService sdCardService,
        MachineStateService machineStateService)
    {
        _sdCardService = sdCardService;
        _machineStateService = machineStateService;

        // Initialize from current state (user may already be connected)
        IsConnected = _machineStateService.Connected;
        UpdateTransferMode();

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        UploadCommand = ReactiveCommand.CreateFromTask(UploadAsync);
        DownloadCommand = ReactiveCommand.CreateFromTask(DownloadAsync);
        DeleteCommand = ReactiveCommand.CreateFromTask(DeleteAsync);
        RunCommand = ReactiveCommand.Create(RunSelectedFile);
        CancelTransferCommand = ReactiveCommand.Create(CancelTransfer);

        _machineStateService.PropertyChanged += OnMachineStateChanged;
    }

    // ---- Properties ----

    public ObservableCollection<SdCardFileInfo> Files
    {
        get => _files;
        set => this.RaiseAndSetIfChanged(ref _files, value);
    }

    public SdCardFileInfo? SelectedFile
    {
        get => _selectedFile;
        set => this.RaiseAndSetIfChanged(ref _selectedFile, value);
    }

    public bool IsTransferring
    {
        get => _isTransferring;
        set => this.RaiseAndSetIfChanged(ref _isTransferring, value);
    }

    public double TransferProgress
    {
        get => _transferProgress;
        set => this.RaiseAndSetIfChanged(ref _transferProgress, value);
    }

    public string TransferStatus
    {
        get => _transferStatus;
        set => this.RaiseAndSetIfChanged(ref _transferStatus, value);
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

    public string TransferModeDisplay
    {
        get => _transferModeDisplay;
        set => this.RaiseAndSetIfChanged(ref _transferModeDisplay, value);
    }

    // ---- Commands ----

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> UploadCommand { get; }
    public ReactiveCommand<Unit, Unit> DownloadCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> RunCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelTransferCommand { get; }

    // ---- Interactions (file pickers handled by View) ----

    public Core.Interaction<string, IReadOnlyList<IStorageFile>?> SelectFilesInteraction { get; } = new();
    public Core.Interaction<string, IStorageFile?> SaveFileInteraction { get; } = new();

    // ---- Command implementations ----

    private async Task RefreshAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected";
            return;
        }

        try
        {
            StatusMessage = "Mounting SD card...";

            // Run SD card operations on a background thread to avoid blocking the UI.
            // The underlying SendCommand/SendAsyncCommand use TaskCompletionSource
            // which can resume on the calling (UI) synchronization context and deadlock.
            var files = await Task.Run(async () =>
            {
                // Try to mount — some controllers return error if already mounted, that's OK
                await _sdCardService.MountSdCardAsync();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                return await _sdCardService.ListFilesAsync(cts.Token);
            });

            // Back on UI thread — update the collection
            StatusMessage = "Loading file list...";
            Files.Clear();
            foreach (var file in files)
                Files.Add(file);

            StatusMessage = files.Count > 0
                ? $"{files.Count} file(s) found"
                : "No files found (SD card may not be available)";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Refresh timed out";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private async Task UploadAsync()
    {
        if (!IsConnected || IsTransferring) return;

        try
        {
            var selectedFiles = await SelectFilesInteraction.HandleAsync("Select file to upload");
            if (selectedFiles == null || selectedFiles.Count == 0) return;

            var file = selectedFiles[0];
            var localPath = file.Path.LocalPath;
            var remoteName = file.Name;

            _transferCts?.Dispose();
            _transferCts = new CancellationTokenSource();

            IsTransferring = true;
            TransferProgress = 0;
            TransferStatus = "Starting upload...";

            var progress = new Progress<TransferProgress>(p =>
            {
                TransferProgress = p.Percentage;
                TransferStatus = p.StatusMessage ?? "";
            });

            // Run on background thread to avoid UI thread deadlocks with FTP/command operations
            var success = await Task.Run(async () =>
                await _sdCardService.UploadFileAsync(
                    localPath, remoteName, progress, _transferCts.Token));

            StatusMessage = success ? $"Uploaded {remoteName}" : "Upload failed";

            // Refresh file list after upload
            if (success)
                await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Upload cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Upload error: {ex.Message}";
        }
        finally
        {
            IsTransferring = false;
        }
    }

    private async Task DownloadAsync()
    {
        if (!IsConnected || IsTransferring || SelectedFile == null) return;

        try
        {
            var saveFile = await SaveFileInteraction.HandleAsync("Save file as");
            if (saveFile == null) return;

            var localPath = saveFile.Path.LocalPath;
            var remoteName = SelectedFile.FileName;

            _transferCts?.Dispose();
            _transferCts = new CancellationTokenSource();

            IsTransferring = true;
            TransferProgress = 0;
            TransferStatus = "Starting download...";

            var progress = new Progress<TransferProgress>(p =>
            {
                TransferProgress = p.Percentage;
                TransferStatus = p.StatusMessage ?? "";
            });

            // Run on background thread to avoid UI thread deadlocks with FTP operations
            var success = await Task.Run(async () =>
                await _sdCardService.DownloadFileAsync(
                    remoteName, localPath, progress, _transferCts.Token));

            StatusMessage = success ? $"Downloaded {remoteName}" : "Download failed";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download cancelled";
        }
        catch (NotSupportedException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Download error: {ex.Message}";
        }
        finally
        {
            IsTransferring = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (!IsConnected || SelectedFile == null) return;

        var fileName = SelectedFile.FileName;
        StatusMessage = $"Deleting {fileName}...";

        // Run on background thread to avoid UI thread deadlocks with command operations
        var success = await Task.Run(async () =>
            await _sdCardService.DeleteFileAsync(fileName));
        StatusMessage = success ? $"Deleted {fileName}" : $"Failed to delete {fileName}";

        if (success)
            await RefreshAsync();
    }

    private void RunSelectedFile()
    {
        if (!IsConnected || SelectedFile == null) return;

        _sdCardService.RunFile(SelectedFile.FileName);
        StatusMessage = $"Running {SelectedFile.FileName}";
    }

    private void CancelTransfer()
    {
        _transferCts?.Cancel();
    }

    // ---- State tracking ----

    private void OnMachineStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MachineStateService.Connected))
        {
            IsConnected = _machineStateService.Connected;
            UpdateTransferMode();
        }
    }

    private void UpdateTransferMode()
    {
        var mode = _sdCardService.GetTransferMode();
        TransferModeDisplay = mode switch
        {
            TransferMode.YModem => "Y/M",
            TransferMode.Ftp => "FTP",
            _ => "N/A"
        };
    }
}

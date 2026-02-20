using Avalonia.Threading;
using GrbLHALSender.Configuration;
using GrbLHALSender.Gcode;
using GrbLHALSender.ViewModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace GrbLHALSender.WebServer;

public class WebServerService : IDisposable
{
    private readonly FileUploadService _fileUploadService;
    private readonly ConfigManager _configManager;
    private WebServerConfig _config = new();
    private WebApplication? _app;
    private CancellationTokenSource? _cts;
    private MainViewModel? _mainViewModel;

    public event EventHandler<string>? StatusMessage;

    public WebServerService(FileUploadService fileUploadService, ConfigManager configManager)
    {
        _fileUploadService = fileUploadService;
        _configManager = configManager;
    }

    public void SetViewModel(MainViewModel vm)
    {
        _mainViewModel = vm;
    }

    public void Initialize(WebServerConfig config)
    {
        _config = config;
        _fileUploadService.Initialize(config);

        if (!config.Enabled) return;
        Start();
    }

    public void Start()
    {
        if (_app != null) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            try
            {
                var builder = WebApplication.CreateSlimBuilder();

                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.ListenAnyIP(_config.Port);
                });

                // Suppress ASP.NET Core startup logging noise
                builder.Logging.ClearProviders();

                var app = builder.Build();

                // Serve embedded index.html as static file at root
                var assembly = Assembly.GetExecutingAssembly();
                var embeddedProvider = new EmbeddedFileProvider(assembly, "GrbLHALSender.WebServer.wwwroot");

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = embeddedProvider,
                    RequestPath = ""
                });

                // Map / to serve index.html
                app.MapGet("/", async (Microsoft.AspNetCore.Http.HttpContext context) =>
                {
                    var fileInfo = embeddedProvider.GetFileInfo("index.html");
                    if (fileInfo.Exists)
                    {
                        context.Response.ContentType = "text/html";
                        await using var stream = fileInfo.CreateReadStream();
                        await stream.CopyToAsync(context.Response.Body);
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                        await context.Response.WriteAsync("index.html not found");
                    }
                });

                // Map API endpoints
                app.MapEndpoints(this, _fileUploadService);

                // Store reference so Stop() can reach it
                _app = app;

                StatusMessage?.Invoke(this, $"Web server starting on port {_config.Port}...");

                // Start the server (non-blocking), then wait until cancellation is requested.
                await app.StartAsync(token);

                // Block here until Stop() cancels the token
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { }

                // Gracefully shut down Kestrel
                await app.StopAsync(CancellationToken.None);
                await app.DisposeAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"Web server error: {ex.Message}");
            }
        }, token);
    }

    public void Stop()
    {
        try
        {
            // Signal the RunAsync cancellation token — this triggers Kestrel's graceful shutdown
            _cts?.Cancel();
        }
        catch
        {
            // Best-effort
        }
        finally
        {
            _app = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Loads a G-code file into the sender by marshaling to the UI thread.
    /// </summary>
    public void LoadFileIntoSender(string filePath, string fileName)
    {
        if (_mainViewModel == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _mainViewModel.JobViewModel.FileName = fileName;
                var parser = new GCodeParser();
                parser.ParseGCodeFile(filePath, _mainViewModel.JobViewModel.FileComplete);
                StatusMessage?.Invoke(this, $"Loading file: {fileName}");
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"Failed to load file: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Reads current machine status from the ViewModel into a DTO for the API.
    /// Safe for cross-thread reads (simple value properties backed by RaiseAndSetIfChanged).
    /// </summary>
    public MachineStatusDto GetMachineStatus()
    {
        if (_mainViewModel == null)
            return new MachineStatusDto();

        var vm = _mainViewModel;
        var axes = vm.AxisCollection;
        string position = "--";
        if (axes != null && axes.Count > 0)
        {
            var parts = new string[Math.Min(axes.Count, 6)];
            for (int i = 0; i < parts.Length; i++)
            {
                var pos = axes[i].Position;
                var wpos = pos.MPos - pos.Wco;
                parts[i] = wpos.ToString("F3");
            }
            position = string.Join(" / ", parts);
        }

        var jobVm = vm.JobViewModel;
        string jobProgress = "--";
        if (jobVm != null && jobVm.FileLoaded)
        {
            var total = jobVm.ToolpathData?.Segments.Count ?? 0;
            var completed = jobVm.CompletedSegmentIndex;
            if (total > 0 && completed >= 0)
                jobProgress = $"{completed}/{total} ({Math.Min((int)((double)completed / total * 100), 100)}%)";
            else
                jobProgress = "Loaded";
        }

        return new MachineStatusDto
        {
            State = vm.GrblHalState,
            Position = position,
            FeedRate = vm.FeedRate,
            SpindleRpm = vm.ActualRpm,
            FileName = jobVm?.FileName ?? "",
            JobProgress = jobProgress,
            Connected = vm.Connected
        };
    }

    public void Dispose()
    {
        Stop();
    }
}

public class MachineStatusDto
{
    public string State { get; set; } = "";
    public string Position { get; set; } = "--";
    public int FeedRate { get; set; }
    public int SpindleRpm { get; set; }
    public string FileName { get; set; } = "";
    public string JobProgress { get; set; } = "--";
    public bool Connected { get; set; }
}

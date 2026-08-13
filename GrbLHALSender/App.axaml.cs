using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Gamepad;
using GrbLHALSender.Gcode;
using GrbLHALSender.Gpio;
using GrbLHALSender.SdCard;
using GrbLHALSender.States;
using GrbLHALSender.Theming;
using GrbLHALSender.ViewModels;
using GrbLHALSender.Views;
using GrbLHALSender.Updates;
using GrbLHALSender.WebServer;
using Microsoft.Extensions.DependencyInjection;
using GrbLHALSender.Pendant;

namespace GrbLHALSender;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {

        var collection = new ServiceCollection();
        collection.AddCommonServices();
        var services = collection.BuildServiceProvider();

        // Resolve before MainViewModel: its constructor calls ConfigManager.LoadConfig(),
        // and ThemeService applies the saved palette off that load event. Constructing it
        // afterwards would miss the event and leave the app on the startup palette.
        services.GetRequiredService<ThemeService>();

        // Same reason as ThemeService: it builds its outputs off ConfigManager's load
        // event, which MainViewModel's constructor raises. Resolved later it would come
        // up with no outputs until the next save.
        services.GetRequiredService<GpioOutputService>();

        var vm = services.GetRequiredService<MainViewModel>();


        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };

            desktop.ShutdownRequested += (_, _) => ShutDownServices(services);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = vm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Tears down every service that owns something outside the process.
    /// </summary>
    /// <remarks>
    /// Nothing called these before. Each service had a correct Stop() and none
    /// of them was ever invoked, so closing the window left the app running
    /// headless: the pendant listener still bound to its port and still
    /// accepting connections, and — the part that matters — the serial port to
    /// the controller still open.
    ///
    /// That was found the hard way. A sender with no window was still holding
    /// port 8422, still connected to a pendant, and still able to move the
    /// machine; the operator had closed it and reasonably believed it was gone.
    /// A CNC controller taking motion commands from a process nobody can see is
    /// the worst failure available here, and it cost an afternoon of measurements
    /// taken against an instance that was not the one being watched.
    ///
    /// Order is deliberate: silence every source of commands first, then close
    /// the link. CommunicationManager.ShutDown() sends a soft reset to flush the
    /// planner before it closes the port, and that has to be the last word on the
    /// wire rather than racing a jog still arriving from the pendant.
    ///
    /// Each step is guarded individually rather than wrapped as a block. A
    /// throwing gamepad must not skip the serial close — the later steps are the
    /// ones with physical consequences, so they cannot depend on the earlier ones
    /// succeeding.
    /// </remarks>
    private static void ShutDownServices(IServiceProvider services)
    {
        void Attempt(Action step)
        {
            try { step(); } catch { /* one failure must not skip the rest */ }
        }

        Attempt(() => services.GetRequiredService<PendantService>().Stop());
        Attempt(() => services.GetRequiredService<GamepadService>().Stop());
        Attempt(() => services.GetRequiredService<WebServerService>().Stop());
        Attempt(() => services.GetRequiredService<CommunicationManager>().ShutDown());
    }
}

public static class ServiceCollectionExtensions
{
    public static void AddCommonServices(this IServiceCollection collection)
    {
        collection.AddSingleton<ConfigManager>();
        collection.AddSingleton<ThemeService>();
        collection.AddSingleton<CommunicationManager>();
        collection.AddSingleton<MachineStateService>();
        collection.AddSingleton<GamepadService>();
        collection.AddSingleton<GpioOutputService>();
        collection.AddSingleton<FileUploadService>();
        collection.AddSingleton<WebServerService>();
        collection.AddSingleton<PendantService>();
        collection.AddSingleton<SdCardService>();
        collection.AddSingleton<UpdateCheckService>();
        // Explicit factory: the injector also has a test-only ctor overload, and
        // this leaves the container nothing to guess about.
        collection.AddSingleton(sp => new GcodeEventInjector(sp.GetRequiredService<ConfigManager>()));
        collection.AddTransient<SettingsViewModel>();
        collection.AddTransient<JobViewModel>();
        collection.AddTransient<MainViewModel>();
        collection.AddTransient<ProbeViewModel>();
        collection.AddTransient<MacroViewModel>();
        collection.AddTransient<ConnectionViewModel>();
        collection.AddTransient<DialogViewModel>();
        collection.AddTransient<MdiViewModel>();
        collection.AddTransient<AppConfigViewModel>();
        collection.AddTransient<SdCardViewModel>();
        collection.AddTransient<AuxOutputViewModel>();
        collection.AddTransient<GpioOutputViewModel>();
        collection.AddTransient<GcodeEventViewModel>();
        collection.AddTransient<SurfacingViewModel>();
    }

}
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
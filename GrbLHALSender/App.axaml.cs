using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GrbLHALSender.Communication;
using GrbLHALSender.Configuration;
using GrbLHALSender.Gamepad;
using GrbLHALSender.SdCard;
using GrbLHALSender.States;
using GrbLHALSender.ViewModels;
using GrbLHALSender.Views;
using GrbLHALSender.Updates;
using GrbLHALSender.WebServer;
using Microsoft.Extensions.DependencyInjection;

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
        collection.AddSingleton<CommunicationManager>();
        collection.AddSingleton<MachineStateService>();
        collection.AddSingleton<GamepadService>();
        collection.AddSingleton<FileUploadService>();
        collection.AddSingleton<WebServerService>();
        collection.AddSingleton<SdCardService>();
        collection.AddSingleton<UpdateCheckService>();
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
    }

}
using System.ComponentModel.DataAnnotations;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GrbLHAL_Sender.Communication;
using GrbLHAL_Sender.Configuration;
using GrbLHAL_Sender.ViewModels;
using GrbLHAL_Sender.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GrbLHAL_Sender;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
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
                DataContext =vm
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
        collection.AddTransient<SettingsViewModel>();
        collection.AddTransient<JobViewModel>();
        collection.AddTransient<MainViewModel>();
        collection.AddTransient<ProbeViewModel>();
    }

}
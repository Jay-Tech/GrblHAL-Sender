using Avalonia;
using ReactiveUI.Avalonia;
using ReactiveUI.Builder;
using System;
using System.Globalization;

namespace GrbLHALSender.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // grblHAL always speaks dot-decimal ("12.345"). Force invariant culture on every
        // thread so OS regions that use comma decimals (de-DE, fr-FR, ...) can't corrupt
        // number parsing/formatting anywhere in the app (DRO, jog, probing, G-code load).
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI(WithReactiveUiBuilder);

    private static void WithReactiveUiBuilder(ReactiveUIBuilder obj)
    {
        //throw new NotImplementedException();
    }
}

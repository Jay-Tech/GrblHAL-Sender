using Avalonia;
using GrbLHALSender.Utility;
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

        // One instance only. Two copies both auto-connecting fight over the serial port,
        // and the loser presents a working-looking window whose commands go nowhere.
        // Claimed before Avalonia starts so the second copy costs nothing and never
        // touches the port. Held for the process lifetime — the OS releases it on exit,
        // crash included, so a lock can never be left behind.
        using var instance = SingleInstance.TryAcquire();
        if (instance == null)
        {
            Console.Error.WriteLine(
                "GrblHAL Sender is already running. Close the other window first.");
            Environment.ExitCode = 1;
            return;
        }

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

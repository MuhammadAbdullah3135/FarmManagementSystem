using FMS.Mobile.Services;

namespace FMS.Mobile;

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var crashReporter = activationState?.Context?.Services?.GetService<CrashReporter>();
        return new Window(new MainPage(crashReporter))
        {
            Title = "FMS Mobile"
        };
    }
}

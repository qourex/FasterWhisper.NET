using Microsoft.Extensions.DependencyInjection;

namespace Qourex.FasterWhisper.NET.Samples.Maui.Gpu;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}

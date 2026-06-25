using Microsoft.Extensions.DependencyInjection;

namespace Qourex.FasterWhisper.NET.Samples.Maui.Cpu;

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
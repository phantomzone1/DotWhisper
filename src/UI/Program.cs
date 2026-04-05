namespace DotWhisper.UI;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        // TODO: Build host, configure DI, launch tray application
        Application.Run();
    }
}

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace ShushVault.Windows;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            var app = new App();
            GC.KeepAlive(app);
        });
    }
}

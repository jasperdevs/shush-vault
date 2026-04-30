using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace ShushVault.Windows
{
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; } = Window.Current;

        public App()
        {
            this.InitializeComponent();
            UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                LogCrash(args.Exception);
                args.SetObserved();
            };
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            MainWindow ??= new Window();
            MainWindow.ExtendsContentIntoTitleBar = true;
            MainWindow.SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt };

            if (MainWindow.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                MainWindow.Content = rootFrame;
            }

            MainWindow.Title = "Shush Vault";
            _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
            MainWindow.Activate();
            ResizeMainWindow(960, 660);
            ApplyWindowIcon();
        }

        private static void ApplyWindowIcon()
        {
            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
                if (!File.Exists(iconPath))
                {
                    return;
                }

                var hwnd = WindowNative.GetWindowHandle(MainWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                AppWindow.GetFromWindowId(windowId)?.SetIcon(iconPath);
            }
            catch
            {
            }
        }

        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            LogCrash(e.Exception);
            e.Handled = true;
        }

        internal static void LogCrash(Exception? exception)
        {
            if (exception is null)
            {
                return;
            }

            try
            {
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ShushVault");
                Directory.CreateDirectory(root);
                File.AppendAllText(
                    Path.Combine(root, "crash.log"),
                    $"{DateTimeOffset.Now:u}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        private static void ResizeMainWindow(int width, int height)
        {
            var hwnd = WindowNative.GetWindowHandle(MainWindow);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow.GetFromWindowId(windowId)?.Resize(new global::Windows.Graphics.SizeInt32(width, height));
        }
    }
}

using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ShushVault.Windows.Controls;
using Velopack;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using WinRT.Interop;

namespace ShushVault.Windows;

internal sealed class SettingsWindow : Window
{
    private static readonly SolidColorBrush CardSurface = new(Color.FromArgb(0xFF, 0x1C, 0x1C, 0x1C));
    private static readonly SolidColorBrush CardBorder = new(Color.FromArgb(0xFF, 0x26, 0x26, 0x26));

    private readonly AppSettings settings;
    private readonly AppSettingsStore settingsStore;
    private readonly string vaultPath;

    public SettingsWindow(
        string vaultPath,
        AppSettings settings,
        AppSettingsStore settingsStore)
    {
        this.vaultPath = vaultPath;
        this.settings = settings;
        this.settingsStore = settingsStore;
        ExtendsContentIntoTitleBar = true;
        Title = "Settings";
        SystemBackdrop = new MicaBackdrop { Kind = Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt };

        var (root, titleBar) = BuildContent();
        Content = root;
        SetTitleBar(titleBar);
        Resize(560, 640);
        if (root is FrameworkElement element)
        {
            element.Loaded += (_, _) => CursorHelper.ApplyToTree(element);
        }
    }

    private (UIElement Root, UIElement TitleBar) BuildContent()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleBar = new WindowTitleBar { Title = "Shush Vault" };
        root.Children.Add(titleBar);

        var scroll = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(28, 8, 28, 22)
        };
        Grid.SetRow(scroll, 1);

        var stack = new StackPanel { Spacing = 14 };
        scroll.Content = stack;
        root.Children.Add(scroll);

        stack.Children.Add(BuildVaultSection());
        stack.Children.Add(BuildClipboardSection());
        stack.Children.Add(BuildUpdatesSection());
        stack.Children.Add(BuildAboutSection());

        return (root, titleBar);
    }

    private Border BuildVaultSection()
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(SectionHeader("Vault"));

        var pathRow = new Grid();
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var pathBox = new TextBox
        {
            Text = vaultPath,
            IsReadOnly = true,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            Padding = new Thickness(12, 9, 84, 9)
        };
        pathRow.Children.Add(pathBox);

        var iconBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };

        var copyButton = QuietIconButton("", "Copy path");
        copyButton.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(vaultPath);
            Clipboard.SetContent(package);
        };
        iconBar.Children.Add(copyButton);

        var revealButton = QuietIconButton("", "Reveal in Explorer");
        revealButton.Click += (_, _) => RevealVault();
        iconBar.Children.Add(revealButton);

        pathRow.Children.Add(iconBar);

        stack.Children.Add(pathRow);
        return Card(stack);
    }

    private void RevealVault()
    {
        try
        {
            var directory = Path.GetDirectoryName(vaultPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(vaultPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{vaultPath}\"",
                    UseShellExecute = false
                });
                return;
            }

            if (!string.IsNullOrEmpty(directory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{directory}\"",
                    UseShellExecute = false
                });
            }
        }
        catch
        {
        }
    }

    private Border BuildClipboardSection()
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(SectionHeader("Clipboard auto-clear"));

        var clipboardBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        AddClipboardItem(clipboardBox, "15 seconds", 15);
        AddClipboardItem(clipboardBox, "30 seconds", 30);
        AddClipboardItem(clipboardBox, "60 seconds", 60);
        AddClipboardItem(clipboardBox, "Never", 0);
        clipboardBox.SelectedIndex = settings.ClipboardClearSeconds switch
        {
            15 => 0,
            60 => 2,
            0 => 3,
            _ => 1
        };
        clipboardBox.SelectionChanged += (_, _) =>
        {
            if (clipboardBox.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out var seconds))
            {
                settings.ClipboardClearSeconds = seconds;
                settingsStore.Save(settings);
            }
        };
        stack.Children.Add(clipboardBox);
        return Card(stack);
    }

    private const string GithubReleasesUrl = "https://github.com/jasperdevs/shush-vault";

    private Border BuildUpdatesSection()
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(SectionHeader("Updates"));

        var version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        stack.Children.Add(new TextBlock
        {
            FontSize = 12,
            Foreground = Brush("TextFillColorSecondaryBrush"),
            Text = $"Current version {version}. Updates ship from GitHub releases."
        });

        var status = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Text = string.Empty
        };

        var checkButton = ActionButton("Check for updates");
        checkButton.Click += async (_, _) =>
        {
            checkButton.IsEnabled = false;
            try
            {
                await CheckForUpdatesAsync(status);
            }
            finally
            {
                checkButton.IsEnabled = true;
            }
        };
        stack.Children.Add(checkButton);
        stack.Children.Add(status);

        return Card(stack);
    }

    private async Task CheckForUpdatesAsync(TextBlock status)
    {
        try
        {
            var source = new global::Velopack.Sources.GithubSource(GithubReleasesUrl, null, false);
            var manager = new UpdateManager(source);
            if (!manager.IsInstalled)
            {
                status.Text = "App is not running from an installed build. Reinstall from setup.exe to enable updates.";
                return;
            }

            status.Text = "Checking GitHub releases...";
            var info = await manager.CheckForUpdatesAsync();
            if (info is null)
            {
                status.Text = "You're on the latest version.";
                return;
            }

            status.Text = $"Downloading {info.TargetFullRelease.Version}...";
            await manager.DownloadUpdatesAsync(info);

            var confirm = new ContentDialog
            {
                Title = "Install update?",
                Content = $"Version {info.TargetFullRelease.Version} is ready. Restart now to apply?",
                PrimaryButtonText = "Restart now",
                CloseButtonText = "Later",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = (Content as FrameworkElement)?.XamlRoot
            };
            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            {
                manager.ApplyUpdatesAndRestart(info.TargetFullRelease);
            }
            else
            {
                status.Text = $"Update {info.TargetFullRelease.Version} downloaded. It will install next launch.";
            }
        }
        catch (Exception ex)
        {
            status.Text = $"Update failed: {ex.Message}";
        }
    }

    private Border BuildAboutSection()
    {
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(SectionHeader("About"));

        var version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        stack.Children.Add(new TextBlock
        {
            Text = $"Shush Vault {version}",
            FontWeight = FontWeights.SemiBold
        });
        return Card(stack);
    }

    private static Border Card(UIElement child) => new()
    {
        Background = CardSurface,
        BorderBrush = CardBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16),
        Child = child
    };

    private static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeights.SemiBold
    };

    private static Brush Brush(string resourceKey)
        => (Brush)Application.Current.Resources[resourceKey];

    private static Button ActionButton(string content) => new()
    {
        Content = content,
        Padding = new Thickness(14, 9, 14, 9),
        CornerRadius = new CornerRadius(8),
        HorizontalAlignment = HorizontalAlignment.Left,
        MinWidth = 120
    };

    private static Button QuietIconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(6),
            Width = 30,
            Height = 30,
            Content = new FontIcon { Glyph = glyph, FontSize = 13 }
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static void AddClipboardItem(ComboBox comboBox, string label, int seconds)
        => comboBox.Items.Add(new ComboBoxItem { Content = label, Tag = seconds.ToString() });

    private void Resize(int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow.GetFromWindowId(windowId)?.Resize(new global::Windows.Graphics.SizeInt32(width, height));
    }
}

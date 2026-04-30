using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using ShushVault.Windows.Controls;
using WinRT.Interop;

namespace ShushVault.Windows;

public sealed class SettingsWindow : Window
{
    private readonly Action<int> onClipboardSecondsChanged;

    public SettingsWindow(string vaultPath, int clipboardSeconds, Action<int> onClipboardSecondsChanged)
    {
        this.onClipboardSecondsChanged = onClipboardSecondsChanged;
        ExtendsContentIntoTitleBar = true;
        Title = "Shush Vault Settings";
        var (root, titleBar) = BuildContent(vaultPath, clipboardSeconds);
        Content = root;
        SetTitleBar(titleBar);
        Resize(520, 380);
    }

    private (UIElement Root, UIElement TitleBar) BuildContent(string vaultPath, int clipboardSeconds)
    {
        var root = new Grid
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleBar = new WindowTitleBar { Title = "settings" };
        root.Children.Add(titleBar);

        var content = new StackPanel
        {
            Padding = new Thickness(22, 18, 22, 22),
            Spacing = 14
        };
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        content.Children.Add(new TextBlock
        {
            Text = "Settings",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        content.Children.Add(new TextBlock
        {
            Text = "Vault",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = vaultPath,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        content.Children.Add(new TextBlock
        {
            Text = "Passphrase is optional. By default, this Windows device stores a generated local key in Credential Manager and the vault file stays encrypted on disk.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        content.Children.Add(new TextBlock
        {
            Text = "Clipboard",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var clipboardBox = new ComboBox
        {
            Header = "Auto-clear copied values",
            Width = 220
        };
        AddClipboardItem(clipboardBox, "15 seconds", 15);
        AddClipboardItem(clipboardBox, "30 seconds", 30);
        AddClipboardItem(clipboardBox, "60 seconds", 60);
        AddClipboardItem(clipboardBox, "Never", 0);
        clipboardBox.SelectedIndex = clipboardSeconds switch
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
                this.onClipboardSecondsChanged(seconds);
            }
        };
        content.Children.Add(clipboardBox);

        var close = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        close.Click += (_, _) => Close();
        content.Children.Add(close);

        return (root, titleBar);
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

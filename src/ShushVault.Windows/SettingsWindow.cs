using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace ShushVault.Windows;

public sealed class SettingsWindow : Window
{
    private readonly Action<int> onClipboardSecondsChanged;

    public SettingsWindow(string vaultPath, int clipboardSeconds, Action<int> onClipboardSecondsChanged)
    {
        this.onClipboardSecondsChanged = onClipboardSecondsChanged;
        Title = "Shush Vault Settings";
        Content = BuildContent(vaultPath, clipboardSeconds);
        Resize(560, 420);
    }

    private UIElement BuildContent(string vaultPath, int clipboardSeconds)
    {
        var root = new StackPanel
        {
            Padding = new Thickness(22),
            Spacing = 16
        };

        root.Children.Add(new TextBlock
        {
            Text = "Settings",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        root.Children.Add(new TextBlock
        {
            Text = "Vault",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        root.Children.Add(new TextBlock
        {
            Text = vaultPath,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        root.Children.Add(new TextBlock
        {
            Text = "Passphrase is optional. By default, this Windows device stores a generated local key in Credential Manager and the vault file stays encrypted on disk.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        root.Children.Add(new TextBlock
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
        root.Children.Add(clipboardBox);

        var close = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        close.Click += (_, _) => Close();
        root.Children.Add(close);

        return root;
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

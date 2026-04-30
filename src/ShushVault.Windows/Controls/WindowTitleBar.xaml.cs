using Microsoft.UI.Xaml.Controls;

namespace ShushVault.Windows.Controls;

public sealed partial class WindowTitleBar : UserControl
{
    public WindowTitleBar()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => TitleTextBlock.Text;
        set => TitleTextBlock.Text = value;
    }
}

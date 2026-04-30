using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ShushVault.Windows.Controls;

public sealed partial class WindowTitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(WindowTitleBar),
        new PropertyMetadata(string.Empty, OnTitleChanged));

    public WindowTitleBar()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WindowTitleBar bar)
        {
            bar.TitleText.Text = e.NewValue as string ?? string.Empty;
        }
    }
}

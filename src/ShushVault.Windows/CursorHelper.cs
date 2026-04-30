using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ShushVault.Windows;

internal static class CursorHelper
{
    private static readonly PropertyInfo? ProtectedCursorProperty =
        typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void ApplyHand(UIElement element) => ApplyShape(element, InputSystemCursorShape.Hand);
    public static void ApplyArrow(UIElement element) => ApplyShape(element, InputSystemCursorShape.Arrow);
    public static void ApplyText(UIElement element) => ApplyShape(element, InputSystemCursorShape.IBeam);

    public static void ApplyToTree(DependencyObject root)
    {
        Apply(root);
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            ApplyToTree(VisualTreeHelper.GetChild(root, i));
        }
    }

    private static void Apply(DependencyObject obj)
    {
        switch (obj)
        {
            case Button:
            case ComboBox:
            case ComboBoxItem:
            case ToggleSwitch:
            case ToggleButton:
            case HyperlinkButton:
            case ListViewItem:
                ApplyShape((UIElement)obj, InputSystemCursorShape.Hand);
                break;
            case TextBox:
            case PasswordBox:
            case RichEditBox:
                ApplyShape((UIElement)obj, InputSystemCursorShape.IBeam);
                break;
        }
    }

    private static void ApplyShape(UIElement element, InputSystemCursorShape shape)
    {
        try
        {
            ProtectedCursorProperty?.SetValue(element, InputSystemCursor.Create(shape));
        }
        catch
        {
        }
    }
}

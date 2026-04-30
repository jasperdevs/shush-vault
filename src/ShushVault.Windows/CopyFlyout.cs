using System.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace ShushVault.Windows;

internal static class CopyFlyout
{
    public static void Show(FrameworkElement anchor)
    {
        try
        {
            var label = new TextBlock
            {
                Text = "Copied",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
            };
            var flyout = new Flyout
            {
                Content = label,
                Placement = FlyoutPlacementMode.Top,
                ShowMode = FlyoutShowMode.Transient,
                OverlayInputPassThroughElement = anchor,
            };
            flyout.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Top });

            var timer = anchor.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(900);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                flyout.Hide();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Copied flyout failed: {ex.Message}");
        }
    }
}

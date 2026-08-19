using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace PetOverlay;

// Win32 and WinForms report cursor/screen coordinates in physical pixels, but every
// WPF Left/Top/Width they end up compared against is in device-independent units.
// On a display scaled above 100% the raw values overshoot by exactly the DPI factor,
// so anything positioned from them drifts - or lands off-screen entirely.
internal static class DpiUtil
{
    internal static Point PhysicalToDiu(Visual visual, Point physical)
    {
        // Per-monitor scale when the visual is on screen; a global fallback otherwise,
        // since PresentationSource is null until the window has actually been shown.
        var source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget is { } target)
        {
            return target.TransformFromDevice.Transform(physical);
        }

        var (scaleX, scaleY) = GlobalScale();
        return new Point(physical.X / scaleX, physical.Y / scaleY);
    }

    private static (double X, double Y) GlobalScale()
    {
        var screen = Forms.Screen.PrimaryScreen;
        if (screen is null) return (1.0, 1.0);

        var scaleX = screen.Bounds.Width / SystemParameters.PrimaryScreenWidth;
        var scaleY = screen.Bounds.Height / SystemParameters.PrimaryScreenHeight;
        return (scaleX > 0 ? scaleX : 1.0, scaleY > 0 ? scaleY : 1.0);
    }
}

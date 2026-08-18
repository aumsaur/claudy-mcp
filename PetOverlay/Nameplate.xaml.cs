using System.Windows;

namespace PetOverlay;

public partial class Nameplate : Window
{
    private const int MaxChars = 20;

    private Point _center;

    public Nameplate(string fullName, Point initialCenter)
    {
        InitializeComponent();

        NameText.Text = fullName.Length > MaxChars ? fullName[..(MaxChars - 1)] + "…" : fullName;
        RootBorder.ToolTip = fullName;

        _center = initialCenter;
        // A SizeToContent + AllowsTransparency window that's first shown with Left/Top
        // still unset (NaN) doesn't shrink-to-fit correctly - it sticks at whatever
        // fallback size WPF picks and never corrects itself afterward, even once
        // ContentRendered fires with the right measurements. Giving it *some* real
        // position up front (even before ActualWidth is known) avoids that path.
        ApplyCenter();
        ContentRendered += (_, _) => ApplyCenter();
    }

    public void MoveTo(Point center)
    {
        _center = center;
        if (ActualWidth > 0) ApplyCenter();
    }

    private void ApplyCenter()
    {
        Left = _center.X - (ActualWidth / 2);
        Top = _center.Y;
    }
}

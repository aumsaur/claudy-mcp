using System.Windows;

namespace PetOverlay;

public partial class ToyMarker : Window
{
    private Point _center;

    public ToyMarker(string emoji, Point initialCenter)
    {
        InitializeComponent();
        EmojiText.Text = emoji;
        _center = initialCenter;
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
        Top = _center.Y - (ActualHeight / 2);
    }
}

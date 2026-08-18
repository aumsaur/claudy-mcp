using System.Windows;
using System.Windows.Controls;

namespace PetOverlay;

public partial class AimLine : Window
{
    public AimLine()
    {
        InitializeComponent();

        // Spans the whole virtual desktop, not just the primary screen - the
        // pet (and now the ball, placed by click) can live on a secondary
        // monitor, and a primary-only canvas would silently clip the whole
        // trajectory preview away over there.
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        RootCanvas.Width = Width;
        RootCanvas.Height = Height;
    }

    public void UpdateLine(Point origin, Point current)
    {
        double ox = origin.X - Left;
        double oy = origin.Y - Top;
        double cx = current.X - Left;
        double cy = current.Y - Top;

        AimLineShape.X1 = ox;
        AimLineShape.Y1 = oy;
        AimLineShape.X2 = cx;
        AimLineShape.Y2 = cy;

        Canvas.SetLeft(PullDot, cx - 6);
        Canvas.SetTop(PullDot, cy - 6);
    }
}

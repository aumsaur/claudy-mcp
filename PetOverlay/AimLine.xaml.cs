using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace PetOverlay;

public partial class AimLine : Window
{
    public AimLine()
    {
        InitializeComponent();

        var working = Forms.Screen.PrimaryScreen!.WorkingArea;
        Left = working.Left;
        Top = working.Top;
        Width = working.Width;
        Height = working.Height;
        RootCanvas.Width = working.Width;
        RootCanvas.Height = working.Height;
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

using System.Windows;
using System.Windows.Media.Animation;

namespace PetOverlay;

// A brief, self-closing "poke" flourish - pops in near a target, holds, fades out.
// Purely decorative and click-through, so it never steals focus/input.
public partial class GhostHand : Window
{
    public GhostHand(Point center)
    {
        InitializeComponent();

        ContentRendered += (_, _) =>
        {
            Left = center.X - (ActualWidth / 2);
            Top = center.Y - (ActualHeight / 2);

            var pop = new DoubleAnimation(0.4, 1.05, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut },
            };
            HandScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pop);
            HandScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pop);

            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250))
            {
                BeginTime = TimeSpan.FromMilliseconds(650),
            };
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        };
    }
}

param(
    [Parameter(Mandatory = $true)][string]$RequestPath,
    [Parameter(Mandatory = $true)][string]$ResponsePath
)

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Windows.Forms

$req = Get-Content -Raw -Path $RequestPath -Encoding UTF8 | ConvertFrom-Json

$question = [string]$req.question
$kind = [string]$req.kind
$options = $req.options
$placeholder = $req.placeholder
$timeoutSeconds = if ($req.timeoutSeconds) { [int]$req.timeoutSeconds } else { 300 }

$script:response = @{ status = "cancelled"; answer = $null }

function Write-Response($obj) {
    ($obj | ConvertTo-Json -Compress) | Set-Content -Path $ResponsePath -Encoding UTF8
}

$window = New-Object System.Windows.Window
$window.Title = "Claude"
$window.WindowStyle = 'None'
$window.AllowsTransparency = $true
$window.Background = [System.Windows.Media.Brushes]::Transparent
$window.Topmost = $true
$window.ShowInTaskbar = $false
$window.SizeToContent = 'WidthAndHeight'
$window.ResizeMode = 'NoResize'
$window.MaxWidth = 380

$border = New-Object System.Windows.Controls.Border
$border.Background = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(30, 30, 34))
$border.CornerRadius = New-Object System.Windows.CornerRadius(12)
$border.Padding = New-Object System.Windows.Thickness(16)
$border.BorderBrush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(70, 70, 78))
$border.BorderThickness = New-Object System.Windows.Thickness(1)

$stack = New-Object System.Windows.Controls.StackPanel
$border.Child = $stack

$title = New-Object System.Windows.Controls.TextBlock
$title.Text = "Claude asks"
$title.Foreground = [System.Windows.Media.Brushes]::Gray
$title.FontSize = 11
$title.Margin = New-Object System.Windows.Thickness(0, 0, 0, 6)
$stack.Children.Add($title) | Out-Null

$qText = New-Object System.Windows.Controls.TextBlock
$qText.Text = $question
$qText.Foreground = [System.Windows.Media.Brushes]::White
$qText.FontSize = 14
$qText.TextWrapping = 'Wrap'
$qText.Margin = New-Object System.Windows.Thickness(0, 0, 0, 12)
$stack.Children.Add($qText) | Out-Null

function New-Btn($text, $primary) {
    $b = New-Object System.Windows.Controls.Button
    $b.Content = $text
    $b.Padding = New-Object System.Windows.Thickness(14, 6, 14, 6)
    $b.Margin = New-Object System.Windows.Thickness(0, 0, 8, 0)
    if ($primary) {
        $b.Background = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(60, 120, 220))
        $b.Foreground = [System.Windows.Media.Brushes]::White
    }
    else {
        $b.Background = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(55, 55, 60))
        $b.Foreground = [System.Windows.Media.Brushes]::White
    }
    $b.BorderThickness = New-Object System.Windows.Thickness(0)
    return $b
}

switch ($kind) {
    "choice" {
        foreach ($opt in $options) {
            $b = New-Btn $opt $false
            $b.HorizontalContentAlignment = 'Left'
            $b.HorizontalAlignment = 'Stretch'
            $b.Margin = New-Object System.Windows.Thickness(0, 0, 0, 6)
            $b.Tag = $opt
            $b.Add_Click({
                    $script:response = @{ status = "answered"; answer = $this.Tag }
                    $window.Close()
                })
            $stack.Children.Add($b) | Out-Null
        }
    }
    "text" {
        if ($placeholder) {
            $hint = New-Object System.Windows.Controls.TextBlock
            $hint.Text = $placeholder
            $hint.Foreground = [System.Windows.Media.Brushes]::Gray
            $hint.FontSize = 11
            $hint.Margin = New-Object System.Windows.Thickness(0, -6, 0, 6)
            $hint.TextWrapping = 'Wrap'
            $stack.Children.Add($hint) | Out-Null
        }

        $tb = New-Object System.Windows.Controls.TextBox
        $tb.Padding = New-Object System.Windows.Thickness(6)
        $tb.Margin = New-Object System.Windows.Thickness(0, 0, 0, 10)
        $tb.MinWidth = 260
        $tb.AcceptsReturn = $false
        $stack.Children.Add($tb) | Out-Null

        $tb.Add_KeyDown({
                if ($_.Key -eq 'Return') {
                    $script:response = @{ status = "answered"; answer = $tb.Text }
                    $window.Close()
                }
            })

        $btnPanel = New-Object System.Windows.Controls.StackPanel
        $btnPanel.Orientation = 'Horizontal'
        $btnPanel.HorizontalAlignment = 'Right'

        $cancelBtn = New-Btn "Cancel" $false
        $cancelBtn.Margin = New-Object System.Windows.Thickness(0, 0, 8, 0)
        $cancelBtn.Add_Click({
                $script:response = @{ status = "cancelled"; answer = $null }
                $window.Close()
            })

        $submitBtn = New-Btn "Submit" $true
        $submitBtn.Margin = New-Object System.Windows.Thickness(0)
        $submitBtn.Add_Click({
                $script:response = @{ status = "answered"; answer = $tb.Text }
                $window.Close()
            })

        $btnPanel.Children.Add($cancelBtn) | Out-Null
        $btnPanel.Children.Add($submitBtn) | Out-Null
        $stack.Children.Add($btnPanel) | Out-Null

        $window.Add_ContentRendered({ $tb.Focus() | Out-Null }.GetNewClosure())
    }
    default {
        # yesno
        $btnPanel = New-Object System.Windows.Controls.StackPanel
        $btnPanel.Orientation = 'Horizontal'
        $btnPanel.HorizontalAlignment = 'Right'

        $noBtn = New-Btn "No" $false
        $noBtn.Add_Click({
                $script:response = @{ status = "answered"; answer = "no" }
                $window.Close()
            })

        $yesBtn = New-Btn "Yes" $true
        $yesBtn.Margin = New-Object System.Windows.Thickness(0)
        $yesBtn.Add_Click({
                $script:response = @{ status = "answered"; answer = "yes" }
                $window.Close()
            })

        $btnPanel.Children.Add($noBtn) | Out-Null
        $btnPanel.Children.Add($yesBtn) | Out-Null
        $stack.Children.Add($btnPanel) | Out-Null
    }
}

$window.Content = $border

$window.Add_ContentRendered({
        # WorkingArea is in physical pixels but WPF's Left/Top are device-independent
        # units, so on any display scaled above 100% the raw numbers put the window
        # past the corner and off-screen entirely. Divide by the DPI scale first.
        $wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $scaleX = 1.0
        $scaleY = 1.0
        $src = [System.Windows.PresentationSource]::FromVisual($window)
        if ($src -and $src.CompositionTarget) {
            $m = $src.CompositionTarget.TransformToDevice
            if ($m.M11 -gt 0) { $scaleX = $m.M11 }
            if ($m.M22 -gt 0) { $scaleY = $m.M22 }
        }
        $window.Left = ($wa.Right / $scaleX) - $window.ActualWidth - 20
        $window.Top = ($wa.Bottom / $scaleY) - $window.ActualHeight - 20
    })

$window.Add_KeyDown({
        if ($_.Key -eq 'Escape') {
            $script:response = @{ status = "cancelled"; answer = $null }
            $window.Close()
        }
    })

$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromSeconds($timeoutSeconds)
$timer.Add_Tick({
        $timer.Stop()
        $script:response = @{ status = "timeout"; answer = $null }
        $window.Close()
    })
$timer.Start()

$window.Add_Closed({ $timer.Stop() })

[System.Media.SystemSounds]::Asterisk.Play()

$window.ShowDialog() | Out-Null

Write-Response $script:response

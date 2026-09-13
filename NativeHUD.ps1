Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Windows.Forms

$bullet = [char]0x2022

$SPOOL_DIR     = "$HOME\.notch"
$PENDING_DIR   = "$SPOOL_DIR\pending"
$DECISIONS_DIR = "$SPOOL_DIR\decisions"
$SESSIONS_DIR  = "$SPOOL_DIR\sessions"

foreach ($d in @($SPOOL_DIR, $PENDING_DIR, $DECISIONS_DIR, $SESSIONS_DIR)) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force -ErrorAction SilentlyContinue | Out-Null }
}

# Write PID file on launch for deterministic micro-shim HUD detection
$pidData = [ordered]@{
    pid       = $PID
    startTime = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
} | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText("$SPOOL_DIR\hud.pid", $pidData, [System.Text.Encoding]::UTF8)

$win32Code = @"
using System;
using System.Runtime.InteropServices;

public class Win32HUD {
    public enum AccentState {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AccentPolicy {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowCompositionAttributeData {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@
if (-not ([System.Management.Automation.PSTypeName]'Win32HUD').Type) {
    Add-Type -TypeDefinition $win32Code
}

# Window size matches pill size exactly so NO invisible transparent overlay blocks clicks/typing
[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Notch Dynamic Island HUD" Height="34" Width="230"
        WindowStyle="None" AllowsTransparency="True" Background="{x:Null}"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        ShowActivated="False" Focusable="False">
    <Window.Resources>
        <Style TargetType="Button">
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="border" Background="{TemplateBinding Background}" CornerRadius="9" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="border" Property="Opacity" Value="0.85"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
    <Grid VerticalAlignment="Top" HorizontalAlignment="Center" Focusable="False">
        <Border x:Name="HudBorder" CornerRadius="0,0,16,16" Background="#F00D0D11" BorderBrush="#30D158" BorderThickness="1,0,1,1" Height="28" Width="210" VerticalAlignment="Top" HorizontalAlignment="Center" ClipToBounds="True" Focusable="False">
            <Border.Effect>
                <DropShadowEffect x:Name="HudShadow" Color="#30D158" BlurRadius="12" ShadowDepth="0" Opacity="0.8"/>
            </Border.Effect>
            <Grid Focusable="False">
                <Grid.RowDefinitions>
                    <RowDefinition Height="28"/>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>
                
                <!-- Status Header (Always Visible, Apple Dynamic Island Layout) -->
                <Grid x:Name="HeaderGrid" Grid.Row="0" Cursor="Hand" Background="Transparent" Focusable="False">
                    <!-- Left: Status Label (Clean Apple Typography) -->
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="14,0,0,0">
                        <TextBlock x:Name="LblStatus" Text="IDLE" Foreground="#30D158" FontWeight="SemiBold" FontSize="10.5" FontFamily="Segoe UI Variable Display, Segoe UI, -apple-system, sans-serif" VerticalAlignment="Center"/>
                    </StackPanel>
                    
                    <!-- Right: Tactile Apple Capsule Toggle Badge (No Lightning Symbol) -->
                    <Border x:Name="BtnAutoToggle" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,8,0" CornerRadius="7" Background="#14FFFFFF" BorderBrush="#26FFFFFF" BorderThickness="1" Padding="6,2" Cursor="Hand" Focusable="False">
                        <TextBlock x:Name="LblAutoBadge" Text="AUTO" Foreground="#77FFFFFF" FontSize="8.5" FontWeight="SemiBold" FontFamily="Segoe UI Variable Display, Segoe UI, -apple-system, sans-serif" VerticalAlignment="Center"/>
                    </Border>
                </Grid>

                <!-- Action / Pending Details -->
                <Grid x:Name="PnlPending" Grid.Row="1" Visibility="Collapsed" Margin="14,4,14,8" Focusable="False">
                    <TextBlock x:Name="LblCommand" Text="" Foreground="#FFFFFFFF" FontSize="12" FontWeight="Normal" HorizontalAlignment="Center" TextWrapping="Wrap" TextAlignment="Center" FontFamily="Segoe UI Variable Text, Segoe UI, sans-serif" MaxHeight="60"/>
                </Grid>

                <!-- Multi-Session Details -->
                <Grid x:Name="PnlSessions" Grid.Row="1" Visibility="Collapsed" Margin="14,4,14,8" Focusable="False">
                    <ScrollViewer VerticalScrollBarVisibility="Auto" MaxHeight="75" Focusable="False">
                        <TextBlock x:Name="LblSessionSummary" Text="No active sessions" Foreground="#CCFFFFFF" FontSize="11" FontFamily="Segoe UI Variable Text, Segoe UI, sans-serif" TextWrapping="Wrap"/>
                    </ScrollViewer>
                </Grid>

                <!-- Approval Buttons -->
                <StackPanel x:Name="BtnPanel" Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,0,0,8" Visibility="Collapsed" Focusable="False">
                    <Button x:Name="BtnApprove" Content="Approve (Shift+↵)" Width="130" Height="26" Margin="0,0,8,0" Background="#0A84FF" Foreground="White" FontWeight="SemiBold" FontSize="10.5" BorderThickness="0"/>
                    <Button x:Name="BtnReject" Content="Reject" Width="70" Height="26" Background="#26FFFFFF" Foreground="#FFEEEE" FontWeight="SemiBold" FontSize="10.5" BorderThickness="0"/>
                </StackPanel>
            </Grid>
        </Border>
    </Grid>
</Window>
"@

$reader = (New-Object System.Xml.XmlNodeReader $xaml)
$window = [Windows.Markup.XamlReader]::Load($reader)

$lblStatus         = $window.FindName("LblStatus")
$lblCommand        = $window.FindName("LblCommand")
$btnPanel          = $window.FindName("BtnPanel")
$btnApprove        = $window.FindName("BtnApprove")
$btnReject         = $window.FindName("BtnReject")
$hudBorder         = $window.FindName("HudBorder")
$hudShadow         = $window.FindName("HudShadow")
$headerGrid        = $window.FindName("HeaderGrid")
$pnlPending        = $window.FindName("PnlPending")
$pnlSessions       = $window.FindName("PnlSessions")
$lblSessionSummary = $window.FindName("LblSessionSummary")
$btnAutoToggle     = $window.FindName("BtnAutoToggle")
$lblAutoBadge      = $window.FindName("LblAutoBadge")

$global:activeConvId        = $null
$global:isManuallyExpanded  = $false
$global:currentExpandedMode = "Collapsed"
$global:hotkeyRegistered    = $false
$global:sessionCache        = @{}
$global:sessionNamesCache   = @{}
$global:pendingIndex        = 0
$global:isPulsing           = $false
$global:isAutoMode          = Test-Path "$SPOOL_DIR\auto_mode.flag"

# Pre-cached Brushes & Colors (Apple Human Interface Guidelines Palette)
$brushConv = New-Object System.Windows.Media.BrushConverter
$colorConv = [System.Windows.Media.ColorConverter]

$brushGreen  = $brushConv.ConvertFromString("#30D158")
$brushBlue   = $brushConv.ConvertFromString("#0A84FF")
$brushRed    = $brushConv.ConvertFromString("#FF453A")
$brushPurple = $brushConv.ConvertFromString("#BF5AF2")
$brushOrange = $brushConv.ConvertFromString("#FF9F0A")
$brushIndigo = $brushConv.ConvertFromString("#5E5CE6")

$brushIndigoTranslucent = $brushConv.ConvertFromString("#2E5E5CE6")
$brushDimBackground     = $brushConv.ConvertFromString("#14FFFFFF")
$brushDimBorder         = $brushConv.ConvertFromString("#26FFFFFF")
$brushDimText           = $brushConv.ConvertFromString("#77FFFFFF")

$colorGreen  = $colorConv::ConvertFromString("#30D158")
$colorBlue   = $colorConv::ConvertFromString("#0A84FF")
$colorRed    = $colorConv::ConvertFromString("#FF453A")
$colorPurple = $colorConv::ConvertFromString("#BF5AF2")
$colorOrange = $colorConv::ConvertFromString("#FF9F0A")
$colorIndigo = $colorConv::ConvertFromString("#5E5CE6")

# Setup Initial Centering & Dynamic Drag Anchoring
$screenWidth = [System.Windows.SystemParameters]::PrimaryScreenWidth
$global:pillCenterX = $screenWidth / 2
$window.WindowStartupLocation = "Manual"
$window.Left = $global:pillCenterX - (230 / 2)
$window.Top = 0

$window.add_LocationChanged({
    if ($window.Top -ne 0) { $window.Top = 0 }
    if ($window.Width -gt 0) {
        $global:pillCenterX = $window.Left + ($window.Width / 2)
    }
})

# Dimensions
$contractedW = 210
$contractedH = 28
$windowContractedW = 230
$windowContractedH = 34

$pendingW = 400
$pendingH = 138
$windowPendingW = 420
$windowPendingH = 150

$sessionsW = 360
$sessionsH = 120
$windowSessionsW = 380
$windowSessionsH = 135

# Expand Animations (Pending)
$expandWidthPending = New-Object System.Windows.Media.Animation.DoubleAnimation
$expandWidthPending.To = $pendingW
$expandWidthPending.Duration = [TimeSpan]::FromMilliseconds(240)
$expandWidthPending.EasingFunction = New-Object System.Windows.Media.Animation.CubicEase
$expandWidthPending.EasingFunction.EasingMode = [System.Windows.Media.Animation.EasingMode]::EaseOut

$expandHeightPending = New-Object System.Windows.Media.Animation.DoubleAnimation
$expandHeightPending.To = $pendingH
$expandHeightPending.Duration = [TimeSpan]::FromMilliseconds(240)
$expandHeightPending.EasingFunction = New-Object System.Windows.Media.Animation.CubicEase
$expandHeightPending.EasingFunction.EasingMode = [System.Windows.Media.Animation.EasingMode]::EaseOut

# Expand Animations (Sessions)
$expandWidthSessions = New-Object System.Windows.Media.Animation.DoubleAnimation
$expandWidthSessions.To = $sessionsW
$expandWidthSessions.Duration = [TimeSpan]::FromMilliseconds(240)
$expandWidthSessions.EasingFunction = New-Object System.Windows.Media.Animation.CubicEase
$expandWidthSessions.EasingFunction.EasingMode = [System.Windows.Media.Animation.EasingMode]::EaseOut

$expandHeightSessions = New-Object System.Windows.Media.Animation.DoubleAnimation
$expandHeightSessions.To = $sessionsH
$expandHeightSessions.Duration = [TimeSpan]::FromMilliseconds(240)
$expandHeightSessions.EasingFunction = New-Object System.Windows.Media.Animation.CubicEase
$expandHeightSessions.EasingFunction.EasingMode = [System.Windows.Media.Animation.EasingMode]::EaseOut

# Collapse Animations
$collapseWidth = New-Object System.Windows.Media.Animation.DoubleAnimation
$collapseWidth.To = $contractedW
$collapseWidth.Duration = [TimeSpan]::FromMilliseconds(220)
$collapseWidth.EasingFunction = New-Object System.Windows.Media.Animation.CubicEase
$collapseWidth.EasingFunction.EasingMode = [System.Windows.Media.Animation.EasingMode]::EaseOut

$collapseHeight = New-Object System.Windows.Media.Animation.DoubleAnimation
$collapseHeight.To = $contractedH
$collapseHeight.Duration = [TimeSpan]::FromMilliseconds(220)
$collapseHeight.EasingFunction = New-Object System.Windows.Media.Animation.CubicEase
$collapseHeight.EasingFunction.EasingMode = [System.Windows.Media.Animation.EasingMode]::EaseOut

# Light Splash Animation on Contraction
$splashBlurAnim = New-Object System.Windows.Media.Animation.DoubleAnimation
$splashBlurAnim.From = 12
$splashBlurAnim.To = 30
$splashBlurAnim.Duration = [TimeSpan]::FromMilliseconds(160)
$splashBlurAnim.AutoReverse = $true
$splashBlurAnim.EasingFunction = New-Object System.Windows.Media.Animation.QuadraticEase
$splashBlurAnim.EasingFunction.EasingMode = [System.Windows.Media.Animation.EasingMode]::EaseOut

$splashOpacityAnim = New-Object System.Windows.Media.Animation.DoubleAnimation
$splashOpacityAnim.From = 0.75
$splashOpacityAnim.To = 1.0
$splashOpacityAnim.Duration = [TimeSpan]::FromMilliseconds(160)
$splashOpacityAnim.AutoReverse = $true
$splashOpacityAnim.EasingFunction = New-Object System.Windows.Media.Animation.QuadraticEase
$splashOpacityAnim.EasingFunction.EasingMode = [System.Windows.Media.Animation.EasingMode]::EaseOut

# Apple Dynamic Island Edge LED Perimeter Breathing Glow (Transitions.dev Motion)
$edgePulseOpacityAnim = New-Object System.Windows.Media.Animation.DoubleAnimation
$edgePulseOpacityAnim.From = 0.45
$edgePulseOpacityAnim.To = 0.95
$edgePulseOpacityAnim.Duration = [TimeSpan]::FromMilliseconds(1200)
$edgePulseOpacityAnim.AutoReverse = $true
$edgePulseOpacityAnim.RepeatBehavior = [System.Windows.Media.Animation.RepeatBehavior]::Forever
$sineEase = New-Object System.Windows.Media.Animation.SineEase
$sineEase.EasingMode = [System.Windows.Media.Animation.EasingMode]::EaseInOut
$edgePulseOpacityAnim.EasingFunction = $sineEase

$edgePulseBlurAnim = New-Object System.Windows.Media.Animation.DoubleAnimation
$edgePulseBlurAnim.From = 10
$edgePulseBlurAnim.To = 20
$edgePulseBlurAnim.Duration = [TimeSpan]::FromMilliseconds(1200)
$edgePulseBlurAnim.AutoReverse = $true
$edgePulseBlurAnim.RepeatBehavior = [System.Windows.Media.Animation.RepeatBehavior]::Forever
$edgePulseBlurAnim.EasingFunction = $sineEase

function Trigger-LightSplash {
    $hudShadow.BeginAnimation([System.Windows.Media.Effects.DropShadowEffect]::BlurRadiusProperty, $splashBlurAnim)
    $hudShadow.BeginAnimation([System.Windows.Media.Effects.DropShadowEffect]::OpacityProperty, $splashOpacityAnim)
}

function Update-AutoBadgeVisuals {
    if ($global:isAutoMode) {
        $btnAutoToggle.Background  = $brushIndigoTranslucent
        $btnAutoToggle.BorderBrush = $brushIndigo
        $lblAutoBadge.Foreground   = $brushIndigo
        $lblAutoBadge.Text         = "AUTO ON"
    } else {
        $btnAutoToggle.Background  = $brushDimBackground
        $btnAutoToggle.BorderBrush = $brushDimBorder
        $lblAutoBadge.Foreground   = $brushDimText
        $lblAutoBadge.Text         = "AUTO"
    }
}

function Toggle-AutoMode {
    $global:isAutoMode = -not $global:isAutoMode
    $autoFlagFile = "$SPOOL_DIR\auto_mode.flag"
    
    if ($global:isAutoMode) {
        $flagData = [ordered]@{
            active    = $true
            timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
            enabledBy = "HUD_UI_Badge"
        } | ConvertTo-Json -Compress
        [System.IO.File]::WriteAllText($autoFlagFile, $flagData, [System.Text.Encoding]::UTF8)
    } else {
        if (Test-Path $autoFlagFile) {
            [System.IO.File]::Delete($autoFlagFile)
        }
    }
    
    Update-AutoBadgeVisuals
    Check-State
}

$btnAutoToggle.add_MouseLeftButtonDown({
    param($sender, $e)
    $e.Handled = $true
    Toggle-AutoMode
})

function Approve-CurrentPending {
    if ($global:activeConvId) {
        $conv = $global:activeConvId
        $global:activeConvId = $null
        
        $decFile = "$DECISIONS_DIR\$conv.json"
        $pendFile = "$PENDING_DIR\$conv.json"
        
        [System.IO.File]::WriteAllText($decFile, '{"decision":"allow"}', [System.Text.Encoding]::UTF8)
        if (Test-Path $pendFile) { [System.IO.File]::Delete($pendFile) }
        
        $global:pendingIndex = 0
        Check-State
    }
}

function Reject-CurrentPending {
    if ($global:activeConvId) {
        $conv = $global:activeConvId
        $global:activeConvId = $null
        
        $decFile = "$DECISIONS_DIR\$conv.json"
        $pendFile = "$PENDING_DIR\$conv.json"
        
        [System.IO.File]::WriteAllText($decFile, '{"decision":"deny"}', [System.Text.Encoding]::UTF8)
        if (Test-Path $pendFile) { [System.IO.File]::Delete($pendFile) }
        
        $global:pendingIndex = 0
        Check-State
    }
}

function Enable-AcrylicBackdrop {
    param([IntPtr]$hWnd)
    try {
        $accent = New-Object Win32HUD+AccentPolicy
        $accent.AccentState = [Win32HUD+AccentState]::ACCENT_ENABLE_ACRYLICBLURBEHIND
        $accent.AccentFlags = 2
        $accent.GradientColor = [uint32]0xCC101014 # 80% opacity dark acrylic tint
        
        $accentSize = [System.Runtime.InteropServices.Marshal]::SizeOf($accent)
        $accentPtr = [System.Runtime.InteropServices.Marshal]::AllocHGlobal($accentSize)
        [System.Runtime.InteropServices.Marshal]::StructureToPtr($accent, $accentPtr, $false)
        
        $data = New-Object Win32HUD+WindowCompositionAttributeData
        $data.Attribute = 19 # WCA_ACCENT_POLICY
        $data.Data = $accentPtr
        $data.SizeOfData = $accentSize
        
        [Win32HUD]::SetWindowCompositionAttribute($hWnd, [ref]$data) | Out-Null
        [System.Runtime.InteropServices.Marshal]::FreeHGlobal($accentPtr)
    } catch { }
}

# Win32 Initialization: Apply WS_EX_TOOLWINDOW and WS_EX_NOACTIVATE
$global:wndProcHook = $null
$window.add_SourceInitialized({
    $helper = New-Object System.Windows.Interop.WindowInteropHelper($window)
    $global:hwnd = $helper.Handle
    
    $GWL_EXSTYLE       = -20
    $WS_EX_TOOLWINDOW  = 0x00000080
    $WS_EX_NOACTIVATE  = 0x08000000
    
    $exStyle = [Win32HUD]::GetWindowLong($global:hwnd, $GWL_EXSTYLE)
    # Applying WS_EX_NOACTIVATE guarantees the window NEVER steals focus or interrupts keyboard typing!
    [Win32HUD]::SetWindowLong($global:hwnd, $GWL_EXSTYLE, $exStyle -bor $WS_EX_TOOLWINDOW -bor $WS_EX_NOACTIVATE) | Out-Null
    
    # Apply DWM Acrylic backdrop blur for true translucent hardware glass
    Enable-AcrylicBackdrop $global:hwnd
    
    # Strongly-referenced HwndSourceHook delegate to prevent GC
    $global:wndProcHook = [System.Windows.Interop.HwndSourceHook]{
        param([IntPtr]$hwnd, [int]$msg, [IntPtr]$wParam, [IntPtr]$lParam, [ref]$handled)
        if ($msg -eq 0x0312 -and $wParam.ToInt32() -eq 1) {
            if ($global:currentExpandedMode -eq "Pending" -and $global:activeConvId) {
                Approve-CurrentPending
                $handled = $true
            }
        }
        return [IntPtr]::Zero
    }
    
    $source = [System.Windows.Interop.HwndSource]::FromHwnd($global:hwnd)
    $source.AddHook($global:wndProcHook)
})

function Set-Expanded {
    param([string]$mode)
    if ($global:currentExpandedMode -eq $mode) { return }
    $prevMode = $global:currentExpandedMode
    $global:currentExpandedMode = $mode

    if ($mode -eq "Pending") {
        # Resize window to fit expanded state
        $window.Width  = $windowPendingW
        $window.Height = $windowPendingH
        $window.Left   = [Math]::Max(10, [Math]::Min($global:pillCenterX - ($windowPendingW / 2), $screenWidth - $windowPendingW - 10))
        
        $pnlPending.Visibility  = "Visible"
        $pnlSessions.Visibility = "Collapsed"
        $btnPanel.Visibility    = "Visible"
        $hudBorder.CornerRadius = New-Object System.Windows.CornerRadius(0, 0, 18, 18)
        $hudBorder.BeginAnimation([System.Windows.Controls.Border]::WidthProperty, $expandWidthPending)
        $hudBorder.BeginAnimation([System.Windows.Controls.Border]::HeightProperty, $expandHeightPending)

        # Register Global Shift+Enter
        if (-not $global:hotkeyRegistered -and $global:hwnd) {
            $reg = [Win32HUD]::RegisterHotKey($global:hwnd, 1, 0x4004, 0x0D)
            if (-not $reg) {
                $reg = [Win32HUD]::RegisterHotKey($global:hwnd, 1, 0x0004, 0x0D)
            }
            $global:hotkeyRegistered = $reg
        }
    } elseif ($mode -eq "Sessions") {
        $window.Width  = $windowSessionsW
        $window.Height = $windowSessionsH
        $window.Left   = [Math]::Max(10, [Math]::Min($global:pillCenterX - ($windowSessionsW / 2), $screenWidth - $windowSessionsW - 10))
        
        $pnlPending.Visibility  = "Collapsed"
        $pnlSessions.Visibility = "Visible"
        $btnPanel.Visibility    = "Collapsed"
        $hudBorder.CornerRadius = New-Object System.Windows.CornerRadius(0, 0, 16, 16)
        $hudBorder.BeginAnimation([System.Windows.Controls.Border]::WidthProperty, $expandWidthSessions)
        $hudBorder.BeginAnimation([System.Windows.Controls.Border]::HeightProperty, $expandHeightSessions)
        
        if ($global:hotkeyRegistered -and $global:hwnd) {
            [Win32HUD]::UnregisterHotKey($global:hwnd, 1) | Out-Null
            $global:hotkeyRegistered = $false
        }
    } else {
        $pnlPending.Visibility  = "Collapsed"
        $pnlSessions.Visibility = "Collapsed"
        $btnPanel.Visibility    = "Collapsed"
        $hudBorder.CornerRadius = New-Object System.Windows.CornerRadius(0, 0, 14, 14)
        $hudBorder.BeginAnimation([System.Windows.Controls.Border]::WidthProperty, $collapseWidth)
        $hudBorder.BeginAnimation([System.Windows.Controls.Border]::HeightProperty, $collapseHeight)
        
        # Snap window frame back down to compact size
        $window.Width  = $windowContractedW
        $window.Height = $windowContractedH
        $window.Left   = [Math]::Max(10, [Math]::Min($global:pillCenterX - ($windowContractedW / 2), $screenWidth - $windowContractedW - 10))
        
        if ($prevMode -ne "Collapsed") {
            Trigger-LightSplash
        }
        
        if ($global:hotkeyRegistered -and $global:hwnd) {
            [Win32HUD]::UnregisterHotKey($global:hwnd, 1) | Out-Null
            $global:hotkeyRegistered = $false
        }
    }
}

function Read-JsonSafely([string]$Path) {
    try {
        $fs = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $sr = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
        $raw = $sr.ReadToEnd()
        $sr.Close()
        $fs.Close()
        if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
        return $raw | ConvertFrom-Json
    } catch {
        return $null
    }
}

function Dismiss-ReviewSessions {
    $sessionFiles = Get-ChildItem -Path $SESSIONS_DIR -Filter "*.json" -ErrorAction SilentlyContinue
    foreach ($sf in $sessionFiles) {
        $sdata = Read-JsonSafely $sf.FullName
        if ($sdata -and ($sdata.state -eq "review" -or $sdata.state -eq "completed")) {
            [System.IO.File]::Delete($sf.FullName)
        }
    }
    Check-State
}

$headerGrid.add_MouseLeftButtonDown({
    param($sender, $e)
    if ($e.ClickCount -ge 2) {
        $global:isManuallyExpanded = -not $global:isManuallyExpanded
        Check-State
        return
    }

    if ($lblStatus.Text -eq "READY FOR REVIEW") {
        Dismiss-ReviewSessions
    } elseif ($global:currentExpandedMode -eq "Pending") {
        # Cycle pending approvals on click
        $pendingFiles = Get-ChildItem -Path $PENDING_DIR -Filter "*.json" -ErrorAction SilentlyContinue
        if ($pendingFiles.Count -gt 1) {
            $global:pendingIndex = ($global:pendingIndex + 1) % $pendingFiles.Count
            Check-State
        }
    }

    # Fluid Top-Bar Dragging
    try {
        if ($global:hwnd) {
            [Win32HUD]::ReleaseCapture() | Out-Null
            [Win32HUD]::SendMessage($global:hwnd, 0x00A1, [IntPtr]2, [IntPtr]::Zero) | Out-Null
        } else {
            $window.DragMove()
        }
        $window.Top = 0
        $global:pillCenterX = $window.Left + ($window.Width / 2)
    } catch { }
})

$hudBorder.add_MouseLeftButtonDown({
    param($sender, $e)
    if ($e.OriginalSource -eq $hudBorder -or $e.OriginalSource -is [System.Windows.Controls.Grid]) {
        try {
            if ($global:hwnd) {
                [Win32HUD]::ReleaseCapture() | Out-Null
                [Win32HUD]::SendMessage($global:hwnd, 0x00A1, [IntPtr]2, [IntPtr]::Zero) | Out-Null
            } else {
                $window.DragMove()
            }
            $window.Top = 0
            $global:pillCenterX = $window.Left + ($window.Width / 2)
        } catch { }
    }
})

# Mouse Wheel Cycling for Multi-Session Approvals
$window.add_MouseWheel({
    param($sender, $e)
    if ($global:currentExpandedMode -eq "Pending") {
        $pFiles = Get-ChildItem -Path $PENDING_DIR -Filter "*.json" -ErrorAction SilentlyContinue
        if ($pFiles.Count -gt 1) {
            if ($e.Delta -lt 0) {
                $global:pendingIndex = ($global:pendingIndex + 1) % $pFiles.Count
            } else {
                $global:pendingIndex = ($global:pendingIndex - 1 + $pFiles.Count) % $pFiles.Count
            }
            Check-State
        }
    }
})

function Get-SessionDisplayName($sdata) {
    if (-not $sdata) { return "Session" }
    $convId = $sdata.conversation_id
    if ($convId -and $global:sessionNamesCache.ContainsKey($convId)) {
        return $global:sessionNamesCache[$convId]
    }

    $displayName = ""
    if ($sdata.task_name -and $sdata.task_name.Length -gt 0) {
        $displayName = $sdata.task_name
    } elseif ($convId) {
        $transPath = "$HOME\.gemini\antigravity-cli\brain\$convId\.system_generated\logs\transcript.jsonl"
        if (Test-Path $transPath) {
            try {
                $lines = Get-Content $transPath -Tail 150 -ErrorAction SilentlyContinue
                for ($i = $lines.Count - 1; $i -ge 0; $i--) {
                    if ($lines[$i] -match '"type":\s*"USER_INPUT"') {
                        $parsed = $lines[$i] | ConvertFrom-Json
                        $content = [string]$parsed.content
                        if ($content -match '<USER_REQUEST>([\s\S]*?)</USER_REQUEST>') {
                            $content = $matches[1]
                        }
                        $firstLine = ($content -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 } | Select-Object -First 1).Trim()
                        if ($firstLine) {
                            if ($firstLine.Length -gt 28) {
                                $firstLine = $firstLine.Substring(0, 25) + "..."
                            }
                            $displayName = $firstLine
                            break
                        }
                    }
                }
            } catch { }
        }
    }

    if (-not $displayName) {
        if ($sdata.project -and $sdata.project -ne "Antigravity") {
            $displayName = $sdata.project
        } else {
            $displayName = "Main Workspace"
        }
    }

    if ($convId) {
        $global:sessionNamesCache[$convId] = $displayName
    }
    return $displayName
}

function Check-State {
    # 0. Sync Auto-Mode Flag
    $flagExists = Test-Path "$SPOOL_DIR\auto_mode.flag"
    if ($global:isAutoMode -ne $flagExists) {
        $global:isAutoMode = $flagExists
    }
    Update-AutoBadgeVisuals


    # 1. Check Pending Approvals & Concurrency Queue
    $pendingFiles = Get-ChildItem -Path $PENDING_DIR -Filter "*.json" -ErrorAction SilentlyContinue
    $pendingBadge = ""
    if ($pendingFiles.Count -gt 0) {
        if ($global:pendingIndex -ge $pendingFiles.Count) {
            $global:pendingIndex = 0
        }
        $targetFile = $pendingFiles[$global:pendingIndex]
        $pdata = Read-JsonSafely $targetFile.FullName
        
        $global:activeConvId = if ($pdata -and $pdata.conversation_id) { $pdata.conversation_id } else { [System.IO.Path]::GetFileNameWithoutExtension($targetFile.Name) }

        if ($pdata -and $pdata.summary) {
            $lblCommand.Text = $pdata.summary
        } else {
            $lblCommand.Text = "Requires Approval"
        }

        # Prevent command truncation blind spot with rich ToolTip
        if ($pdata -and $pdata.detail) {
            $lblCommand.ToolTip = $pdata.detail
        } else {
            $lblCommand.ToolTip = $lblCommand.Text
        }

        if ($pendingFiles.Count -gt 1) {
            $pendingBadge = " ($($global:pendingIndex + 1) of $($pendingFiles.Count))"
        }
    } else {
        $global:activeConvId = $null
        $global:pendingIndex = 0
    }

    # 2. Check Sessions & Auto-Prune Inactive/Zombie Sessions (300s TTL, 60s Review Decay)
    $sessionFiles = Get-ChildItem -Path $SESSIONS_DIR -Filter "*.json" -ErrorAction SilentlyContinue
    $hasError   = $false
    $hasReview  = $false
    $hasWorking = $false
    
    $summaryList = @()
    $currentPaths = @()
    $nowUtc = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

    foreach ($sf in $sessionFiles) {
        $path = $sf.FullName
        $sdata = Read-JsonSafely $path
        
        # Pruning check: delete stale sessions older than 300 seconds
        if ($sdata -and $sdata.timestamp) {
            $ageSec = $nowUtc - [long]$sdata.timestamp
            if ($ageSec -gt 300) {
                try { [System.IO.File]::Delete($path) } catch { }
                continue
            }
            # If in review for > 60 seconds, auto-decay to prevent permanently trapping HUD
            if (($sdata.state -eq "review" -or $sdata.state -eq "completed") -and $ageSec -gt 60) {
                try { [System.IO.File]::Delete($path) } catch { }
                continue
            }
        } elseif ($sf.LastWriteTime -lt (Get-Date).AddSeconds(-300)) {
            try { [System.IO.File]::Delete($path) } catch { }
            continue
        }

        $currentPaths += $path
        if ($sdata) {
            $global:sessionCache[$path] = $sdata
        } elseif ($global:sessionCache.ContainsKey($path)) {
            $sdata = $global:sessionCache[$path]
        }
        
        if ($sdata) {
            $st = $sdata.state
            $dispName = Get-SessionDisplayName $sdata
            $summaryList += "$bullet $($dispName): $st - $($sdata.message)"
            if ($st -eq "error") { $hasError = $true }
            elseif ($st -eq "review" -or $st -eq "completed") { $hasReview = $true }
            elseif ($st -eq "working" -or $st -eq "thinking") { $hasWorking = $true }
        }
    }

    # Pull Fallback: If no spool files exist, check if agy.exe is running and inspect the active brain transcripts
    if ($currentPaths.Count -eq 0) {
        $agyProc = Get-Process agy -ErrorAction SilentlyContinue
        if ($agyProc) {
            $recentTranscripts = Get-ChildItem -Path "$HOME\.gemini\antigravity-cli\brain\*\.system_generated\logs\transcript.jsonl" -ErrorAction SilentlyContinue |
                Where-Object { $_.LastWriteTime -gt (Get-Date).AddMinutes(-30) } |
                Sort-Object LastWriteTime -Descending |
                Select-Object -First 3

            foreach ($tf in $recentTranscripts) {
                try {
                    $convId = $tf.Directory.Parent.Parent.Name
                    $lastLines = Get-Content $tf.FullName -Tail 15 -ErrorAction SilentlyContinue
                    $mName = "CLI AGENT"
                    foreach ($l in $lastLines) {
                        if ($l -match 'setting\s+`?Model Selection`?\s+from\s+.*?to\s+([A-Za-z0-9. ()-]+?)\.\s+No need') {
                            $mName = $matches[1].Trim()
                        } elseif ($l -match '"modelName":\s*"([^"]+)"') {
                            $mName = $matches[1].Trim()
                        }
                    }
                    $fallbackState = [ordered]@{
                        state           = "working"
                        message         = "Active CLI Session"
                        timestamp       = $nowUtc
                        conversation_id = $convId
                        project         = "Antigravity"
                        model           = $mName
                        agent           = "antigravity"
                    }
                    $fPath = "$SESSIONS_DIR\$convId.json"
                    [System.IO.File]::WriteAllText($fPath, ($fallbackState | ConvertTo-Json -Compress), [System.Text.Encoding]::UTF8)
                    $currentPaths += $fPath
                    $hasWorking = $true
                    $dispName = Get-SessionDisplayName $fallbackState
                    $summaryList += "$bullet $($dispName): working - $($fallbackState.message)"
                } catch { }
            }
        }
    }

    $keysToRemove = @()
    foreach ($k in $global:sessionCache.Keys) {
        if ($currentPaths -notcontains $k) { $keysToRemove += $k }
    }
    foreach ($k in $keysToRemove) { $global:sessionCache.Remove($k) }

    $summaryText = if ($summaryList.Count -gt 0) { $summaryList -join "`n" } else { "No active agent sessions" }

    if ($global:activeConvId) {
        $globalState = "REQUIRES ATTENTION$pendingBadge"
        $globalBrush = $brushOrange
        $globalColor = $colorOrange
        $targetMode  = "Pending"
    } elseif ($hasError) {
        $globalState = "ERROR DETECTED"
        $globalBrush = $brushRed
        $globalColor = $colorRed
        $targetMode  = if ($global:isManuallyExpanded) { "Sessions" } else { "Collapsed" }
    } elseif ($hasReview) {
        $globalState = "READY FOR REVIEW"
        $globalBrush = $brushPurple
        $globalColor = $colorPurple
        $targetMode  = if ($global:isManuallyExpanded) { "Sessions" } else { "Collapsed" }
    } elseif ($hasWorking) {
        $globalState = if ($global:isAutoMode) { "AUTO $bullet WORKING" } else { "WORKING" }
        $globalBrush = if ($global:isAutoMode) { $brushIndigo } else { $brushBlue }
        $globalColor = if ($global:isAutoMode) { $colorIndigo } else { $colorBlue }
        $targetMode  = if ($global:isManuallyExpanded) { "Sessions" } else { "Collapsed" }
    } else {
        $globalState = if ($global:isAutoMode) { "AUTO $bullet PILOT" } else { "IDLE" }
        $globalBrush = if ($global:isAutoMode) { $brushIndigo } else { $brushGreen }
        $globalColor = if ($global:isAutoMode) { $colorIndigo } else { $colorGreen }
        $targetMode  = if ($global:isManuallyExpanded) { "Sessions" } else { "Collapsed" }
    }

    Set-Expanded $targetMode

    # Apple Dynamic Island Edge LED Perimeter Breathing Glow
    if ($hasWorking -and -not $global:activeConvId) {
        if (-not $global:isPulsing) {
            $hudShadow.BeginAnimation([System.Windows.Media.Effects.DropShadowEffect]::OpacityProperty, $edgePulseOpacityAnim)
            $hudShadow.BeginAnimation([System.Windows.Media.Effects.DropShadowEffect]::BlurRadiusProperty, $edgePulseBlurAnim)
            $global:isPulsing = $true
        }
    } else {
        if ($global:isPulsing) {
            $hudShadow.BeginAnimation([System.Windows.Media.Effects.DropShadowEffect]::OpacityProperty, $null)
            $hudShadow.BeginAnimation([System.Windows.Media.Effects.DropShadowEffect]::BlurRadiusProperty, $null)
            $hudShadow.Opacity = 0.8
            $hudShadow.BlurRadius = 12
            $global:isPulsing = $false
        }
    }
    
    $lblStatus.Text         = $globalState
    $lblStatus.Foreground   = $globalBrush
    $hudBorder.BorderBrush  = $globalBrush
    $hudShadow.Color        = $globalColor
    $lblSessionSummary.Text = $summaryText
}

$btnApprove.add_Click({ Approve-CurrentPending })
$btnReject.add_Click({ Reject-CurrentPending })

# Local PreviewKeyDown fallback
$window.add_PreviewKeyDown({
    param($sender, $e)
    if ($e.Key -eq [System.Windows.Input.Key]::Enter -and [System.Windows.Input.Keyboard]::Modifiers -match "Shift") {
        if ($global:currentExpandedMode -eq "Pending" -and $global:activeConvId) {
            Approve-CurrentPending
            $e.Handled = $true
        }
    } elseif ($e.Key -eq [System.Windows.Input.Key]::Escape -and $global:isAutoMode) {
        # Emergency Escape Hatch: Esc disengages Auto-Mode
        Toggle-AutoMode
        $e.Handled = $true
    }
})

$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromMilliseconds(300)
$timer.Add_Tick({ Check-State })
$timer.Start()

# Cleanup on exit
$window.add_Closed({
    if (Test-Path "$SPOOL_DIR\hud.pid") { [System.IO.File]::Delete("$SPOOL_DIR\hud.pid") }
    if (Test-Path "$SPOOL_DIR\auto_mode.flag") { [System.IO.File]::Delete("$SPOOL_DIR\auto_mode.flag") }
})

Update-AutoBadgeVisuals
Check-State
$window.ShowDialog() | Out-Null

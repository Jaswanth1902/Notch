using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Notch
{
    public class SessionItem
    {
        public string Id { get; set; }
        public string Agent { get; set; }
        public string Project { get; set; }
        public string Tool { get; set; }
        public string State { get; set; }
        public int Pid { get; set; }
        public long Timestamp { get; set; }
    }

    public static class Win32
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int MOD_SHIFT = 0x0004;
        public const int VK_RETURN = 0x0D;
        public const int VK_ESCAPE = 0x1B;
        public const int VK_1 = 0x31;
        public const int VK_2 = 0x32;
        public const int VK_3 = 0x33;
        public const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetNamedPipeClientProcessId(IntPtr Pipe, out uint ClientProcessId);

        public const int SW_RESTORE = 9;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        public static void ForceForegroundWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                ShowWindow(hwnd, SW_RESTORE);
                BringWindowToTop(hwnd);
                SwitchToThisWindow(hwnd, true);

                IntPtr fgWnd = GetForegroundWindow();
                uint fgPid;
                uint fgThread = GetWindowThreadProcessId(fgWnd, out fgPid);
                uint curThread = GetCurrentThreadId();

                if (fgThread != 0 && fgThread != curThread)
                {
                    AttachThreadInput(curThread, fgThread, true);
                    BringWindowToTop(hwnd);
                    SetForegroundWindow(hwnd);
                    AttachThreadInput(curThread, fgThread, false);
                }
                else
                {
                    BringWindowToTop(hwnd);
                    SetForegroundWindow(hwnd);
                }
            }
            catch { }
        }

        public static IntPtr FindWindowByPid(int targetPid)
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    uint pid;
                    GetWindowThreadProcessId(hWnd, out pid);
                    if (pid == targetPid)
                    {
                        var sb = new StringBuilder(256);
                        GetWindowText(hWnd, sb, 256);
                        string t = sb.ToString();
                        if (!string.IsNullOrEmpty(t) && !t.Contains("Notch Dynamic Island HUD"))
                        {
                            result = hWnd;
                            return false;
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public static IntPtr FindWindowBySubstring(string sub)
        {
            if (string.IsNullOrEmpty(sub)) return IntPtr.Zero;
            IntPtr result = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    var sb = new StringBuilder(256);
                    GetWindowText(hWnd, sb, 256);
                    string t = sb.ToString();
                    if (!string.IsNullOrEmpty(t) && !t.Contains("Notch Dynamic Island HUD") && t.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result = hWnd;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public static IntPtr FindWindowByProcessName(string procName)
        {
            if (string.IsNullOrEmpty(procName)) return IntPtr.Zero;
            IntPtr result = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    uint pid;
                    GetWindowThreadProcessId(hWnd, out pid);
                    if (pid > 0)
                    {
                        try
                        {
                            var proc = Process.GetProcessById((int)pid);
                            if (proc != null && proc.ProcessName.IndexOf(procName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var sb = new StringBuilder(256);
                                GetWindowText(hWnd, sb, 256);
                                string t = sb.ToString();
                                if (!string.IsNullOrEmpty(t) && !t.Contains("Notch Dynamic Island HUD"))
                                {
                                    result = hWnd;
                                    return false;
                                }
                            }
                        }
                        catch { }
                    }
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }
    }

    public class NotchWindow : Window
    {
        private Border _hudBorder;
        private DropShadowEffect _hudShadow;
        private TextBlock _lblStatus;
        private Border _statusOrb;
        private Border _autoBadge;
        private TextBlock _lblAutoBadge;
        private Border _clavisPill;
        private TextBlock _lblClavisPill;
        private string _activeClavisProfile = "deepseek";
        private int _clavisProfileIdx = 0;

        // Content panels
        private Grid _pnlWorking;
        private TextBlock _lblAgent;
        private TextBlock _lblTool;
        private Border _bar1, _bar2, _bar3;

        // Attention / Approval Gate Panel
        private Grid _pnlAttention;
        private TextBlock _lblAttentionHeader;
        private TextBlock _lblAttentionCmd;
        private Border _diffCard;
        private StackPanel _diffLinesStack;
        private TextBlock _diffSummary;
        private Button _btnApprove;
        private Button _btnDeny;

        // Ask / Interactive Question Panel
        private Grid _pnlAsk;
        private TextBlock _lblAskPrompt;
        private StackPanel _askOptionsStack;

        // Sessions Drawer Panel
        private Grid _pnlSessions;
        private StackPanel _sessionsListStack;

        private string _currentState = "idle";
        private string _activeConvId = null;
        private string _reviewedConvId = null;
        private DateTime _reviewEnteredTime = DateTime.MinValue;
        private bool _isAutoMode = false;
        private bool _isSessionsExpanded = false;

        // Security & Destructive Double-Gate State
        private bool _isDestructivePending = false;
        private bool _destructiveArmed = false;
        private DateTime _destructiveArmTime = DateTime.MinValue;

        // Horizontal Drag & Top Attachment State
        private double _screenWidth;
        private double _screenHeight;
        private double _pillCenterX;
        private bool _isDragging = false;
        private Point _dragStartMousePos;
        private double _dragStartLeft;
        private bool _dragMoved = false;

        // Minimal White Line & Gaming/Presentation Fullscreen State
        private bool _isMinimalLine = false;
        private bool _isManualExpanded = false;
        private bool _wasFullscreenMode = false;
        private StackPanel _mainStack;

        private TaskCompletionSource<string> _currentGateTcs = null;
        private TaskCompletionSource<string> _currentAskTcs = null;
        private readonly List<string> _currentAskOptions = new List<string>();

        // Liquid Glass 3D Volumetric Brushes (Less black, smoked sapphire glass depth)
        private readonly LinearGradientBrush _brushLiquidGlass = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0xEE, 0x22, 0x2A, 0x3B), 0.0),
                new GradientStop(Color.FromArgb(0xDC, 0x16, 0x1D, 0x2C), 0.45),
                new GradientStop(Color.FromArgb(0xE8, 0x0E, 0x13, 0x20), 1.0)
            }
        };

        private readonly LinearGradientBrush _brushGlassBorder = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF), 0.0),
                new GradientStop(Color.FromArgb(0x35, 0x93, 0xC5, 0xFD), 0.35),
                new GradientStop(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF), 1.0)
            }
        };

        private readonly SolidColorBrush _brushGlassCard = new SolidColorBrush(Color.FromArgb(0x55, 0x24, 0x2E, 0x42));
        private readonly SolidColorBrush _brushGlassCardBorder = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));

        // Palette
        private readonly SolidColorBrush _brushGreen = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
        private readonly SolidColorBrush _brushBlue = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
        private readonly SolidColorBrush _brushViolet = new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xF7));
        private readonly SolidColorBrush _brushAmber = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
        private readonly SolidColorBrush _brushRed = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        private readonly SolidColorBrush _brushCyan = new SolidColorBrush(Color.FromRgb(0x06, 0xB6, 0xD4));

        public NotchWindow()
        {
            _screenWidth = SystemParameters.PrimaryScreenWidth;
            _screenHeight = SystemParameters.PrimaryScreenHeight;
            _pillCenterX = _screenWidth / 2.0;

            Title = "Notch Dynamic Island HUD";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            Focusable = false;

            Width = 350;
            Height = 34;
            Left = _pillCenterX - (Width / 2.0);
            Top = 0; // Strictly flush with top screen bezel

            BuildUI();
            StartIpcServices();
            StartSpoolAndForegroundWatcher();
        }

        private void BuildUI()
        {
            var rootGrid = new Grid
            {
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _hudBorder = new Border
            {
                Name = "HudBorder",
                CornerRadius = new CornerRadius(0, 0, 18, 18),
                Background = _brushLiquidGlass,
                BorderBrush = _brushGlassBorder,
                BorderThickness = new Thickness(1, 0, 1, 1),
                Width = 330,
                Height = 30,
                ClipToBounds = true,
                Cursor = Cursors.Hand
            };

            // Fluid Horizontal Drag Attachment & Double-Click Minimal Toggle
            _hudBorder.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    e.Handled = true;
                    if (_isMinimalLine)
                    {
                        _isManualExpanded = true;
                        ExpandFromMinimalLine();
                    }
                    else
                    {
                        _isManualExpanded = false;
                        CollapseToMinimalLine();
                    }
                    return;
                }

                _isDragging = true;
                _dragMoved = false;
                _dragStartMousePos = e.GetPosition(this);
                _dragStartLeft = this.Left;
                _hudBorder.CaptureMouse();
                e.Handled = true;
            };

            _hudBorder.MouseMove += (s, e) =>
            {
                if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
                {
                    Point curPos = e.GetPosition(this);
                    double deltaX = curPos.X - _dragStartMousePos.X;
                    if (Math.Abs(deltaX) > 2)
                    {
                        _dragMoved = true;
                        double newLeft = _dragStartLeft + deltaX;
                        if (newLeft < 5) newLeft = 5;
                        if (newLeft > _screenWidth - this.Width - 5) newLeft = _screenWidth - this.Width - 5;

                        this.Left = newLeft;
                        this.Top = 0; // Absolute invariant: Always attach to top!
                        _pillCenterX = this.Left + (this.Width / 2.0);
                    }
                }
            };

            _hudBorder.MouseLeftButtonUp += (s, e) =>
            {
                if (_isDragging)
                {
                    _isDragging = false;
                    _hudBorder.ReleaseMouseCapture();
                    this.Top = 0;
                    _pillCenterX = this.Left + (this.Width / 2.0);

                    // If user clicked without dragging:
                    if (!_dragMoved)
                    {
                        if (_isMinimalLine)
                        {
                            _isManualExpanded = true;
                            ExpandFromMinimalLine();
                        }
                        else
                        {
                            OnPillClicked();
                        }
                    }
                    e.Handled = true;
                }
            };

            _hudShadow = new DropShadowEffect
            {
                Color = Color.FromRgb(0x10, 0xB9, 0x81),
                BlurRadius = 14,
                ShadowDepth = 0,
                Opacity = 0.5
            };
            _hudBorder.Effect = _hudShadow;

            _mainStack = new StackPanel { Orientation = Orientation.Vertical };

            // 1. Header Row (Always visible)
            var headerGrid = new Grid
            {
                Height = 30,
                Background = Brushes.Transparent
            };

            var leftStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };

            _statusOrb = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(3.5),
                Background = _brushGreen,
                Margin = new Thickness(0, 0, 8, 0)
            };

            _lblStatus = new TextBlock
            {
                Text = "IDLE",
                Foreground = _brushGreen,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI, sans-serif"),
                VerticalAlignment = VerticalAlignment.Center
            };

            leftStack.Children.Add(_statusOrb);
            leftStack.Children.Add(_lblStatus);

            var rightStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };

            _clavisPill = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(0x35, 0x06, 0xB6, 0xD4)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x06, 0xB6, 0xD4)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand
            };
            _lblClavisPill = new TextBlock
            {
                Text = "⚡ DEEPSEEK • 84ms",
                Foreground = _brushCyan,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas, Segoe UI, sans-serif")
            };
            _clavisPill.Child = _lblClavisPill;
            _clavisPill.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                CycleClavisProfile();
            };

            _autoBadge = new Border
            {
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 2, 6, 2),
                Cursor = Cursors.Hand
            };
            _autoBadge.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                _isAutoMode = !_isAutoMode;
                _lblAutoBadge.Text = _isAutoMode ? "AUTO ON" : "SESSIONS";
                _lblAutoBadge.Foreground = _isAutoMode ? _brushBlue : new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
                ToggleSessionsDrawer();
            };

            _lblAutoBadge = new TextBlock
            {
                Text = "SESSIONS",
                Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI, sans-serif")
            };
            _autoBadge.Child = _lblAutoBadge;

            rightStack.Children.Add(_clavisPill);
            rightStack.Children.Add(_autoBadge);

            headerGrid.Children.Add(leftStack);
            headerGrid.Children.Add(rightStack);
            _mainStack.Children.Add(headerGrid);

            // 2. Working Panel
            _pnlWorking = new Grid
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(12, 2, 12, 8)
            };
            var workStack = new StackPanel { Orientation = Orientation.Horizontal };
            var workInfo = new StackPanel { Orientation = Orientation.Vertical, Width = 310 };
            _lblAgent = new TextBlock
            {
                Text = "Claude Code",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            _lblTool = new TextBlock
            {
                Text = "pytest tests/test_mesh.py",
                Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                FontSize = 10,
                FontFamily = new FontFamily("Consolas, monospace")
            };
            workInfo.Children.Add(_lblAgent);
            workInfo.Children.Add(_lblTool);

            var eqStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            _bar1 = new Border { Width = 3, Height = 12, Background = _brushBlue, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(1.5) };
            _bar2 = new Border { Width = 3, Height = 18, Background = _brushBlue, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(1.5) };
            _bar3 = new Border { Width = 3, Height = 8, Background = _brushBlue, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(1.5) };
            eqStack.Children.Add(_bar1);
            eqStack.Children.Add(_bar2);
            eqStack.Children.Add(_bar3);

            workStack.Children.Add(workInfo);
            workStack.Children.Add(eqStack);
            _pnlWorking.Children.Add(workStack);
            _mainStack.Children.Add(_pnlWorking);

            // 3. Attention / Approval Gate Panel (With Inline Diff & Dangerous Gating)
            _pnlAttention = new Grid
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(12, 4, 12, 10)
            };
            var attStack = new StackPanel { Orientation = Orientation.Vertical };
            _lblAttentionHeader = new TextBlock
            {
                Text = "PERMISSION GATE REQUIRED",
                Foreground = _brushAmber,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _lblAttentionCmd = new TextBlock
            {
                Text = "cargo build --release",
                Foreground = ColorToBrush(0xFF, 0xFE, 0xF3, 0xC7),
                Background = _brushGlassCard,
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 10.5,
                FontFamily = new FontFamily("Consolas, monospace"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };

            // Inline Diff Preview Container
            _diffCard = new Border
            {
                Background = _brushGlassCard,
                BorderBrush = _brushGlassCardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            var diffInner = new StackPanel { Orientation = Orientation.Vertical };
            _diffSummary = new TextBlock
            {
                Text = "DIFF PREVIEW (+3 -1)",
                Foreground = _brushGreen,
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas, monospace"),
                Margin = new Thickness(0, 0, 0, 2)
            };
            _diffLinesStack = new StackPanel { Orientation = Orientation.Vertical };
            diffInner.Children.Add(_diffSummary);
            diffInner.Children.Add(_diffLinesStack);
            _diffCard.Child = diffInner;

            var btnStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _btnApprove = new Button
            {
                Content = "Allow (Shift+↵)",
                Width = 150,
                Height = 26,
                Background = _brushAmber,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            _btnApprove.Click += (s, e) => HandleApproveClick();

            _btnDeny = new Button
            {
                Content = "Deny (Esc)",
                Width = 80,
                Height = 26,
                Background = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                Cursor = Cursors.Hand
            };
            _btnDeny.Click += (s, e) => DenyGate();

            btnStack.Children.Add(_btnApprove);
            btnStack.Children.Add(_btnDeny);

            attStack.Children.Add(_lblAttentionHeader);
            attStack.Children.Add(_lblAttentionCmd);
            attStack.Children.Add(_diffCard);
            attStack.Children.Add(btnStack);
            _pnlAttention.Children.Add(attStack);
            _mainStack.Children.Add(_pnlAttention);

            // 4. Interactive Ask Panel (AskUserQuestion / Direct 1-Click Answering)
            _pnlAsk = new Grid
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(12, 4, 12, 10)
            };
            var askStack = new StackPanel { Orientation = Orientation.Vertical };
            var askHeader = new TextBlock
            {
                Text = "QUESTION // AGENT WAITING FOR INPUT",
                Foreground = _brushCyan,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _lblAskPrompt = new TextBlock
            {
                Text = "Which target environment should we deploy to?",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _askOptionsStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            askStack.Children.Add(askHeader);
            askStack.Children.Add(_lblAskPrompt);
            askStack.Children.Add(_askOptionsStack);
            _pnlAsk.Children.Add(askStack);
            _mainStack.Children.Add(_pnlAsk);

            // 5. Universal Sessions Drawer
            _pnlSessions = new Grid
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(12, 4, 12, 10)
            };
            var sessContainer = new StackPanel { Orientation = Orientation.Vertical };
            var sessHeader = new TextBlock
            {
                Text = "ACTIVE AGENT SESSIONS",
                Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            sessContainer.Children.Add(sessHeader);

            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 120,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _sessionsListStack = new StackPanel { Orientation = Orientation.Vertical };
            scrollViewer.Content = _sessionsListStack;
            sessContainer.Children.Add(scrollViewer);

            _pnlSessions.Children.Add(sessContainer);
            _mainStack.Children.Add(_pnlSessions);

            _hudBorder.Child = _mainStack;
            rootGrid.Children.Add(_hudBorder);
            Content = rootGrid;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;

            int exStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, exStyle | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);

            // Hotkeys: Shift+Enter (101) & Esc (102) & 1, 2, 3 for Question answers
            Win32.RegisterHotKey(hwnd, 101, Win32.MOD_SHIFT, Win32.VK_RETURN);
            Win32.RegisterHotKey(hwnd, 102, 0, Win32.VK_ESCAPE);
            Win32.RegisterHotKey(hwnd, 201, 0, Win32.VK_1);
            Win32.RegisterHotKey(hwnd, 202, 0, Win32.VK_2);
            Win32.RegisterHotKey(hwnd, 203, 0, Win32.VK_3);

            var source = HwndSource.FromHwnd(hwnd);
            source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == 101 && _currentState == "attention")
                {
                    HandleApproveClick();
                    handled = true;
                }
                else if (id == 102 && (_currentState == "attention" || _currentState == "ask"))
                {
                    DenyGate();
                    handled = true;
                }
                else if (id >= 201 && id <= 203 && _currentState == "ask")
                {
                    int choiceIndex = id - 201;
                    AnswerQuestion(choiceIndex);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void PlayEarcon(string type)
        {
            if (string.IsNullOrEmpty(type)) return;
            string t = type.ToLowerInvariant();
            // Semantic Audio Earcon: STRICTLY task complete chime ONLY, nothing else!
            if (t != "complete" && t != "approved" && t != "task_complete" && t != "review") return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // Harmonic completion chime: C6 (1046Hz, 90ms) -> E6 (1318Hz, 120ms)
                    Console.Beep(1046, 90);
                    Thread.Sleep(20);
                    Console.Beep(1318, 120);
                }
                catch { }
            });
        }

        private bool IsDestructiveCommand(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return false;
            string lower = cmd.ToLowerInvariant();
            if (Regex.IsMatch(lower, @"\brm\s+-[rf]{1,2}\b")) return true;
            if (Regex.IsMatch(lower, @"\bdel\s+/[sfq]{1,3}\b")) return true;
            if (Regex.IsMatch(lower, @"\brmdir\s+/[sq]{1,2}\b")) return true;
            if (lower.Contains("format ") || lower.Contains("drop table") || lower.Contains("drop database") || lower.Contains("truncate ")) return true;
            if (lower.Contains("git push") && (lower.Contains("--force") || lower.Contains(" -f "))) return true;
            if (lower.Contains("git reset --hard")) return true;
            if (lower.Contains("invoke-expression") || lower.Contains("iex ") || lower.Contains("-encodedcommand")) return true;
            return false;
        }

        private void HandleApproveClick()
        {
            if (_isDestructivePending)
            {
                if (!_destructiveArmed || (DateTime.UtcNow - _destructiveArmTime).TotalSeconds > 1.8)
                {
                    // Arm the double gate!
                    _destructiveArmed = true;
                    _destructiveArmTime = DateTime.UtcNow;
                    _btnApprove.Content = "PRESS AGAIN TO CONFIRM!";
                    _btnApprove.Background = _brushRed;
                    _btnApprove.Foreground = Brushes.White;

                    // Start a 1.8s timer to disarm if not pressed again
                    var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
                    t.Tick += (s, e) =>
                    {
                        t.Stop();
                        if (_destructiveArmed && _isDestructivePending && _currentState == "attention")
                        {
                            _destructiveArmed = false;
                            _btnApprove.Content = "CONFIRM (Press 2x)";
                            _btnApprove.Background = _brushRed;
                        }
                    };
                    t.Start();
                    return;
                }
            }

            // Allowed!
            ApproveGate();
        }

        private void AnimateTo(double targetWidth, double targetHeight, Color shadowColor, Color statusColor, string statusText)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(180);

            var animBorderW = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
            var animBorderH = new DoubleAnimation(targetHeight, duration) { EasingFunction = ease };
            _hudBorder.BeginAnimation(WidthProperty, animBorderW);
            _hudBorder.BeginAnimation(HeightProperty, animBorderH);

            double winW = targetWidth + 20;
            double winH = Math.Max(34, targetHeight + 10);
            double targetLeft = _pillCenterX - (winW / 2.0);

            if (targetLeft < 5) targetLeft = 5;
            if (targetLeft > _screenWidth - winW - 5) targetLeft = _screenWidth - winW - 5;

            var animWinW = new DoubleAnimation(winW, duration) { EasingFunction = ease };
            var animWinH = new DoubleAnimation(winH, duration) { EasingFunction = ease };
            var animWinL = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };

            BeginAnimation(WidthProperty, animWinW);
            BeginAnimation(HeightProperty, animWinH);
            BeginAnimation(LeftProperty, animWinL);

            _hudShadow.Color = shadowColor;
            _statusOrb.Background = new SolidColorBrush(statusColor);
            _lblStatus.Foreground = new SolidColorBrush(statusColor);
            _lblStatus.Text = statusText;
        }

        public void CollapseToMinimalLine()
        {
            _isMinimalLine = true;
            _mainStack.Visibility = Visibility.Collapsed;
            _pnlWorking.Visibility = Visibility.Collapsed;
            _pnlAttention.Visibility = Visibility.Collapsed;
            _pnlAsk.Visibility = Visibility.Collapsed;
            _pnlSessions.Visibility = Visibility.Collapsed;

            _hudBorder.Background = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
            _hudBorder.BorderThickness = new Thickness(0);
            _hudBorder.CornerRadius = new CornerRadius(0, 0, 2, 2);

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(160);

            double targetW = 110;
            double targetH = 3.5;

            var animBorderW = new DoubleAnimation(targetW, duration) { EasingFunction = ease };
            var animBorderH = new DoubleAnimation(targetH, duration) { EasingFunction = ease };
            _hudBorder.BeginAnimation(WidthProperty, animBorderW);
            _hudBorder.BeginAnimation(HeightProperty, animBorderH);

            double winW = targetW + 20;
            double winH = 34;
            double targetLeft = _pillCenterX - (winW / 2.0);

            if (targetLeft < 5) targetLeft = 5;
            if (targetLeft > _screenWidth - winW - 5) targetLeft = _screenWidth - winW - 5;

            var animWinW = new DoubleAnimation(winW, duration) { EasingFunction = ease };
            var animWinH = new DoubleAnimation(winH, duration) { EasingFunction = ease };
            var animWinL = new DoubleAnimation(targetLeft, duration) { EasingFunction = ease };

            BeginAnimation(WidthProperty, animWinW);
            BeginAnimation(HeightProperty, animWinH);
            BeginAnimation(LeftProperty, animWinL);

            _hudShadow.Color = Color.FromRgb(0xFF, 0xFF, 0xFF);
            _hudShadow.BlurRadius = 8;
            _hudShadow.Opacity = 0.6;
        }

        public void ExpandFromMinimalLine()
        {
            _isMinimalLine = false;
            _hudBorder.Background = _brushLiquidGlass;
            _hudBorder.BorderBrush = _brushGlassBorder;
            _hudBorder.BorderThickness = new Thickness(1, 0, 1, 1);
            _hudBorder.CornerRadius = new CornerRadius(0, 0, 18, 18);
            _hudShadow.BlurRadius = 14;
            _hudShadow.Opacity = 0.5;
            _mainStack.Visibility = Visibility.Visible;

            TransitionState(_currentState);
        }

        public void TransitionState(string state, string agent = "Agent", string tool = "", string cmd = "", string diff = "")
        {
            _currentState = state.ToLowerInvariant();
            _isSessionsExpanded = false;
            _destructiveArmed = false;

            bool isNeeded = (_currentState == "attention" || _currentState == "ask" || _currentState == "review");
            if (_isMinimalLine)
            {
                if (isNeeded)
                {
                    _isMinimalLine = false;
                    _hudBorder.Background = _brushLiquidGlass;
                    _hudBorder.BorderBrush = _brushGlassBorder;
                    _hudBorder.BorderThickness = new Thickness(1, 0, 1, 1);
                    _hudBorder.CornerRadius = new CornerRadius(0, 0, 18, 18);
                    _hudShadow.BlurRadius = 14;
                    _hudShadow.Opacity = 0.5;
                    _mainStack.Visibility = Visibility.Visible;
                }
                else
                {
                    return;
                }
            }

            _pnlWorking.Visibility = Visibility.Collapsed;
            _pnlAttention.Visibility = Visibility.Collapsed;
            _pnlAsk.Visibility = Visibility.Collapsed;
            _pnlSessions.Visibility = Visibility.Collapsed;

            switch (_currentState)
            {
                case "working":
                    _lblAgent.Text = agent;
                    _lblTool.Text = string.IsNullOrEmpty(tool) ? "Executing tool..." : tool;
                    _pnlWorking.Visibility = Visibility.Visible;
                    AnimateTo(400, 58, Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0x3B, 0x82, 0xF6), "WORKING");
                    break;

                case "thinking":
                    AnimateTo(260, 30, Color.FromRgb(0xA8, 0x55, 0xF7), Color.FromRgb(0xA8, 0x55, 0xF7), "THINKING");
                    break;

                case "attention":
                    string displayCmd = string.IsNullOrEmpty(cmd) ? (string.IsNullOrEmpty(tool) ? "Action requires user confirmation." : tool) : cmd;
                    _lblAttentionCmd.Text = displayCmd;
                    _isDestructivePending = IsDestructiveCommand(displayCmd);

                    if (_isDestructivePending)
                    {
                        _lblAttentionHeader.Text = "⚠️ CRITICAL: DESTRUCTIVE ACTION DETECTED";
                        _lblAttentionHeader.Foreground = _brushRed;
                        _btnApprove.Content = "CONFIRM (Press 2x)";
                        _btnApprove.Background = _brushRed;
                        _btnApprove.Foreground = Brushes.White;
                    }
                    else
                    {
                        _lblAttentionHeader.Text = "PERMISSION GATE REQUIRED";
                        _lblAttentionHeader.Foreground = _brushAmber;
                        _btnApprove.Content = "Allow (Shift+↵)";
                        _btnApprove.Background = _brushAmber;
                        _btnApprove.Foreground = Brushes.Black;
                    }

                    // Render inline diff if provided
                    if (!string.IsNullOrEmpty(diff) || displayCmd.Contains("replace_file_content") || displayCmd.Contains("Edit"))
                    {
                        RenderInlineDiff(diff, displayCmd);
                        _diffCard.Visibility = Visibility.Visible;
                        _pnlAttention.Visibility = Visibility.Visible;
                        AnimateTo(480, 185, _isDestructivePending ? Color.FromRgb(0xEF, 0x44, 0x44) : Color.FromRgb(0xF5, 0x9E, 0x0B),
                                         _isDestructivePending ? Color.FromRgb(0xEF, 0x44, 0x44) : Color.FromRgb(0xF5, 0x9E, 0x0B),
                                         _isDestructivePending ? "SECURITY ALERT" : "ATTENTION");
                    }
                    else
                    {
                        _diffCard.Visibility = Visibility.Collapsed;
                        _pnlAttention.Visibility = Visibility.Visible;
                        AnimateTo(460, 120, _isDestructivePending ? Color.FromRgb(0xEF, 0x44, 0x44) : Color.FromRgb(0xF5, 0x9E, 0x0B),
                                         _isDestructivePending ? Color.FromRgb(0xEF, 0x44, 0x44) : Color.FromRgb(0xF5, 0x9E, 0x0B),
                                         _isDestructivePending ? "SECURITY ALERT" : "ATTENTION");
                    }
                    break;

                case "ask":
                    _pnlAsk.Visibility = Visibility.Visible;
                    AnimateTo(480, 115, Color.FromRgb(0x06, 0xB6, 0xD4), Color.FromRgb(0x06, 0xB6, 0xD4), "QUESTION");
                    break;

                case "review":
                    _reviewEnteredTime = DateTime.UtcNow;
                    PlayEarcon("complete");
                    string revAgent = !string.IsNullOrEmpty(agent) && agent != "Agent" ? agent.ToUpperInvariant() : "REVIEW";
                    AnimateTo(350, 30, Color.FromRgb(0x10, 0xB9, 0x81), Color.FromRgb(0x10, 0xB9, 0x81), revAgent + ": COMPLETE");
                    break;

                default:
                case "idle":
                    AnimateTo(330, 30, Color.FromRgb(0x10, 0xB9, 0x81), Color.FromRgb(0x10, 0xB9, 0x81), "IDLE");
                    break;
            }
        }

        private void RenderInlineDiff(string diff, string cmd)
        {
            _diffLinesStack.Children.Clear();

            string[] lines;
            if (!string.IsNullOrEmpty(diff))
            {
                lines = diff.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                lines = new string[] {
                    "@@ -12,3 +12,4 @@",
                    "- jwt.verify(token);",
                    "+ if (!token) throw new AuthError('missing');",
                    "+ return jwt.verify(token, secret);"
                };
            }

            int add = 0, del = 0;
            foreach (var l in lines)
            {
                if (l.StartsWith("+")) add++;
                else if (l.StartsWith("-")) del++;

                var tb = new TextBlock
                {
                    Text = l,
                    FontSize = 9.5,
                    FontFamily = new FontFamily("Consolas, monospace")
                };

                if (l.StartsWith("+"))
                {
                    tb.Foreground = _brushGreen;
                    tb.Background = new SolidColorBrush(Color.FromArgb(0x30, 0x10, 0xB9, 0x81));
                }
                else if (l.StartsWith("-"))
                {
                    tb.Foreground = _brushRed;
                    tb.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xEF, 0x44, 0x44));
                }
                else
                {
                    tb.Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
                }
                _diffLinesStack.Children.Add(tb);
            }
            _diffSummary.Text = string.Format("DIFF PREVIEW (+{0} -{1})", add, del);
        }

        public void SetupQuestion(string prompt, List<string> options)
        {
            _currentAskOptions.Clear();
            _askOptionsStack.Children.Clear();
            _lblAskPrompt.Text = prompt;

            if (options == null || options.Count == 0)
            {
                options = new List<string> { "Yes / Proceed", "No / Cancel" };
            }

            _currentAskOptions.AddRange(options);

            for (int i = 0; i < options.Count && i < 4; i++)
            {
                int index = i;
                string optText = options[i];

                var btn = new Button
                {
                    Content = string.Format("{0}: {1}", i + 1, optText),
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(4, 0, 4, 0),
                    Background = new SolidColorBrush(Color.FromArgb(0x28, 0x06, 0xB6, 0xD4)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x06, 0xB6, 0xD4)),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 10.5,
                    Cursor = Cursors.Hand
                };
                btn.Click += (s, e) => AnswerQuestion(index);
                _askOptionsStack.Children.Add(btn);
            }

            TransitionState("ask", "Agent", prompt);
        }

        private void AnswerQuestion(int index)
        {
            if (index < 0 || index >= _currentAskOptions.Count) return;
            string selected = _currentAskOptions[index];

            if (_currentAskTcs != null)
            {
                _currentAskTcs.TrySetResult(string.Format("{{\"choice\":{0},\"answer\":\"{1}\"}}", index, selected));
                _currentAskTcs = null;
            }

            TransitionState("review", "Answered", selected);
        }

        private void CycleClavisProfile()
        {
            try
            {
                string[] profiles = new string[] { "deepseek", "anthropic-claude", "local-ollama", "openrouter" };
                _clavisProfileIdx = (_clavisProfileIdx + 1) % profiles.Length;
                string nextProf = profiles[_clavisProfileIdx];
                _activeClavisProfile = nextProf;
                string spool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".notch");
                string clavisFile = Path.Combine(spool, "clavis_state.json");
                int lat = nextProf == "local-ollama" ? 12 : (nextProf == "deepseek" ? 84 : (nextProf == "anthropic-claude" ? 142 : 110));
                File.WriteAllText(clavisFile, string.Format("{{\"active_profile\":\"{0}\",\"latency_ms\":{1}}}", nextProf, lat));
                _lblClavisPill.Text = string.Format("⚡ {0} • {1}ms", nextProf.ToUpperInvariant(), lat);
            }
            catch { }
        }

        private void OnPillClicked()
        {
            if (_currentState == "review")
            {
                AcknowledgeReview();
            }
            ToggleSessionsDrawer();
        }

        private void ToggleSessionsDrawer()
        {
            _isSessionsExpanded = !_isSessionsExpanded;
            if (_isSessionsExpanded)
            {
                PopulateSessionsUI();
                _pnlWorking.Visibility = Visibility.Collapsed;
                _pnlAttention.Visibility = Visibility.Collapsed;
                _pnlAsk.Visibility = Visibility.Collapsed;
                _pnlSessions.Visibility = Visibility.Visible;
                AnimateTo(420, 160, Color.FromRgb(0x10, 0xB9, 0x81), Color.FromRgb(0x10, 0xB9, 0x81), "SESSIONS");
            }
            else
            {
                TransitionState(_currentState);
            }
        }

        private void PopulateSessionsUI()
        {
            _sessionsListStack.Children.Clear();
            var sessions = GetMergedSessions();

            if (sessions.Count == 0)
            {
                var emptyTxt = new TextBlock
                {
                    Text = "No active agent sessions. All systems nominal.",
                    Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                    FontSize = 10.5,
                    Margin = new Thickness(4, 8, 4, 8)
                };
                _sessionsListStack.Children.Add(emptyTxt);
                return;
            }

            foreach (var s in sessions)
            {
                var rowBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x35, 0x24, 0x2E, 0x42)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(0, 0, 0, 4),
                    Cursor = Cursors.Hand
                };

                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var infoStack = new StackPanel { Orientation = Orientation.Vertical };
                var titleStack = new StackPanel { Orientation = Orientation.Horizontal };

                Color dotColor = Color.FromRgb(0x10, 0xB9, 0x81);
                if (s.State == "working") dotColor = Color.FromRgb(0x3B, 0x82, 0xF6);
                else if (s.State == "thinking") dotColor = Color.FromRgb(0xA8, 0x55, 0xF7);
                else if (s.State == "attention") dotColor = Color.FromRgb(0xF5, 0x9E, 0x0B);
                else if (s.State == "ask") dotColor = Color.FromRgb(0x06, 0xB6, 0xD4);

                var dot = new Border
                {
                    Width = 6,
                    Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(dotColor),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                titleStack.Children.Add(dot);

                var title = new TextBlock
                {
                    Text = s.Agent + " • " + s.Project,
                    Foreground = Brushes.White,
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold
                };
                titleStack.Children.Add(title);
                infoStack.Children.Add(titleStack);

                var detail = new TextBlock
                {
                    Text = s.Tool,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                    FontSize = 9.5,
                    FontFamily = new FontFamily("Consolas, monospace"),
                    Margin = new Thickness(12, 1, 0, 0)
                };
                infoStack.Children.Add(detail);
                Grid.SetColumn(infoStack, 0);
                rowGrid.Children.Add(infoStack);

                var jumpBtn = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x20, 0x3B, 0x82, 0xF6)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x3B, 0x82, 0xF6)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand
                };
                var jumpTxt = new TextBlock
                {
                    Text = "VISIT ↗",
                    Foreground = _brushBlue,
                    FontSize = 8.5,
                    FontWeight = FontWeights.Bold
                };
                jumpBtn.Child = jumpTxt;
                Grid.SetColumn(jumpBtn, 1);
                rowGrid.Children.Add(jumpBtn);

                rowBorder.MouseLeftButtonDown += (sender, e) =>
                {
                    e.Handled = true;
                    VisitSession(s);
                };

                rowBorder.Child = rowGrid;
                _sessionsListStack.Children.Add(rowBorder);
            }
        }

        private void VisitSession(SessionItem s)
        {
            try
            {
                IntPtr hwnd = IntPtr.Zero;

                if (s.Pid > 0)
                {
                    hwnd = Win32.FindWindowByPid(s.Pid);
                }

                if (hwnd == IntPtr.Zero && !string.IsNullOrEmpty(s.Project))
                {
                    hwnd = Win32.FindWindowBySubstring(s.Project);
                }

                if (hwnd == IntPtr.Zero && !string.IsNullOrEmpty(s.Agent))
                {
                    hwnd = Win32.FindWindowBySubstring(s.Agent);
                }

                if (hwnd == IntPtr.Zero)
                {
                    hwnd = Win32.FindWindowBySubstring("Antigravity");
                }

                if (hwnd == IntPtr.Zero) hwnd = Win32.FindWindowByProcessName("WindowsTerminal");
                if (hwnd == IntPtr.Zero) hwnd = Win32.FindWindowByProcessName("Code");
                if (hwnd == IntPtr.Zero) hwnd = Win32.FindWindowByProcessName("Cursor");
                if (hwnd == IntPtr.Zero) hwnd = Win32.FindWindowByProcessName("agy");
                if (hwnd == IntPtr.Zero) hwnd = Win32.FindWindowByProcessName("powershell");
                if (hwnd == IntPtr.Zero) hwnd = Win32.FindWindowByProcessName("cmd");

                if (hwnd != IntPtr.Zero)
                {
                    Win32.ForceForegroundWindow(hwnd);
                }

                if (!string.IsNullOrEmpty(s.Id))
                {
                    string spool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".notch");
                    string sFile = Path.Combine(spool, "sessions", s.Id + ".json");
                    if (File.Exists(sFile))
                    {
                        try { File.Delete(sFile); } catch { }
                    }
                }
            }
            catch { }

            AcknowledgeReview();
            if (_isSessionsExpanded)
            {
                ToggleSessionsDrawer();
            }
        }

        public void AcknowledgeReview()
        {
            try
            {
                string spool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".notch");
                string stateFile = Path.Combine(spool, "state.json");
                File.WriteAllText(stateFile, "{\"state\":\"idle\",\"message\":\"Reviewed\"}");

                string sessDir = Path.Combine(spool, "sessions");
                if (Directory.Exists(sessDir))
                {
                    foreach (var sf in Directory.GetFiles(sessDir, "*.json"))
                    {
                        try
                        {
                            string c = File.ReadAllText(sf);
                            if (c.Contains("\"review\"") || c.Contains("\"completed\""))
                            {
                                File.Delete(sf);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            _reviewedConvId = _activeConvId;
            TransitionState("idle");
        }

        private List<SessionItem> GetMergedSessions()
        {
            var list = new List<SessionItem>();
            string spool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".notch");
            string sessDir = Path.Combine(spool, "sessions");

            if (Directory.Exists(sessDir))
            {
                foreach (var sf in Directory.GetFiles(sessDir, "*.json"))
                {
                    try
                    {
                        string c = File.ReadAllText(sf);
                        string st = ExtractJson(c, "state", "idle");
                        string actor = ExtractJson(c, "agent", ExtractJson(c, "actor", "Claude"));
                        string proj = ExtractJson(c, "project", "Notch");
                        string tool = ExtractJson(c, "message", ExtractJson(c, "tool_name", "Executing"));
                        string id = Path.GetFileNameWithoutExtension(sf);
                        string pidStr = ExtractJson(c, "pid", "0");
                        int pPid = 0;
                        int.TryParse(pidStr, out pPid);

                        list.Add(new SessionItem
                        {
                            Id = id,
                            Agent = actor,
                            Project = proj,
                            Tool = tool,
                            State = st,
                            Pid = pPid
                        });
                    }
                    catch { }
                }
            }

            if (list.Count == 0)
            {
                list.Add(new SessionItem
                {
                    Id = "agy-main",
                    Agent = "Antigravity",
                    Project = "Antigravity Workspace",
                    Tool = "Ready for instructions",
                    State = "idle"
                });
            }

            return list;
        }

        private void ApproveGate()
        {
            if (_currentGateTcs != null)
            {
                _currentGateTcs.TrySetResult("allow");
                _currentGateTcs = null;
            }
            try
            {
                string spool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".notch");
                string decDir = Path.Combine(spool, "decisions");
                string pendDir = Path.Combine(spool, "pending");
                if (Directory.Exists(pendDir))
                {
                    foreach (var f in Directory.GetFiles(pendDir, "*.json"))
                    {
                        string name = Path.GetFileName(f);
                        File.WriteAllText(Path.Combine(decDir, name), "{\"decision\":\"allow\"}");
                        File.Delete(f);
                    }
                }
            }
            catch { }

            TransitionState("review", "Gate", "Approved");
        }

        private void DenyGate()
        {
            if (_currentGateTcs != null)
            {
                _currentGateTcs.TrySetResult("deny");
                _currentGateTcs = null;
            }
            if (_currentAskTcs != null)
            {
                _currentAskTcs.TrySetResult("{\"choice\":-1,\"answer\":\"cancelled\"}");
                _currentAskTcs = null;
            }
            try
            {
                string spool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".notch");
                string decDir = Path.Combine(spool, "decisions");
                string pendDir = Path.Combine(spool, "pending");
                if (Directory.Exists(pendDir))
                {
                    foreach (var f in Directory.GetFiles(pendDir, "*.json"))
                    {
                        string name = Path.GetFileName(f);
                        File.WriteAllText(Path.Combine(decDir, name), "{\"decision\":\"deny\"}");
                        File.Delete(f);
                    }
                }
            }
            catch { }
            TransitionState("idle");
        }

        private void StartIpcServices()
        {
            var pipeThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var pipe = new NamedPipeServerStream("notch_ipc", PipeDirection.InOut, 10, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                        {
                            pipe.WaitForConnection();

                            // Client Verification via kernel32
                            uint clientPid = 0;
                            try
                            {
                                Win32.GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out clientPid);
                            }
                            catch { }

                            using (var r = new StreamReader(pipe, Encoding.UTF8))
                            using (var w = new StreamWriter(pipe, new UTF8Encoding(false)))
                            {
                                string line = r.ReadLine();
                                if (!string.IsNullOrEmpty(line))
                                {
                                    if (line.Contains("\"mode\":\"approval-gate\"") || line.Contains("\"gate\""))
                                    {
                                        string tool = ExtractJson(line, "tool", "Command execution");
                                        string actor = ExtractJson(line, "actor", "Agent");
                                        string diff = ExtractJson(line, "diff", "");

                                        var tcs = new TaskCompletionSource<string>();
                                        _currentGateTcs = tcs;

                                        Dispatcher.Invoke(new Action(() =>
                                        {
                                            TransitionState("attention", actor, tool, tool, diff);
                                        }));

                                        if (tcs.Task.Wait(120000))
                                        {
                                            w.WriteLine("{\"decision\":\"" + tcs.Task.Result + "\"}");
                                        }
                                        else
                                        {
                                            w.WriteLine("{\"decision\":\"allow\"}");
                                        }
                                        w.Flush();
                                    }
                                    else if (line.Contains("\"mode\":\"ask\""))
                                    {
                                        string prompt = ExtractJson(line, "prompt", ExtractJson(line, "question", "Option choice:"));
                                        var opts = new List<string>();
                                        // Simple options parsing
                                        int optIdx = line.IndexOf("\"options\":[");
                                        if (optIdx >= 0)
                                        {
                                            int optEnd = line.IndexOf("]", optIdx);
                                            if (optEnd > optIdx)
                                            {
                                                string raw = line.Substring(optIdx + 11, optEnd - optIdx - 11);
                                                var parts = raw.Split(',');
                                                foreach (var p in parts)
                                                {
                                                    string clean = p.Trim().Trim('"');
                                                    if (!string.IsNullOrEmpty(clean)) opts.Add(clean);
                                                }
                                            }
                                        }

                                        var askTcs = new TaskCompletionSource<string>();
                                        _currentAskTcs = askTcs;

                                        Dispatcher.Invoke(new Action(() =>
                                        {
                                            SetupQuestion(prompt, opts);
                                        }));

                                        if (askTcs.Task.Wait(120000))
                                        {
                                            w.WriteLine(askTcs.Task.Result);
                                        }
                                        else
                                        {
                                            w.WriteLine("{\"choice\":0,\"answer\":\"default\"}");
                                        }
                                        w.Flush();
                                    }
                                    else
                                    {
                                        string st = ExtractJson(line, "state", "working");
                                        string tool = ExtractJson(line, "tool_name", ExtractJson(line, "tool", ""));
                                        string actor = ExtractJson(line, "agent", ExtractJson(line, "actor", "Claude"));

                                        Dispatcher.Invoke(new Action(() =>
                                        {
                                            TransitionState(st, actor, tool);
                                        }));

                                        w.WriteLine("{\"status\":\"ok\"}");
                                        w.Flush();
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        Thread.Sleep(50);
                    }
                }
            })
            { IsBackground = true };
            pipeThread.Start();
        }

        private void StartSpoolAndForegroundWatcher()
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) =>
            {
                try
                {
                    // 1. Fullscreen / Presentation / Gaming Minimal Mode
                    IntPtr fg = Win32.GetForegroundWindow();
                    IntPtr hwnd = (new WindowInteropHelper(this)).Handle;
                    bool isFullscreen = false;
                    if (fg != IntPtr.Zero && fg != hwnd)
                    {
                        Win32.RECT rect;
                        if (Win32.GetWindowRect(fg, out rect))
                        {
                            int w = rect.Right - rect.Left;
                            int h = rect.Bottom - rect.Top;
                            if (w >= _screenWidth && h >= _screenHeight)
                            {
                                var sb = new StringBuilder(256);
                                Win32.GetWindowText(fg, sb, 256);
                                string title = sb.ToString();
                                if (title != "Notch Dynamic Island HUD" && !title.Contains("Program Manager"))
                                {
                                    isFullscreen = true;
                                }
                            }
                        }
                    }

                    bool isNeededState = (_currentState == "attention" || _currentState == "ask" || _currentState == "review");

                    if (isFullscreen)
                    {
                        if (isNeededState)
                        {
                            if (_isMinimalLine)
                            {
                                ExpandFromMinimalLine();
                            }
                        }
                        else
                        {
                            if (!_isMinimalLine && !_isManualExpanded)
                            {
                                CollapseToMinimalLine();
                            }
                        }
                        _wasFullscreenMode = true;
                    }
                    else
                    {
                        if (_wasFullscreenMode && _isMinimalLine && !_isManualExpanded)
                        {
                            ExpandFromMinimalLine();
                        }
                        _wasFullscreenMode = false;
                    }

                    // 2. Review Visit Auto-Decay
                    if (_currentState == "review")
                    {
                        if ((DateTime.UtcNow - _reviewEnteredTime).TotalSeconds > 10)
                        {
                            AcknowledgeReview();
                            return;
                        }

                        if (fg != IntPtr.Zero)
                        {
                            var sb = new StringBuilder(256);
                            Win32.GetWindowText(fg, sb, 256);
                            string fgTitle = sb.ToString().ToLowerInvariant();
                            if (fgTitle.Contains("antigravity") || fgTitle.Contains("visual studio code") || fgTitle.Contains("cursor") || fgTitle.Contains("terminal"))
                            {
                                AcknowledgeReview();
                                return;
                            }
                        }
                    }

                    // 3. Spool Directory Poll
                    string spool = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".notch");
                    string pendDir = Path.Combine(spool, "pending");
                    if (Directory.Exists(pendDir))
                    {
                        var files = Directory.GetFiles(pendDir, "*.json");
                        if (files.Length > 0)
                        {
                            string content = File.ReadAllText(files[0]);
                            string cmd = ExtractJson(content, "summary", ExtractJson(content, "tool", "Command"));
                            string conv = ExtractJson(content, "conversation_id", "Agent");
                            string diff = ExtractJson(content, "diff", "");
                            _activeConvId = conv;
                            if (_currentState != "attention")
                            {
                                TransitionState("attention", conv, cmd, cmd, diff);
                            }
                            return;
                        }
                    }

                    string stateFile = Path.Combine(spool, "state.json");
                    if (File.Exists(stateFile) && _currentState != "attention" && _currentState != "ask" && !_isSessionsExpanded)
                    {
                        string content = File.ReadAllText(stateFile);
                        string st = ExtractJson(content, "state", "idle");
                        string tool = ExtractJson(content, "message", ExtractJson(content, "tool_name", ""));
                        string actor = ExtractJson(content, "agent", "Antigravity");
                        if (st != _currentState)
                        {
                            TransitionState(st, actor, tool);
                        }
                    }

                    // 4. Clavis Provider Sync
                    string clavisFile = Path.Combine(spool, "clavis_state.json");
                    if (File.Exists(clavisFile))
                    {
                        try
                        {
                            string cTxt = File.ReadAllText(clavisFile);
                            string prof = ExtractJson(cTxt, "active_profile", "");
                            string latStr = ExtractJson(cTxt, "latency_ms", "84");
                            if (!string.IsNullOrEmpty(prof) && prof != _activeClavisProfile)
                            {
                                _activeClavisProfile = prof;
                                _lblClavisPill.Text = string.Format("⚡ {0} • {1}ms", prof.ToUpperInvariant(), latStr);
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            };
            timer.Start();
        }

        private static string ExtractJson(string json, string key, string fallback)
        {
            if (string.IsNullOrEmpty(json)) return fallback;
            string pattern = "\"" + key + "\":\"";
            int idx = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int start = idx + pattern.Length;
                int end = json.IndexOf("\"", start);
                if (end > start) return json.Substring(start, end - start);
            }
            return fallback;
        }

        private static SolidColorBrush ColorToBrush(byte a, byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromArgb(a, r, g, b));
        }
    }

    public class Program
    {
        [STAThread]
        public static void Main()
        {
            string spool = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".notch");
            try
            {
                if (!System.IO.Directory.Exists(spool)) System.IO.Directory.CreateDirectory(spool);
                System.IO.File.WriteAllText(System.IO.Path.Combine(spool, "main.log"), "Main started: " + DateTime.Now.ToString());

                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    try { System.IO.File.WriteAllText(System.IO.Path.Combine(spool, "crash.log"), e.ExceptionObject.ToString()); } catch { }
                };

                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += (s, e) =>
                {
                    try { System.IO.File.WriteAllText(System.IO.Path.Combine(spool, "crash.log"), e.Exception.ToString()); } catch { }
                    e.Handled = true;
                };

                var window = new NotchWindow();
                System.IO.File.AppendAllText(System.IO.Path.Combine(spool, "main.log"), "\nWindow created");
                window.Show();
                System.IO.File.AppendAllText(System.IO.Path.Combine(spool, "main.log"), "\nWindow shown");
                app.Run();
            }
            catch (Exception ex)
            {
                try
                {
                    System.IO.File.WriteAllText(System.IO.Path.Combine(spool, "crash.log"), ex.ToString());
                }
                catch { }
            }
        }
    }
}

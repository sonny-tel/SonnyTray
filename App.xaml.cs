using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using SonnyTray.Services;
using SonnyTray.ViewModels;

namespace SonnyTray;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private TailscaleClient? _client;
    private MainViewModel? _mainVm;
    private MainWindow? _popout;
    internal static bool SuppressDeactivate;

    // Context menu items that need dynamic updates
    private MenuItem? _profileMenuItem;
    private MenuItem? _adminConsoleMenuItem;
    private MenuItem? _connectMenuItem;
    private MenuItem? _loginMenuItem;
    private MenuItem? _exitNodeMenuItem;
    private MenuItem? _runAsExitNodeItem;
    private MenuItem? _ipMenuItem;
    private MenuItem? _copyIPv4Item;
    private MenuItem? _copyIPv6Item;
    private MenuItem? _copyHostItem;
    private MenuItem? _copyFqdnItem;
    private MenuItem? _tailscaleVersionItem;

    private System.Windows.Threading.DispatcherTimer? _iconAnimTimer;
    private int _animFrame;
    private IntPtr _prevIconHandle;
    private string _lastProfilePicUrl = "";
    private System.Windows.Media.Imaging.BitmapImage? _cachedProfilePic;

    private static readonly (int X, int Y)[] DotCenters =
    [
        (6, 6),  (6, 16),  (6, 26),
        (16, 6), (16, 16), (16, 26),
        (26, 6), (26, 16), (26, 26),
    ];

    private static readonly int[] AllDots = [0, 1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly int[] TDots = [0, 3, 4, 5, 6];

    private static readonly int[][] AnimFrames =
    [
        [4],
        [3, 4, 5],
        [0, 3, 4, 5, 6],
        [],
    ];

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ThemeManager.ApplyTheme(AppThemeMode.System);

        _client = new TailscaleClient();
        _mainVm = new MainViewModel(_client);

        _trayIcon = new TaskbarIcon
        {
            Icon = CreateTrayIcon(),
            ToolTipText = "SonnyTray",
            NoLeftClickDelay = true,
        };

        _trayIcon.TrayLeftMouseUp += OnTrayLeftClick;
        _trayIcon.ContextMenu = BuildContextMenu();

        _trayIcon.ForceCreate();

        // Pre-create the popout window so XAML parsing + JIT happens now, not on first click.
        _popout = new MainWindow { DataContext = _mainVm };
        _popout.Deactivated += (_, _) => { if (!SuppressDeactivate) HidePopout(); };

        await _mainVm.InitializeAsync();

        // Update tray menu + tooltip when state changes
        _mainVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.BackendState)
                or nameof(MainViewModel.IsConnected)
                or nameof(MainViewModel.SelfIP)
                or nameof(MainViewModel.CurrentExitNodeName)
                or nameof(MainViewModel.CurrentExitNodeId)
                or nameof(MainViewModel.TailnetName)
                or nameof(MainViewModel.NeedsLogin)
                or nameof(MainViewModel.IsBusy))
            {
                UpdateContextMenu();
                UpdateTooltip();
                UpdateTrayIcon();
            }

            // Show a balloon notification when login is required
            if (args.PropertyName is nameof(MainViewModel.NeedsLogin) && _mainVm.NeedsLogin)
            {
                _trayIcon?.ShowNotification(
                    "SonnyTray",
                    "You're not signed in to Tailscale. Click here to log in.",
                    H.NotifyIcon.Core.NotificationIcon.Info);
            }
        };
        _mainVm.ProfileManager.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ProfileManagerViewModel.ProfilePicUrl)
                or nameof(ProfileManagerViewModel.UserDisplayName)
                or nameof(ProfileManagerViewModel.ControlHostName))
            {
                UpdateContextMenu();
            }
        };
        ThemeManager.ThemeChanged += RefreshContextMenuTheme;
        UpdateContextMenu();
        UpdateTooltip();
        UpdateTrayIcon();

        // If not signed in at startup, open the popout so the user sees the login prompt
        if (_mainVm.NeedsLogin)
        {
            ShowPopout();
            _trayIcon?.ShowNotification(
                "SonnyTray",
                "You're not signed in to Tailscale. Click the tray icon to log in.",
                H.NotifyIcon.Core.NotificationIcon.Info);
        }
    }

    private ContextMenu BuildContextMenu()
    {
        // Profile header click anywhere to open popout
        _profileMenuItem = new MenuItem { Padding = new Thickness(12, 10, 12, 10) };
        _profileMenuItem.Click += (_, _) => ShowPopout();

        _adminConsoleMenuItem = new MenuItem { Header = "Admin Console", Icon = MenuIcon("\uE774") };
        _adminConsoleMenuItem.Click += (_, _) => _mainVm?.OpenAdminConsoleCommand.Execute(null);

        _connectMenuItem = new MenuItem { Header = "Connect", Icon = MenuIcon("\uE836") };
        _connectMenuItem.Click += async (_, _) =>
        {
            if (_mainVm is not null)
                await _mainVm.ToggleConnectionCommand.ExecuteAsync(null);
        };

        _loginMenuItem = new MenuItem { Header = "Log in...", Icon = MenuIcon("\uE7E8") };
        _loginMenuItem.Click += async (_, _) =>
        {
            if (_mainVm is not null)
                await _mainVm.LoginCommand.ExecuteAsync(null);
        };

        _exitNodeMenuItem = new MenuItem { Header = "Exit Node: None", Icon = MenuIcon("\uE968") };
        _exitNodeMenuItem.Click += (_, _) =>
        {
            if (_mainVm is not null)
            {
                _mainVm.ShowSettings = false;
                _mainVm.ShowExitNodePicker = true;
            }
            ShowPopout();
        };

        _runAsExitNodeItem = new MenuItem { Header = "Run exit node", Icon = MenuIcon("\uE8AB") };
        _runAsExitNodeItem.Click += (_, _) =>
        {
            if (_mainVm is not null)
                _mainVm.Settings.AdvertiseExitNode = !_mainVm.Settings.AdvertiseExitNode;
        };

        _ipMenuItem = new MenuItem { Header = "Copy", Icon = MenuIcon("\uE8C8") };
        _copyIPv4Item = new MenuItem { Header = "IPv4" };
        _copyIPv4Item.Click += (_, _) => CopyToClipboard(_mainVm?.SelfIP);
        _copyIPv6Item = new MenuItem { Header = "IPv6" };
        _copyIPv6Item.Click += (_, _) => CopyToClipboard(_mainVm?.SelfIPv6);
        _copyHostItem = new MenuItem { Header = "Hostname" };
        _copyHostItem.Click += (_, _) => CopyToClipboard(_mainVm?.SelfHostName);
        _copyFqdnItem = new MenuItem { Header = "FQDN" };
        _copyFqdnItem.Click += (_, _) => CopyToClipboard(_mainVm?.SelfDNSName);
        _ipMenuItem.Items.Add(_copyIPv4Item);
        _ipMenuItem.Items.Add(_copyIPv6Item);
        _ipMenuItem.Items.Add(_copyHostItem);
        _ipMenuItem.Items.Add(_copyFqdnItem);

        var exitItem = new MenuItem { Header = "Quit", Icon = MenuIcon("\uE894") };
        exitItem.Click += (_, _) => Shutdown();

        var menu = new ContextMenu();
        menu.Items.Add(_profileMenuItem);

        // Version info (non-interactive)
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var infoVer = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // Strip the +commitsha suffix if present
        var appVerStr = infoVer?.Split('+')[0] ?? asm.GetName().Version?.ToString(3) ?? "0.0.1";
        var sonnyVerItem = new MenuItem
        {
            Header = $"SonnyTray v{appVerStr}",
            IsEnabled = false,
            IsHitTestVisible = false,
            Focusable = false,
            Padding = new Thickness(12, 2, 12, 2),
        };
        _tailscaleVersionItem = new MenuItem
        {
            Header = "Tailscale",
            IsEnabled = false,
            IsHitTestVisible = false,
            Focusable = false,
            Padding = new Thickness(12, 2, 12, 6),
        };
        menu.Items.Add(sonnyVerItem);
        menu.Items.Add(_tailscaleVersionItem);

        menu.Items.Add(new Separator { Style = FindResource("MenuSeparator") as Style });
        menu.Items.Add(_adminConsoleMenuItem);
        menu.Items.Add(_connectMenuItem);
        menu.Items.Add(_exitNodeMenuItem);
        menu.Items.Add(_runAsExitNodeItem);
        menu.Items.Add(new Separator { Style = FindResource("MenuSeparator") as Style });
        menu.Items.Add(_ipMenuItem);
        var settingsItem = new MenuItem { Header = "Settings", Icon = MenuIcon("\uE713") };
        settingsItem.Click += (_, _) =>
        {
            if (_mainVm is not null)
            {
                _mainVm.ShowExitNodePicker = false;
                _mainVm.ShowSettings = true;
            }
            ShowPopout();
        };
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator { Style = FindResource("MenuSeparator") as Style });
        menu.Items.Add(_loginMenuItem);
        menu.Items.Add(exitItem);

        return menu;
    }

    private void UpdateContextMenu()
    {
        if (_mainVm is null) return;

        var connected = _mainVm.IsConnected;
        var needsLogin = _mainVm.NeedsLogin;

        if (_profileMenuItem is not null)
        {
            var pm = _mainVm.ProfileManager;
            var displayName = pm.UserDisplayName;
            var tailnet = _mainVm.TailnetName;
            var controlHost = pm.ControlHostName;

            // Profile picture
            var picUrl = pm.ProfilePicUrl;
            var picBorder = new Border
            {
                CornerRadius = new CornerRadius(12),
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush"),
            };
            if (!string.IsNullOrEmpty(picUrl))
            {
                // Only re-download if the URL changed
                if (picUrl != _lastProfilePicUrl || _cachedProfilePic is null)
                {
                    _lastProfilePicUrl = picUrl;
                    try
                    {
                        var bmp = new System.Windows.Media.Imaging.BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(picUrl, UriKind.Absolute);
                        bmp.DecodePixelWidth = 48;
                        bmp.DecodePixelHeight = 48;
                        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bmp.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
                        bmp.EndInit();
                        if (bmp.IsDownloading)
                        {
                            bmp.DownloadCompleted += (_, _) => Dispatcher.BeginInvoke(UpdateContextMenu);
                        }
                        else
                        {
                            bmp.Freeze();
                        }
                        _cachedProfilePic = bmp;
                    }
                    catch
                    {
                        _cachedProfilePic = null;
                    }
                }

                if (_cachedProfilePic is not null)
                {
                    var inner = new Border { CornerRadius = new CornerRadius(12) };
                    inner.Background = new System.Windows.Media.ImageBrush(_cachedProfilePic)
                    {
                        Stretch = System.Windows.Media.Stretch.UniformToFill,
                    };
                    picBorder.Child = inner;
                }
            }
            else
            {
                _lastProfilePicUrl = "";
                _cachedProfilePic = null;
            }

            // Text content
            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
            nameLine.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(displayName) ? "SonnyTray" : displayName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
            });
            if (!string.IsNullOrEmpty(tailnet))
            {
                nameLine.Children.Add(new TextBlock
                {
                    Text = $" · {tailnet}",
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                    FontSize = 13,
                });
            }
            textPanel.Children.Add(nameLine);

            var statusText = connected ? "Connected" : _mainVm.BackendState switch
            {
                "NeedsLogin" => "Needs Login",
                "Starting" => "Starting...",
                "NoState" => "Logged Out",
                _ => "Disconnected"
            };
            var statusLine = !string.IsNullOrEmpty(controlHost)
                ? $"{statusText} · {controlHost}"
                : statusText;
            textPanel.Children.Add(new TextBlock
            {
                Text = statusLine,
                Foreground = (System.Windows.Media.Brush)FindResource("TextTertiaryBrush"),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(picBorder);
            row.Children.Add(textPanel);

            _profileMenuItem.Header = row;
        }

        if (_tailscaleVersionItem is not null)
        {
            var tsVer = _mainVm.Version;
            _tailscaleVersionItem.Header = string.IsNullOrEmpty(tsVer)
                ? "Tailscale"
                : $"Tailscale {tsVer}";
        }

        if (_connectMenuItem is not null)
        {
            _connectMenuItem.Header = connected ? "Disconnect" : "Connect";
            _connectMenuItem.Visibility = needsLogin ? Visibility.Collapsed : Visibility.Visible;
        }

        if (_loginMenuItem is not null)
        {
            _loginMenuItem.Header = needsLogin ? "Log in..." : "Log out";
            _loginMenuItem.Click -= LoginOrLogoutClick;
            _loginMenuItem.Click += LoginOrLogoutClick;
        }

        if (_exitNodeMenuItem is not null)
        {
            var cc = _mainVm.CurrentExitNodeCountryCode;
            var name = _mainVm.CurrentExitNodeName;
            if (!string.IsNullOrEmpty(cc))
            {
                var flag = ExitNodePickerViewModel.CountryCodeToFlag(cc);
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new Emoji.Wpf.TextBlock
                {
                    Text = flag,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                });
                sp.Children.Add(new TextBlock
                {
                    Text = name,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                _exitNodeMenuItem.Header = sp;
            }
            else
            {
                _exitNodeMenuItem.Header = $"Exit Node: {name}";
            }
        }

        if (_runAsExitNodeItem is not null)
        {
            var isExitNode = _mainVm.Settings.AdvertiseExitNode;
            _runAsExitNodeItem.Header = isExitNode ? "Stop running exit node" : "Run exit node";
            _runAsExitNodeItem.FontWeight = isExitNode ? FontWeights.SemiBold : FontWeights.Normal;
        }

        if (_ipMenuItem is not null && _mainVm is not null)
        {
            var hasAny = !string.IsNullOrEmpty(_mainVm.SelfIP);
            _ipMenuItem.IsEnabled = hasAny;

            if (_copyIPv4Item is not null)
            {
                var v4 = _mainVm.SelfIP;
                _copyIPv4Item.Header = string.IsNullOrEmpty(v4) ? "IPv4" : v4;
                _copyIPv4Item.IsEnabled = !string.IsNullOrEmpty(v4);
            }
            if (_copyIPv6Item is not null)
            {
                var v6 = _mainVm.SelfIPv6;
                _copyIPv6Item.Header = string.IsNullOrEmpty(v6) ? "IPv6" : v6;
                _copyIPv6Item.IsEnabled = !string.IsNullOrEmpty(v6);
            }
            if (_copyHostItem is not null)
            {
                var host = _mainVm.SelfHostName;
                _copyHostItem.Header = string.IsNullOrEmpty(host) ? "Hostname" : host;
                _copyHostItem.IsEnabled = !string.IsNullOrEmpty(host);
            }
            if (_copyFqdnItem is not null)
            {
                var fqdn = _mainVm.SelfDNSName;
                _copyFqdnItem.Header = string.IsNullOrEmpty(fqdn) ? "FQDN" : fqdn;
                _copyFqdnItem.IsEnabled = !string.IsNullOrEmpty(fqdn);
            }
        }
    }

    private async void LoginOrLogoutClick(object sender, RoutedEventArgs e)
    {
        if (_mainVm is null) return;
        if (_mainVm.NeedsLogin || _mainVm.BackendState == "Stopped")
            await _mainVm.LoginCommand.ExecuteAsync(null);
        else
            await _mainVm.LogoutCommand.ExecuteAsync(null);
    }

    private void RefreshContextMenuTheme()
    {
        if (_trayIcon?.ContextMenu is not { } menu) return;

        var menuStyle = FindResource(typeof(ContextMenu)) as Style;
        var itemStyle = FindResource(typeof(MenuItem)) as Style;
        var sepStyle = FindResource("MenuSeparator") as Style;

        if (menuStyle is not null) menu.Style = menuStyle;
        foreach (var item in menu.Items)
        {
            if (item is MenuItem mi && itemStyle is not null)
            {
                mi.Style = itemStyle;
                foreach (var sub in mi.Items.OfType<MenuItem>())
                    sub.Style = itemStyle;
            }
            else if (item is Separator sep && sepStyle is not null)
            {
                sep.Style = sepStyle;
            }
        }

        UpdateContextMenu();
    }

    private TextBlock MenuIcon(string glyph) => new()
    {
        Text = glyph,
        FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont"),
        FontSize = 14,
    };

    private static void CopyToClipboard(string? text)
    {
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    private void UpdateTooltip()
    {
        if (_trayIcon is null || _mainVm is null) return;
        if (_mainVm.NeedsLogin)
        {
            _trayIcon.ToolTipText = "SonnyTray: Not signed in";
            return;
        }
        var state = _mainVm.IsConnected ? "Connected" : "Disconnected";
        var tailnet = string.IsNullOrEmpty(_mainVm.TailnetName) ? "" : $"\n{_mainVm.TailnetName}";
        _trayIcon.ToolTipText = $"SonnyTray: {state}{tailnet}";
    }

    private void OnTrayLeftClick(object sender, RoutedEventArgs e)
    {
        if (_popout is not null && _popout.IsVisible)
            HidePopout();
        else
            ShowPopout();
    }

    private void ShowPopout()
    {
        if (_popout is null) return;
        PositionPopoutNearTray(_popout);
        _popout.Show();
        _popout.Activate();
    }

    private void HidePopout()
    {
        _popout?.Hide();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private void PositionPopoutNearTray(Window window)
    {
        var workArea = SystemParameters.WorkArea;

        // Get cursor position (in physical pixels) it's on the tray icon at click time
        double cursorX = workArea.Right - window.Width / 2;
        if (GetCursorPos(out var pt))
        {
            var source = PresentationSource.FromVisual(window);
            double scaleX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
            cursorX = pt.X * scaleX;
        }

        // Center horizontally on cursor, pin to bottom of work area
        double left = cursorX - window.Width / 2;
        double top = workArea.Bottom - window.Height;

        // Clamp horizontally to work area
        if (left + window.Width > workArea.Right) left = workArea.Right - window.Width;
        if (left < workArea.Left) left = workArea.Left;

        window.Left = left;
        window.Top = top;
    }

    private static readonly Color DimDot = Color.FromArgb(140, 255, 255, 255);

    private static Icon CreateTrayIcon()
    {
        var bmp = DrawDotsIcon(TDots, Color.White);
        var handle = bmp.GetHicon();
        bmp.Dispose();
        return Icon.FromHandle(handle);
    }

    private void UpdateTrayIcon()
    {
        if (_trayIcon is null || _mainVm is null) return;

        if (_mainVm.IsBusy || _mainVm.BackendState is "Starting")
        {
            StartIconAnimation();
            return;
        }

        StopIconAnimation();

        if (!string.IsNullOrEmpty(_mainVm.CurrentExitNodeId))
            ApplyIcon(DrawExitNodeIcon());
        else if (_mainVm.IsConnected)
            ApplyIcon(DrawDotsIcon(TDots, Color.White));
        else
            ApplyIcon(DrawDotsIcon(AllDots, Color.FromArgb(140, 140, 140)));
    }

    private void StartIconAnimation()
    {
        if (_iconAnimTimer is not null) return;
        _animFrame = 0;
        ApplyIcon(DrawDotsIcon(AnimFrames[0], Color.White));
        _iconAnimTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _iconAnimTimer.Tick += OnIconAnimTick;
        _iconAnimTimer.Start();
    }

    private void OnIconAnimTick(object? sender, EventArgs e)
    {
        _animFrame = (_animFrame + 1) % AnimFrames.Length;
        ApplyIcon(DrawDotsIcon(AnimFrames[_animFrame], Color.White));
    }

    private void StopIconAnimation()
    {
        if (_iconAnimTimer is null) return;
        _iconAnimTimer.Stop();
        _iconAnimTimer.Tick -= OnIconAnimTick;
        _iconAnimTimer = null;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private void ApplyIcon(Bitmap bmp)
    {
        var oldHandle = _prevIconHandle;
        var newHandle = bmp.GetHicon();
        bmp.Dispose();
        _trayIcon!.Icon = Icon.FromHandle(newHandle);
        _prevIconHandle = newHandle;
        if (oldHandle != IntPtr.Zero) DestroyIcon(oldHandle);
    }

    private static Bitmap DrawDotsIcon(int[] highlight, Color color)
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var highlightSet = new HashSet<int>(highlight);
        using var dimBrush = new SolidBrush(DimDot);
        using var brightBrush = new SolidBrush(color);
        for (var i = 0; i < DotCenters.Length; i++)
        {
            var (x, y) = DotCenters[i];
            g.FillEllipse(highlightSet.Contains(i) ? brightBrush : dimBrush, x - 4, y - 4, 8, 8);
        }
        return bmp;
    }

    private static Bitmap DrawExitNodeIcon()
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var highlightSet = new HashSet<int>(TDots);
        using var dimBrush = new SolidBrush(DimDot);
        using var brightBrush = new SolidBrush(Color.White);
        for (var i = 0; i < DotCenters.Length; i++)
        {
            var (x, y) = DotCenters[i];
            g.FillEllipse(highlightSet.Contains(i) ? brightBrush : dimBrush, x - 4, y - 4, 8, 8);
        }

        using var outlinePen = new Pen(Color.FromArgb(30, 30, 30), 5.5f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };
        g.DrawLine(outlinePen, 16, 22, 30, 22);
        g.DrawLine(outlinePen, 24, 16, 31, 22);
        g.DrawLine(outlinePen, 24, 28, 31, 22);

        using var pen = new Pen(Color.White, 2.8f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };
        g.DrawLine(pen, 16, 22, 30, 22);
        g.DrawLine(pen, 24, 16, 31, 22);
        g.DrawLine(pen, 24, 28, 31, 22);

        return bmp;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopIconAnimation();
        if (_prevIconHandle != IntPtr.Zero) DestroyIcon(_prevIconHandle);
        _trayIcon?.Dispose();
        _mainVm?.Dispose();
        _client?.Dispose();
        base.OnExit(e);
    }
}

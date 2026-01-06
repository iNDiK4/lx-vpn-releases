using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Web;
using System.Windows.Shapes;

namespace XrayLauncher
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        private void OnPropertyChanged(string propName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
            }
        }

        private readonly string settingsPath;
        private readonly string blockedDomainsPath;
        
        // Hardcoded VLESS config
        private const string DEFAULT_VLESS = "vless://98b3a3c8-57d0-4aa7-bd38-e7ff68b44b5c@45.90.99.219:16933?type=tcp&encryption=none&security=reality&pbk=5PaSG1WdFyCOzf3gXfhJ217PjXzKhIe8FiGJyTpRlkY&fp=chrome&sni=www.intel.com&sid=58b6423bfd&spx=%2F&flow=xtls-rprx-vision#yhra9r5o";
        
        private bool _vpnRunning;
        private bool _isConnecting;
        private Process xrayProcess;
        private NotifyIcon trayIcon;
        private DispatcherTimer pulseTimer;
        private DispatcherTimer statsTimer;
        private DispatcherTimer sessionTimer;
        private DispatcherTimer spinnerTimer;
        private List<string> blockedDomains = new List<string>();
        
        private string proxyPort = "10808";
        private const string proxyAddress = "127.0.0.1";
        private string lastConfigJson = "";
        
        // Traffic stats
        private DateTime sessionStart;
        private long lastBytesReceived;
        private long lastBytesSent;
        private long totalDownload;
        private long totalUpload;
        
        // Settings
        private bool killSwitchEnabled;
        private bool dohEnabled = true;
        private bool rgbEnabled = true;
        private bool isLoading = false; // Prevent saving during load
        
        // RGB Animation
        private DispatcherTimer rgbTimer;
        private double rgbHue = 0;
        private SolidColorBrush _rgbBrush; // Cached brush for RGB animation
        
        // Cached brushes to prevent memory leaks
        private RadialGradientBrush _connectedGlow;
        private RadialGradientBrush _disconnectedGlow;
        private RadialGradientBrush _connectedAmbient;
        private RadialGradientBrush _disconnectedAmbient;
        private SolidColorBrush _disconnectedStatusBrush;
        private SolidColorBrush _staticPurpleBrush;
        
        // Log size limit to prevent memory growth
        private const int MAX_LOG_LENGTH = 50000;
        
        // Version and update - using centralized version from App.xaml.cs
        private static string CURRENT_VERSION { get { return App.Version; } }
        // Update URL - using GitHub API to avoid CDN caching issues
        private const string UPDATE_URL = "https://api.github.com/repos/iNDiK4/lx-vpn-releases/contents/version.json";

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        // Split tunneling domains
        private static readonly Dictionary<string, List<string>> SplitDomains = new Dictionary<string, List<string>>
        {
            { "Discord", new List<string> { "discord.com", "discordapp.com", "discord.gg", "discordapp.net", "discord.media", "discord.dev", "discordcdn.com", "gateway.discord.gg", "cdn.discordapp.com", "media.discordapp.net" } },
            { "Telegram", new List<string> { "telegram.org", "telegram.me", "t.me", "web.telegram.org", "telegram-cdn.org", "cdn-telegram.org" } },
            { "YouTube", new List<string> { "youtube.com", "googlevideo.com", "ytimg.com", "youtu.be", "ggpht.com", "gstatic.com", "googleapis.com", "youtube-nocookie.com", "youtubekids.com" } },
            { "Twitter", new List<string> { "twitter.com", "x.com", "twimg.com", "t.co", "twittercdn.com" } },
            { "Instagram", new List<string> { "instagram.com", "cdninstagram.com", "fbcdn.net" } },
            { "Spotify", new List<string> { "spotify.com", "scdn.co", "spotifycdn.com" } }
        };

        public bool vpnRunning
        {
            get { return _vpnRunning; }
            set
            {
                if (_vpnRunning == value) return;
                _vpnRunning = value;
                OnPropertyChanged("vpnRunning");
                UpdateConnectionUI();
            }
        }

        public MainWindow()
        {
            // CRITICAL: Set isLoading before InitializeComponent to prevent 
            // Changed events from overwriting settings during UI creation
            isLoading = true;
            
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            settingsPath = System.IO.Path.Combine(baseDir, "settings.json");
            blockedDomainsPath = System.IO.Path.Combine(baseDir, "blocked-domains.txt");
            
            // Initialize cached brushes to prevent memory leaks
            InitializeCachedBrushes();
            
            InitializeComponent();
            this.DataContext = this;
            
            // Subscribe to Closing event for resource cleanup
            this.Closing += Window_Closing;
            
            LoadBlockedDomains();
            LoadSettings();  // This will set isLoading = false at the end
            UpdateConnectionUI();
            StartPulseAnimation();
            InitializeTimers();
            
            // Ensure isLoading is false after init completes
            isLoading = false;
            
            // Set version labels from centralized App.Version
            if (HeaderVersionLabel != null) HeaderVersionLabel.Text = "v" + App.Version;
            if (CurrentVersionLabel != null) CurrentVersionLabel.Text = "v" + App.Version;
            
            // Initialize RGB animation timer
            InitializeRgbAnimation();
            
            Dispatcher.InvokeAsync(new Action(UpdateCurrentIP), DispatcherPriority.Background);
            Dispatcher.InvokeAsync(new Action(UpdatePing), DispatcherPriority.Background);
            
            AppendLog(App.AppName + " v" + App.Version + " запущен");
            AppendLog("Загружено " + blockedDomains.Count + " заблокированных доменов");
            
            // Check for updates after window is fully loaded
            this.Loaded += MainWindow_Loaded;
        }
        
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Check for updates 2 seconds after window loaded
            var updateTimer = new DispatcherTimer();
            updateTimer.Interval = TimeSpan.FromSeconds(2);
            updateTimer.Tick += delegate 
            { 
                updateTimer.Stop(); 
                CheckForUpdatesAsync(); 
            };
            updateTimer.Start();
        }

        private void InitializeTimers()
        {
            // Stats timer - update traffic every second
            statsTimer = new DispatcherTimer();
            statsTimer.Interval = TimeSpan.FromSeconds(1);
            statsTimer.Tick += StatsTimer_Tick;
            
            // Session timer
            sessionTimer = new DispatcherTimer();
            sessionTimer.Interval = TimeSpan.FromSeconds(1);
            sessionTimer.Tick += SessionTimer_Tick;
            
            // Spinner animation
            spinnerTimer = new DispatcherTimer();
            spinnerTimer.Interval = TimeSpan.FromMilliseconds(16);
            spinnerTimer.Tick += SpinnerTimer_Tick;
        }

        private void LoadBlockedDomains()
        {
            blockedDomains.Clear();
            try
            {
                if (File.Exists(blockedDomainsPath))
                {
                    string[] lines = File.ReadAllLines(blockedDomainsPath);
                    foreach (string line in lines)
                    {
                        string domain = line.Trim();
                        if (!string.IsNullOrEmpty(domain) && !domain.StartsWith("#") && !domain.StartsWith("*"))
                        {
                            domain = domain.Replace("*.", "");
                            if (!blockedDomains.Contains(domain))
                            {
                                blockedDomains.Add(domain);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("Ошибка загрузки blocked-domains.txt: " + ex.Message);
            }
            
            if (BlockedCountLabel != null)
            {
                BlockedCountLabel.Text = blockedDomains.Count + " доменов (RU blacklist)";
            }
        }

        #region Traffic Stats
        private void StatsTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
                long currentReceived = 0;
                long currentSent = 0;
                
                foreach (NetworkInterface ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        IPv4InterfaceStatistics stats = ni.GetIPv4Statistics();
                        currentReceived += stats.BytesReceived;
                        currentSent += stats.BytesSent;
                    }
                }
                
                if (lastBytesReceived > 0)
                {
                    long downloadSpeed = currentReceived - lastBytesReceived;
                    long uploadSpeed = currentSent - lastBytesSent;
                    
                    totalDownload += downloadSpeed;
                    totalUpload += uploadSpeed;
                    
                    // Update UI
                    DownloadLabel.Text = FormatSpeed(downloadSpeed);
                    UploadLabel.Text = FormatSpeed(uploadSpeed);
                    DownloadTotal.Text = "Всего: " + FormatBytes(totalDownload);
                    UploadTotal.Text = "Всего: " + FormatBytes(totalUpload);
                }
                
                lastBytesReceived = currentReceived;
                lastBytesSent = currentSent;
            }
            catch { }
        }
        
        private string FormatSpeed(long bytesPerSecond)
        {
            if (bytesPerSecond < 1024)
                return bytesPerSecond + " B/s";
            else if (bytesPerSecond < 1024 * 1024)
                return (bytesPerSecond / 1024.0).ToString("F1") + " KB/s";
            else
                return (bytesPerSecond / (1024.0 * 1024.0)).ToString("F2") + " MB/s";
        }
        
        private string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return bytes + " B";
            else if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString("F1") + " KB";
            else if (bytes < 1024 * 1024 * 1024)
                return (bytes / (1024.0 * 1024.0)).ToString("F1") + " MB";
            else
                return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";
        }
        #endregion

        #region Session Timer
        private void SessionTimer_Tick(object sender, EventArgs e)
        {
            TimeSpan elapsed = DateTime.Now - sessionStart;
            SessionTimer.Text = "⏱ " + elapsed.ToString(@"hh\:mm\:ss");
        }
        #endregion

        #region Spinner Animation
        private double spinnerAngle;
        
        private void SpinnerTimer_Tick(object sender, EventArgs e)
        {
            spinnerAngle += 5;
            if (spinnerAngle >= 360) spinnerAngle = 0;
            SpinnerRotation.Angle = spinnerAngle;
        }
        
        private void ShowSpinner(bool show)
        {
            SpinnerRing.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show)
            {
                spinnerTimer.Start();
            }
            else
            {
                spinnerTimer.Stop();
            }
        }
        #endregion

        #region Navigation
        private void NavHome_Click(object sender, RoutedEventArgs e) { ShowPage("Home"); }
        private void NavSplit_Click(object sender, RoutedEventArgs e) { ShowPage("Split"); }
        private void NavSettings_Click(object sender, RoutedEventArgs e) { ShowPage("Settings"); }

        private void ShowPage(string page)
        {
            HomePage.Visibility = Visibility.Collapsed;
            SplitPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            
            NavHome.Style = (Style)FindResource("SidebarButton");
            NavSplit.Style = (Style)FindResource("SidebarButton");
            NavSettings.Style = (Style)FindResource("SidebarButton");
            
            switch (page)
            {
                case "Home":
                    HomePage.Visibility = Visibility.Visible;
                    NavHome.Style = (Style)FindResource("SidebarButtonActive");
                    break;
                case "Split":
                    SplitPage.Visibility = Visibility.Visible;
                    NavSplit.Style = (Style)FindResource("SidebarButtonActive");
                    break;
                case "Settings":
                    SettingsPage.Visibility = Visibility.Visible;
                    NavSettings.Style = (Style)FindResource("SidebarButtonActive");
                    break;
            }
        }
        #endregion

        #region Animations
        private void StartPulseAnimation()
        {
            pulseTimer = new DispatcherTimer();
            pulseTimer.Interval = TimeSpan.FromMilliseconds(2000);
            pulseTimer.Tick += PulseTimer_Tick;
            pulseTimer.Start();
        }
        
        private void InitializeCachedBrushes()
        {
            // Connected glow brush (green)
            _connectedGlow = new RadialGradientBrush();
            _connectedGlow.GradientStops.Add(new GradientStop(Color.FromRgb(16, 185, 129), 0.4));
            _connectedGlow.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            _connectedGlow.Freeze();
            
            // Disconnected glow brush (purple)
            _disconnectedGlow = new RadialGradientBrush();
            _disconnectedGlow.GradientStops.Add(new GradientStop(Color.FromRgb(99, 102, 241), 0.4));
            _disconnectedGlow.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            _disconnectedGlow.Freeze();
            
            // Connected ambient brush (green)
            _connectedAmbient = new RadialGradientBrush();
            _connectedAmbient.GradientStops.Add(new GradientStop(Color.FromRgb(16, 185, 129), 0));
            _connectedAmbient.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            _connectedAmbient.Freeze();
            
            // Disconnected ambient brush (purple)
            _disconnectedAmbient = new RadialGradientBrush();
            _disconnectedAmbient.GradientStops.Add(new GradientStop(Color.FromRgb(99, 102, 241), 0));
            _disconnectedAmbient.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            _disconnectedAmbient.Freeze();
            
            // Disconnected status brush (gray)
            _disconnectedStatusBrush = new SolidColorBrush(Color.FromRgb(136, 136, 136));
            _disconnectedStatusBrush.Freeze();
            
            // Static purple brush for RGB disabled state
            _staticPurpleBrush = new SolidColorBrush(Color.FromRgb(99, 102, 241));
            _staticPurpleBrush.Freeze();
            
            // RGB brush - not frozen because we modify its color
            _rgbBrush = new SolidColorBrush(Color.FromRgb(99, 102, 241));
        }
        
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Stop all timers to prevent memory leaks
            if (pulseTimer != null) { pulseTimer.Stop(); pulseTimer = null; }
            if (statsTimer != null) { statsTimer.Stop(); statsTimer = null; }
            if (sessionTimer != null) { sessionTimer.Stop(); sessionTimer = null; }
            if (spinnerTimer != null) { spinnerTimer.Stop(); spinnerTimer = null; }
            if (rgbTimer != null) { rgbTimer.Stop(); rgbTimer = null; }
            
            // VPN cleanup
            if (vpnRunning)
            {
                StopVPN();
                ResetSystemProxy();
            }
            if (killSwitchEnabled) DisableKillSwitch();
            
            // Dispose tray icon
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }
        }

        private void PulseTimer_Tick(object sender, EventArgs e)
        {
            if (ConnectButtonGlow != null)
            {
                var animation = new DoubleAnimation();
                animation.From = vpnRunning ? 0.5 : 0.3;
                animation.To = vpnRunning ? 0.8 : 0.5;
                animation.Duration = TimeSpan.FromMilliseconds(1000);
                animation.AutoReverse = true;
                animation.EasingFunction = new SineEase();
                ConnectButtonGlow.BeginAnimation(Ellipse.OpacityProperty, animation);
            }
        }

        private void UpdateConnectionUI()
        {
            Dispatcher.InvokeAsync(new Action(delegate
            {
                if (vpnRunning)
                {
                    ConnectionStatus.Text = "ПОДКЛЮЧЕНО";
                    ConnectionStatus.Foreground = (Brush)FindResource("SuccessBrush");
                    StatusDetails.Text = "Защищенное соединение активно";
                    SessionTimer.Visibility = Visibility.Visible;
                    
                    if (ConnectButtonGlow != null)
                    {
                        ConnectButtonGlow.Fill = _connectedGlow;
                        ConnectButtonGlow.Opacity = 0.6;
                    }
                    
                    if (AmbientGlow != null)
                    {
                        AmbientGlow.Fill = _connectedAmbient;
                        AmbientGlow.Opacity = 0.25;
                    }
                    
                    // Tray notification
                    ShowNotification("Подключено", "VPN соединение установлено");
                }
                else
                {
                    ConnectionStatus.Text = "ОТКЛЮЧЕНО";
                    ConnectionStatus.Foreground = _disconnectedStatusBrush;
                    StatusDetails.Text = "Нажмите для подключения";
                    SessionTimer.Visibility = Visibility.Collapsed;
                    
                    if (ConnectButtonGlow != null)
                    {
                        ConnectButtonGlow.Fill = _disconnectedGlow;
                        ConnectButtonGlow.Opacity = 0.4;
                    }
                    
                    if (AmbientGlow != null)
                    {
                        AmbientGlow.Fill = _disconnectedAmbient;
                        AmbientGlow.Opacity = 0.15;
                    }
                }
            }));
        }
        
        private void ShowNotification(string title, string message)
        {
            if (trayIcon == null)
            {
                trayIcon = new NotifyIcon();
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (System.IO.File.Exists(iconPath))
                    trayIcon.Icon = new System.Drawing.Icon(iconPath);
                else
                    trayIcon.Icon = System.Drawing.SystemIcons.Shield;
                trayIcon.DoubleClick += delegate { Show(); WindowState = WindowState.Normal; };
            }
            trayIcon.Visible = true;
            trayIcon.ShowBalloonTip(2000, title, message, ToolTipIcon.Info);
        }
        #endregion

        #region Window Controls
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CloseDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
            
            if (dialog.Result == CloseDialog.CloseAction.Close)
            {
                if (vpnRunning) { StopVPN(); ResetSystemProxy(); }
                if (killSwitchEnabled) DisableKillSwitch();
                if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); }
                Close();
            }
            else if (dialog.Result == CloseDialog.CloseAction.Tray)
            {
                MinimizeToTray();
            }
        }

        private void TrayBtn_Click(object sender, RoutedEventArgs e)
        {
            MinimizeToTray();
        }
        
        private void MinimizeToTray()
        {
            Hide();
            if (trayIcon == null)
            {
                trayIcon = new NotifyIcon();
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (System.IO.File.Exists(iconPath))
                    trayIcon.Icon = new System.Drawing.Icon(iconPath);
                else
                    trayIcon.Icon = System.Drawing.SystemIcons.Shield;
                trayIcon.DoubleClick += delegate { Show(); WindowState = WindowState.Normal; };
            }
            trayIcon.Visible = true;
            trayIcon.Text = vpnRunning ? "LX VPN - Подключено" : "LX VPN - Отключено";
            trayIcon.ShowBalloonTip(2000, "LX VPN", "Приложение свёрнуто в трей", ToolTipIcon.Info);
        }
        #endregion

        #region Settings
        private const string AUTOSTART_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "LX VPN";
        
        [DataContract]
        private class AppSettings
        {
            [DataMember(Name = "port")] public string Port { get; set; }
            [DataMember(Name = "proxyType")] public string ProxyType { get; set; }
            [DataMember(Name = "splitEnabled")] public bool SplitEnabled { get; set; }
            [DataMember(Name = "autoConnect")] public bool AutoConnect { get; set; }
            [DataMember(Name = "autoStart")] public bool AutoStart { get; set; }
            [DataMember(Name = "killSwitch")] public bool KillSwitch { get; set; }
            [DataMember(Name = "doh")] public bool DoH { get; set; }
            [DataMember(Name = "rgbEnabled")] public bool RgbEnabled { get; set; }
            
            public AppSettings() 
            { 
                Port = "10808"; 
                ProxyType = "http"; 
                SplitEnabled = true; 
                AutoConnect = false; 
                AutoStart = false;
                KillSwitch = false;
                DoH = true;
                RgbEnabled = true;
            }
        }

        private void LoadSettings()
        {
            if (!File.Exists(settingsPath)) 
            {
                AppendLog("Файл настроек не найден, используются значения по умолчанию");
                return;
            }
            try
            {
                isLoading = true;
                AppendLog("Загрузка настроек...");
                string json = File.ReadAllText(settingsPath);
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var settings = (AppSettings)serializer.ReadObject(stream);
                    if (settings != null)
                    {
                        proxyPort = settings.Port ?? "10808";
                        if (PortInput != null) PortInput.Text = proxyPort;
                        if (ProxyTypeCombo != null) ProxyTypeCombo.SelectedIndex = settings.ProxyType == "socks" ? 1 : 0;
                        if (SplitToggle != null) SplitToggle.IsChecked = settings.SplitEnabled;
                        if (AutoConnectToggle != null) AutoConnectToggle.IsChecked = settings.AutoConnect;
                        if (AutoStartToggle != null) AutoStartToggle.IsChecked = settings.AutoStart;
                        if (KillSwitchToggle != null) KillSwitchToggle.IsChecked = settings.KillSwitch;
                        if (DoHToggle != null) DoHToggle.IsChecked = settings.DoH;
                        if (RgbToggle != null) RgbToggle.IsChecked = settings.RgbEnabled;
                        
                        killSwitchEnabled = settings.KillSwitch;
                        dohEnabled = settings.DoH;
                        rgbEnabled = settings.RgbEnabled;
                        
                        AppendLog("Настройки загружены: AutoConnect=" + settings.AutoConnect + ", AutoStart=" + settings.AutoStart);
                        
                        isLoading = false;
                        
                        // Auto-connect on startup
                        if (settings.AutoConnect && !vpnRunning)
                        {
                            Dispatcher.InvokeAsync(new Action(delegate
                            {
                                try
                                {
                                    AppendLog("Автоподключение...");
                                    StartVPN();
                                    vpnRunning = true;
                                }
                                catch (Exception ex)
                                {
                                    AppendLog("Ошибка автоподключения: " + ex.Message);
                                }
                            }), DispatcherPriority.Background);
                        }
                    }
                }
            }
            catch (Exception ex) 
            { 
                AppendLog("Ошибка загрузки настроек: " + ex.Message);
            }
            finally
            {
                isLoading = false;
            }
        }

        private void SaveSettings()
        {
            try
            {
                var comboItem = ProxyTypeCombo != null ? ProxyTypeCombo.SelectedItem as ComboBoxItem : null;
                var settings = new AppSettings();
                settings.Port = proxyPort;
                settings.ProxyType = comboItem != null && comboItem.Tag != null ? comboItem.Tag.ToString() : "http";
                settings.SplitEnabled = SplitToggle != null && SplitToggle.IsChecked == true;
                settings.AutoConnect = AutoConnectToggle != null && AutoConnectToggle.IsChecked == true;
                settings.AutoStart = AutoStartToggle != null && AutoStartToggle.IsChecked == true;
                settings.KillSwitch = KillSwitchToggle != null && KillSwitchToggle.IsChecked == true;
                settings.DoH = DoHToggle != null && DoHToggle.IsChecked == true;
                settings.RgbEnabled = RgbToggle != null && RgbToggle.IsChecked == true;
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                using (var stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, settings);
                    File.WriteAllText(settingsPath, Encoding.UTF8.GetString(stream.ToArray()));
                }
            }
            catch { }
        }
        
        private void AutoConnectToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoading) return;
            SaveSettings();
            bool enabled = AutoConnectToggle.IsChecked == true;
            AppendLog("Автоподключение: " + (enabled ? "ВКЛ" : "ВЫКЛ"));
        }
        
        private void AutoStartToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoading) return;
            bool enabled = AutoStartToggle.IsChecked == true;
            SetAutoStart(enabled);
            SaveSettings();
            AppendLog("Запуск с Windows: " + (enabled ? "ВКЛ" : "ВЫКЛ"));
        }
        
        private void KillSwitchToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoading) return;
            killSwitchEnabled = KillSwitchToggle.IsChecked == true;
            SaveSettings();
            AppendLog("Kill Switch: " + (killSwitchEnabled ? "ВКЛ" : "ВЫКЛ"));
            
            if (killSwitchEnabled && vpnRunning)
            {
                EnableKillSwitch();
            }
            else if (!killSwitchEnabled)
            {
                DisableKillSwitch();
            }
        }
        
        private void DoHToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoading) return;
            dohEnabled = DoHToggle.IsChecked == true;
            SaveSettings();
            AppendLog("DNS-over-HTTPS: " + (dohEnabled ? "ВКЛ" : "ВЫКЛ"));
        }
        
        private void RgbToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoading) return;
            rgbEnabled = RgbToggle.IsChecked == true;
            SaveSettings();
            
            if (rgbEnabled)
            {
                if (rgbTimer != null) rgbTimer.Start();
                AppendLog("RGB подсветка: ВКЛ");
            }
            else
            {
                if (rgbTimer != null) rgbTimer.Stop();
                // Set static purple border when disabled - use cached brush
                if (RgbBorder != null) RgbBorder.BorderBrush = _staticPurpleBrush;
                AppendLog("RGB подсветка: ВЫКЛ");
            }
        }
        
        private void InitializeRgbAnimation()
        {
            rgbTimer = new DispatcherTimer();
            rgbTimer.Interval = TimeSpan.FromMilliseconds(50); // Increased from 30ms for better performance
            rgbTimer.Tick += RgbTimer_Tick;
            
            if (rgbEnabled)
            {
                rgbTimer.Start();
            }
        }
        
        private void RgbTimer_Tick(object sender, EventArgs e)
        {
            if (RgbBorder == null || !rgbEnabled) return;
            
            rgbHue += 1.5; // Slightly faster to compensate for longer interval
            if (rgbHue >= 360) rgbHue = 0;
            
            Color color = HsvToRgb(rgbHue, 1.0, 1.0);
            _rgbBrush.Color = color; // Reuse brush, just update color
            RgbBorder.BorderBrush = _rgbBrush;
        }
        
        private Color HsvToRgb(double h, double s, double v)
        {
            int hi = (int)(h / 60) % 6;
            double f = h / 60 - (int)(h / 60);
            double p = v * (1 - s);
            double q = v * (1 - f * s);
            double t = v * (1 - (1 - f) * s);
            
            double r = 0, g = 0, b = 0;
            switch (hi)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                case 5: r = v; g = p; b = q; break;
            }
            
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }
        
        private void SetAutoStart(bool enable)
        {
            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AUTOSTART_KEY, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue(APP_NAME, "\"" + exePath + "\"");
                        }
                        else
                        {
                            key.DeleteValue(APP_NAME, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("Ошибка автозапуска: " + ex.Message);
            }
        }
        #endregion

        #region Kill Switch
        private void EnableKillSwitch()
        {
            try
            {
                // Block all outbound except localhost and xray
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "netsh";
                psi.Arguments = "advfirewall firewall add rule name=\"LX_VPN_KillSwitch\" dir=out action=block enable=yes";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.Verb = "runas";
                Process.Start(psi).WaitForExit();
                
                // Allow localhost
                psi.Arguments = "advfirewall firewall add rule name=\"LX_VPN_Allow_Local\" dir=out action=allow remoteip=127.0.0.1 enable=yes";
                Process.Start(psi).WaitForExit();
                
                AppendLog("Kill Switch активирован");
            }
            catch (Exception ex)
            {
                AppendLog("Ошибка Kill Switch: " + ex.Message);
            }
        }
        
        private void DisableKillSwitch()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "netsh";
                psi.Arguments = "advfirewall firewall delete rule name=\"LX_VPN_KillSwitch\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi).WaitForExit();
                
                psi.Arguments = "advfirewall firewall delete rule name=\"LX_VPN_Allow_Local\"";
                Process.Start(psi).WaitForExit();
                
                AppendLog("Kill Switch деактивирован");
            }
            catch { }
        }
        #endregion

        #region Speed Test
        private async void SpeedTest_Click(object sender, RoutedEventArgs e)
        {
            SpeedTestResult.Text = "Тестирование...";
            AppendLog("Запуск Speed Test...");
            
            try
            {
                // Download speed test using 10MB file
                string testUrl = "http://speedtest.tele2.net/10MB.zip";
                var stopwatch = new Stopwatch();
                
                using (var client = new WebClient())
                {
                    stopwatch.Start();
                    byte[] data = await client.DownloadDataTaskAsync(testUrl);
                    stopwatch.Stop();
                    
                    double seconds = stopwatch.ElapsedMilliseconds / 1000.0;
                    double mbps = (data.Length * 8.0 / 1000000.0) / seconds;
                    
                    SpeedTestResult.Text = "↓ " + mbps.ToString("F2") + " Mbps (" + (data.Length / 1024.0 / 1024.0).ToString("F1") + " MB за " + seconds.ToString("F1") + "s)";
                    AppendLog("Speed Test: " + mbps.ToString("F2") + " Mbps");
                }
            }
            catch (Exception ex)
            {
                SpeedTestResult.Text = "Ошибка: " + ex.Message;
                AppendLog("Speed Test ошибка: " + ex.Message);
            }
        }
        #endregion

        #region WebRTC Check
        private async void WebRTCCheck_Click(object sender, RoutedEventArgs e)
        {
            SpeedTestResult.Text = "Проверка WebRTC...";
            AppendLog("Проверка WebRTC утечки...");
            
            try
            {
                using (var client = new WebClient())
                {
                    // Check current IP
                    string currentIp = await client.DownloadStringTaskAsync("https://api.ipify.org");
                    
                    SpeedTestResult.Text = "🔍 WebRTC: Ваш IP " + currentIp + "\n⚠️ Для полной защиты отключите WebRTC в браузере";
                    AppendLog("WebRTC Check: IP = " + currentIp);
                    
                    ShowNotification("WebRTC Check", "Проверьте настройки браузера для защиты от WebRTC утечек");
                }
            }
            catch (Exception ex)
            {
                SpeedTestResult.Text = "Ошибка проверки: " + ex.Message;
                AppendLog("WebRTC Check ошибка: " + ex.Message);
            }
        }
        #endregion

        #region Update Check
        [DataContract]
        private class UpdateInfo
        {
            [DataMember(Name = "version")] public string Version { get; set; }
            [DataMember(Name = "changelog")] public string Changelog { get; set; }
            [DataMember(Name = "downloadUrl")] public string DownloadUrl { get; set; }
        }
        
        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("Проверка обновлений...");
            SpeedTestResult.Text = "Проверка обновлений...";
            
            try
            {
                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers.Add("User-Agent", "LX-VPN/" + CURRENT_VERSION);
                    client.Headers.Add("Accept", "application/vnd.github.v3.raw");
                    string json = await client.DownloadStringTaskAsync(UPDATE_URL);
                    
                    var serializer = new DataContractJsonSerializer(typeof(UpdateInfo));
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        var updateInfo = (UpdateInfo)serializer.ReadObject(stream);
                        
                        if (updateInfo != null && !string.IsNullOrEmpty(updateInfo.Version))
                        {
                            bool hasUpdate = CompareVersions(updateInfo.Version, CURRENT_VERSION) > 0;
                            
                            if (hasUpdate)
                            {
                                AppendLog("Доступна новая версия: " + updateInfo.Version);
                                SpeedTestResult.Text = "🎉 Доступна версия " + updateInfo.Version;
                                
                                var dialog = new UpdateDialog();
                                dialog.Owner = this;
                                dialog.OldVersion = "v" + CURRENT_VERSION;
                                dialog.NewVersion = "v" + updateInfo.Version;
                                dialog.Changelog = updateInfo.Changelog ?? "• Улучшения и исправления";
                                dialog.DownloadUrl = updateInfo.DownloadUrl;
                                dialog.ShowDialog();
                            }
                            else
                            {
                                AppendLog("У вас актуальная версия " + CURRENT_VERSION);
                                SpeedTestResult.Text = "✓ У вас последняя версия " + CURRENT_VERSION;
                                ShowNotification("Обновления", "У вас установлена последняя версия");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("Ошибка проверки обновлений: " + ex.Message);
                SpeedTestResult.Text = "Ошибка: " + ex.Message;
            }
        }
        
        private int CompareVersions(string v1, string v2)
        {
            try
            {
                string[] parts1 = v1.Split('.');
                string[] parts2 = v2.Split('.');
                
                for (int i = 0; i < Math.Max(parts1.Length, parts2.Length); i++)
                {
                    int p1 = i < parts1.Length ? int.Parse(parts1[i]) : 0;
                    int p2 = i < parts2.Length ? int.Parse(parts2[i]) : 0;
                    
                    if (p1 > p2) return 1;
                    if (p1 < p2) return -1;
                }
                return 0;
            }
            catch
            {
                return string.Compare(v1, v2, StringComparison.Ordinal);
            }
        }
        
        // Silent update check for startup - only shows dialog if update available
        private async void CheckForUpdatesAsync()
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers.Add("User-Agent", "LX-VPN/" + CURRENT_VERSION);
                    client.Headers.Add("Accept", "application/vnd.github.v3.raw");
                    string json = await client.DownloadStringTaskAsync(UPDATE_URL);
                    
                    var serializer = new DataContractJsonSerializer(typeof(UpdateInfo));
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    {
                        var updateInfo = (UpdateInfo)serializer.ReadObject(stream);
                        
                        if (updateInfo != null && !string.IsNullOrEmpty(updateInfo.Version))
                        {
                            bool hasUpdate = CompareVersions(updateInfo.Version, CURRENT_VERSION) > 0;
                            
                            if (hasUpdate)
                            {
                                AppendLog("🔔 Доступно обновление: v" + updateInfo.Version);
                                ShowNotification("Доступно обновление", "Новая версия " + updateInfo.Version + " доступна!");
                                
                                var dialog = new UpdateDialog();
                                dialog.Owner = this;
                                dialog.OldVersion = "v" + CURRENT_VERSION;
                                dialog.NewVersion = "v" + updateInfo.Version;
                                dialog.Changelog = updateInfo.Changelog ?? "• Улучшения и исправления";
                                dialog.DownloadUrl = updateInfo.DownloadUrl;
                                dialog.ShowDialog();
                            }
                            else
                            {
                                AppendLog("Версия актуальна: " + CURRENT_VERSION);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog("Автопроверка обновлений: " + ex.Message);
            }
        }
        #endregion

        #region Split Tunneling
        private void SplitToggle_Changed(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            if (ModeLabel != null)
            {
                bool enabled = SplitToggle.IsChecked == true;
                ModeLabel.Text = enabled ? "Split" : "Full";
            }
        }

        private void AddCustomDomain_Click(object sender, RoutedEventArgs e)
        {
            string domain = CustomDomainBox.Text.Trim();
            if (string.IsNullOrEmpty(domain)) return;
            
            domain = domain.Replace("https://", "").Replace("http://", "").Replace("www.", "");
            if (domain.Contains("/")) domain = domain.Substring(0, domain.IndexOf("/"));
            
            var border = new Border();
            border.Background = new SolidColorBrush(Color.FromRgb(13, 13, 26));
            border.CornerRadius = new CornerRadius(12);
            border.Padding = new Thickness(15, 12, 15, 12);
            border.Margin = new Thickness(0, 0, 0, 10);
            
            var stack = new StackPanel();
            stack.Orientation = System.Windows.Controls.Orientation.Horizontal;
            
            var chk = new System.Windows.Controls.CheckBox();
            chk.IsChecked = true;
            chk.Tag = domain;
            chk.Cursor = System.Windows.Input.Cursors.Hand;
            stack.Children.Add(chk);
            
            var iconBorder = new Border();
            iconBorder.Background = new SolidColorBrush(Color.FromRgb(42, 42, 78));
            iconBorder.CornerRadius = new CornerRadius(8);
            iconBorder.Width = 36;
            iconBorder.Height = 36;
            iconBorder.Margin = new Thickness(12, 0, 12, 0);
            var icon = new TextBlock();
            icon.Text = "🌐";
            icon.FontSize = 16;
            icon.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            iconBorder.Child = icon;
            stack.Children.Add(iconBorder);
            
            var textStack = new StackPanel();
            textStack.VerticalAlignment = VerticalAlignment.Center;
            var title = new TextBlock();
            title.Text = domain;
            title.FontSize = 14;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = Brushes.White;
            textStack.Children.Add(title);
            var sub = new TextBlock();
            sub.Text = "Пользовательский";
            sub.FontSize = 11;
            sub.Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102));
            textStack.Children.Add(sub);
            
            stack.Children.Add(textStack);
            border.Child = stack;
            
            SitesList.Children.Add(border);
            CustomDomainBox.Text = "";
            
            AppendLog("Добавлен домен: " + domain);
        }

        private List<string> GetSelectedDomains()
        {
            var domains = new List<string>();
            
            if (ChkBlocked.IsChecked == true && blockedDomains.Count > 0)
            {
                domains.AddRange(blockedDomains);
            }
            
            if (ChkDiscord.IsChecked == true) domains.AddRange(SplitDomains["Discord"]);
            if (ChkTelegram.IsChecked == true) domains.AddRange(SplitDomains["Telegram"]);
            if (ChkYouTube.IsChecked == true) domains.AddRange(SplitDomains["YouTube"]);
            if (ChkTwitter.IsChecked == true) domains.AddRange(SplitDomains["Twitter"]);
            if (ChkInstagram.IsChecked == true) domains.AddRange(SplitDomains["Instagram"]);
            if (ChkSpotify.IsChecked == true) domains.AddRange(SplitDomains["Spotify"]);
            
            foreach (var child in SitesList.Children)
            {
                var border = child as Border;
                if (border != null)
                {
                    var stack = border.Child as StackPanel;
                    if (stack != null && stack.Children.Count > 0)
                    {
                        var chk = stack.Children[0] as System.Windows.Controls.CheckBox;
                        if (chk != null && chk.IsChecked == true && chk.Tag is string)
                        {
                            domains.Add(chk.Tag.ToString());
                        }
                    }
                }
            }
            
            return domains;
        }
        #endregion

        #region Network
        private async void UpdateCurrentIP()
        {
            try
            {
                if (IpLabel != null) IpLabel.Text = "...";
                using (var wc = new WebClient()) 
                { 
                    string ip = await wc.DownloadStringTaskAsync("https://api.ipify.org"); 
                    if (IpLabel != null) IpLabel.Text = ip;
                }
            }
            catch { if (IpLabel != null) IpLabel.Text = "Ошибка"; }
        }

        private async void UpdatePing()
        {
            try
            {
                if (PingLabel != null) PingLabel.Text = "...";
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync("8.8.8.8", 2000);
                    if (PingLabel != null)
                    {
                        PingLabel.Text = (reply.Status == IPStatus.Success) ? reply.RoundtripTime + " ms" : "Таймаут";
                    }
                }
            }
            catch { if (PingLabel != null) PingLabel.Text = "Ошибка"; }
        }
        #endregion

        #region VPN
        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnecting) return;
            
            try
            {
                if (vpnRunning)
                {
                    StopVPN();
                    ResetSystemProxy();
                    if (killSwitchEnabled) DisableKillSwitch();
                    vpnRunning = false;
                    statsTimer.Stop();
                    sessionTimer.Stop();
                    ShowNotification("Отключено", "VPN соединение разорвано");
                    AppendLog("VPN остановлен");
                }
                else
                {
                    _isConnecting = true;
                    ShowSpinner(true);
                    ConnectionStatus.Text = "ПОДКЛЮЧЕНИЕ...";
                    ConnectionStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                    StatusDetails.Text = "Устанавливаем соединение";
                    
                    // Start connection asynchronously
                    Dispatcher.InvokeAsync(new Action(delegate
                    {
                        try
                        {
                            SaveSettings();
                            StartVPN();
                            
                            // Start timers
                            sessionStart = DateTime.Now;
                            totalDownload = 0;
                            totalUpload = 0;
                            lastBytesReceived = 0;
                            lastBytesSent = 0;
                            statsTimer.Start();
                            sessionTimer.Start();
                            
                            if (killSwitchEnabled) EnableKillSwitch();
                            
                            vpnRunning = true;
                        }
                        catch (Exception ex)
                        {
                            AppendLog("ОШИБКА: " + ex.Message);
                            ConnectionStatus.Text = "ОШИБКА";
                            ConnectionStatus.Foreground = (Brush)FindResource("DangerBrush");
                            StatusDetails.Text = ex.Message;
                        }
                        finally
                        {
                            _isConnecting = false;
                            ShowSpinner(false);
                        }
                    }), DispatcherPriority.Background);
                }
                
                var timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(2);
                timer.Tick += delegate { UpdateCurrentIP(); UpdatePing(); timer.Stop(); };
                timer.Start();
            }
            catch (Exception ex)
            {
                AppendLog("ОШИБКА: " + ex.Message);
                _isConnecting = false;
                ShowSpinner(false);
                if (vpnRunning) vpnRunning = false;
            }
        }

        private void StartVPN()
        {
            AppendLog("=== Запуск VPN ===");
            
            int port;
            if (int.TryParse(PortInput.Text, out port) && port > 0 && port <= 65535)
            {
                proxyPort = PortInput.Text;
            }
            else
            {
                proxyPort = "10808";
                PortInput.Text = "10808";
            }
            
            string xrayExePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "xray.exe");
            
            if (!File.Exists(xrayExePath))
            {
                AppendLog("ОШИБКА: xray.exe не найден");
                throw new FileNotFoundException("xray.exe не найден");
            }

            string configFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            
            var comboItem = ProxyTypeCombo.SelectedItem as ComboBoxItem;
            string proxyType = comboItem != null && comboItem.Tag != null ? comboItem.Tag.ToString() : "socks";
            
            bool splitEnabled = SplitToggle != null && SplitToggle.IsChecked == true;
            List<string> splitDomains = splitEnabled ? GetSelectedDomains() : null;
            
            AppendLog("Split Tunneling: " + (splitEnabled ? "ВКЛ (" + (splitDomains != null ? splitDomains.Count : 0) + " доменов)" : "ВЫКЛ"));
            AppendLog("DNS-over-HTTPS: " + (dohEnabled ? "ВКЛ" : "ВЫКЛ"));
            
            string xrayConfig = GenerateXrayConfig(DEFAULT_VLESS, proxyType, splitDomains);
            if (string.IsNullOrEmpty(xrayConfig))
            {
                throw new Exception("Ошибка генерации конфигурации");
            }

            lastConfigJson = xrayConfig;
            File.WriteAllText(configFilePath, xrayConfig);
            
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = xrayExePath;
            startInfo.Arguments = "run -config \"" + configFilePath + "\"";
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            xrayProcess = new Process();
            xrayProcess.StartInfo = startInfo;
            xrayProcess.EnableRaisingEvents = true;
            
            xrayProcess.OutputDataReceived += delegate(object s, DataReceivedEventArgs args)
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    AppendLog("[xray] " + args.Data);
                }
            };
            
            xrayProcess.ErrorDataReceived += delegate(object s, DataReceivedEventArgs args)
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    AppendLog("[xray] " + args.Data);
                }
            };
            
            xrayProcess.Exited += delegate
            {
                Dispatcher.InvokeAsync(new Action(delegate
                {
                    AppendLog("Xray процесс завершился");
                    if (vpnRunning)
                    {
                        vpnRunning = false;
                        ResetSystemProxy();
                        statsTimer.Stop();
                        sessionTimer.Stop();
                        if (killSwitchEnabled)
                        {
                            ShowNotification("Kill Switch", "VPN отключился! Интернет заблокирован.");
                        }
                        UpdateCurrentIP();
                    }
                }));
            };

            xrayProcess.Start();
            xrayProcess.BeginOutputReadLine();
            xrayProcess.BeginErrorReadLine();
            
            if (proxyType == "http")
            {
                SetSystemProxy();
                AppendLog("Системный HTTP прокси установлен");
            }
            else
            {
                AppendLog("SOCKS5 прокси: " + proxyAddress + ":" + proxyPort);
            }
            
            AppendLog("✓ VPN запущен!");
        }

        private string GenerateXrayConfig(string vlessUrl, string proxyType, List<string> splitDomains)
        {
            try
            {
                if (!vlessUrl.StartsWith("vless://")) return null;

                Uri uri = new Uri(vlessUrl);
                var queryParams = HttpUtility.ParseQueryString(uri.Query);

                string uuid = uri.UserInfo;
                string address = uri.DnsSafeHost;
                int port = uri.Port;
                string type = queryParams["type"] ?? "tcp";
                string security = queryParams["security"] ?? "none";
                string flow = queryParams["flow"] ?? "";
                string sni = queryParams["sni"] ?? "";
                string pbk = queryParams["pbk"] ?? "";
                string sid = queryParams["sid"] ?? "";
                string fp = queryParams["fp"] ?? "chrome";

                int proxyPortInt = int.Parse(proxyPort);
                bool useSplit = splitDomains != null && splitDomains.Count > 0;

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"log\": { \"loglevel\": \"warning\" },");
                
                // DNS section with DoH
                if (dohEnabled)
                {
                    sb.AppendLine("  \"dns\": {");
                    sb.AppendLine("    \"servers\": [");
                    sb.AppendLine("      \"https://cloudflare-dns.com/dns-query\",");
                    sb.AppendLine("      \"https://dns.google/dns-query\",");
                    sb.AppendLine("      \"1.1.1.1\",");
                    sb.AppendLine("      \"8.8.8.8\"");
                    sb.AppendLine("    ]");
                    sb.AppendLine("  },");
                }
                
                // Inbound
                sb.AppendLine("  \"inbounds\": [{");
                if (proxyType == "http")
                {
                    sb.AppendLine("    \"tag\": \"http-in\",");
                    sb.AppendLine("    \"port\": " + proxyPortInt + ",");
                    sb.AppendLine("    \"listen\": \"" + proxyAddress + "\",");
                    sb.AppendLine("    \"protocol\": \"http\",");
                    sb.AppendLine("    \"settings\": {},");
                }
                else
                {
                    sb.AppendLine("    \"tag\": \"socks-in\",");
                    sb.AppendLine("    \"port\": " + proxyPortInt + ",");
                    sb.AppendLine("    \"listen\": \"" + proxyAddress + "\",");
                    sb.AppendLine("    \"protocol\": \"socks\",");
                    sb.AppendLine("    \"settings\": { \"auth\": \"noauth\", \"udp\": true },");
                }
                sb.AppendLine("    \"sniffing\": {");
                sb.AppendLine("      \"enabled\": true,");
                sb.AppendLine("      \"destOverride\": [\"http\", \"tls\"]");
                sb.AppendLine("    }");
                sb.AppendLine("  }],");
                
                // Routing
                sb.AppendLine("  \"routing\": {");
                sb.AppendLine("    \"domainStrategy\": \"AsIs\",");
                sb.AppendLine("    \"rules\": [");
                
                if (useSplit)
                {
                    sb.AppendLine("      {");
                    sb.AppendLine("        \"type\": \"field\",");
                    sb.AppendLine("        \"domain\": [");
                    for (int i = 0; i < splitDomains.Count; i++)
                    {
                        string d = splitDomains[i];
                        sb.Append("          \"domain:" + d + "\"");
                        if (i < splitDomains.Count - 1) sb.Append(",");
                        sb.AppendLine();
                    }
                    sb.AppendLine("        ],");
                    sb.AppendLine("        \"outboundTag\": \"proxy\"");
                    sb.AppendLine("      },");
                    sb.AppendLine("      {");
                    sb.AppendLine("        \"type\": \"field\",");
                    sb.AppendLine("        \"network\": \"tcp,udp\",");
                    sb.AppendLine("        \"outboundTag\": \"direct\"");
                    sb.AppendLine("      }");
                }
                else
                {
                    sb.AppendLine("      {");
                    sb.AppendLine("        \"type\": \"field\",");
                    sb.AppendLine("        \"network\": \"tcp,udp\",");
                    sb.AppendLine("        \"outboundTag\": \"proxy\"");
                    sb.AppendLine("      }");
                }
                sb.AppendLine("    ]");
                sb.AppendLine("  },");
                
                // Outbounds
                sb.AppendLine("  \"outbounds\": [");
                sb.AppendLine("    {");
                sb.AppendLine("      \"tag\": \"proxy\",");
                sb.AppendLine("      \"protocol\": \"vless\",");
                sb.AppendLine("      \"settings\": {");
                sb.AppendLine("        \"vnext\": [{");
                sb.AppendLine("          \"address\": \"" + address + "\",");
                sb.AppendLine("          \"port\": " + port + ",");
                sb.AppendLine("          \"users\": [{");
                sb.AppendLine("            \"id\": \"" + uuid + "\",");
                sb.Append("            \"encryption\": \"none\"");
                if (!string.IsNullOrEmpty(flow))
                {
                    sb.AppendLine(",");
                    sb.Append("            \"flow\": \"" + flow + "\"");
                }
                sb.AppendLine();
                sb.AppendLine("          }]");
                sb.AppendLine("        }]");
                sb.AppendLine("      },");
                sb.AppendLine("      \"streamSettings\": {");
                sb.AppendLine("        \"network\": \"" + type + "\",");
                sb.Append("        \"security\": \"" + security + "\"");
                
                if (security == "reality")
                {
                    sb.AppendLine(",");
                    sb.AppendLine("        \"realitySettings\": {");
                    sb.AppendLine("          \"serverName\": \"" + sni + "\",");
                    sb.AppendLine("          \"fingerprint\": \"" + fp + "\",");
                    sb.AppendLine("          \"show\": false,");
                    sb.AppendLine("          \"publicKey\": \"" + pbk + "\",");
                    sb.AppendLine("          \"shortId\": \"" + sid + "\"");
                    sb.Append("        }");
                }
                
                sb.AppendLine();
                sb.AppendLine("      }");
                sb.AppendLine("    },");
                sb.AppendLine("    {");
                sb.AppendLine("      \"tag\": \"direct\",");
                sb.AppendLine("      \"protocol\": \"freedom\",");
                sb.AppendLine("      \"settings\": {}");
                sb.AppendLine("    }");
                sb.AppendLine("  ]");
                sb.Append("}");
                
                return sb.ToString();
            }
            catch (Exception ex)
            {
                AppendLog("Ошибка генерации конфига: " + ex.Message);
                return null;
            }
        }

        private void StopVPN()
        {
            try
            {
                if (xrayProcess != null && !xrayProcess.HasExited)
                {
                    xrayProcess.Kill();
                    xrayProcess.WaitForExit(1000);
                }
            }
            catch { }
            finally { xrayProcess = null; }
        }

        private void SetSystemProxy()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                {
                    if (key != null)
                    {
                        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                        key.SetValue("ProxyServer", proxyAddress + ":" + proxyPort, RegistryValueKind.String);
                    }
                }
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            }
            catch { }
        }

        private void ResetSystemProxy()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                {
                    if (key != null) key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                }
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            }
            catch { }
        }
        #endregion

        #region Logging
        private void AppendLog(string message)
        {
            Dispatcher.InvokeAsync(new Action(delegate
            {
                if (LogBox != null)
                {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss");
                    LogBox.Text += "[" + timestamp + "] " + message + "\r\n";
                    
                    // Limit log size to prevent memory growth
                    if (LogBox.Text.Length > MAX_LOG_LENGTH)
                    {
                        LogBox.Text = LogBox.Text.Substring(LogBox.Text.Length - MAX_LOG_LENGTH / 2);
                    }
                    
                    LogBox.ScrollToEnd();
                }
            }));
        }
        
        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            if (LogBox != null) LogBox.Text = "";
            AppendLog("Логи очищены");
        }
        
        private void ShowConfig_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(lastConfigJson))
            {
                AppendLog("=== config.json ===");
                AppendLog(lastConfigJson);
            }
            else
            {
                AppendLog("Конфиг не сгенерирован");
            }
        }
        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace TemizlikMasaUygulamasi
{
    public partial class MainWindow : Window
    {
        private const uint RecycleFlagNoConfirmation = 0x00000001;
        private const uint RecycleFlagNoProgressUI = 0x00000002;
        private const uint RecycleFlagNoSound = 0x00000004;
        private const string UpdateRepoOwner = "vedatguldu";
        private const string UpdateRepoName = "temizlik-bakim-merkezi-professional";
        private static readonly HttpClient UpdateDownloadClient = CreateUpdateDownloadClient();

        private readonly string _scheduleTaskName = "TemizlikMasaUygulamasiV3Daily";
        private readonly string _dataDirectory;
        private readonly string _reportsDirectory;
        private readonly string _tributeConfigPath;
        private readonly string _profilePath;

        private readonly List<TaskExecutionRecord> _executionHistory = new();
        private readonly List<LargeFileRecord> _lastLargeFiles = new();

        private CancellationTokenSource? _cancellationTokenSource;
        private bool _screenReaderAnnouncementsEnabled = true;

        private bool _isFullScreen;
        private WindowState _savedWindowState;
        private WindowStyle _savedWindowStyle;
        private ResizeMode _savedResizeMode;
        private bool _savedTopmost;

        private ThemeMode _themeMode = ThemeMode.Light;
        private string _currentPanelKey = "Dashboard";
        private int _statusSequence;
        private bool _isUpdateCheckInProgress;
        private bool _openMenuOnAltKeyUp;
        private string _lastUpdateSummary = "Güncelleme durumu henüz denetlenmedi.";
        private TributeConfig _tributeConfig = new();

        public MainWindow()
        {
            InitializeComponent();

            _dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TemizlikMasaUygulamasiV3");
            _reportsDirectory = Path.Combine(_dataDirectory, "Raporlar");
            _tributeConfigPath = Path.Combine(_dataDirectory, "tribute-config.json");
            _profilePath = Path.Combine(_dataDirectory, "bakim-profil.json");
        }

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var appVersion = GetApplicationVersion();
            HeaderVersionText.Text = $"Sürüm {appVersion}";
            Title = $"Temizlik ve Bakım Merkezi Professional v{appVersion}";

            EnsureDirectories();
            PopulateScenarioPresets();
            PopulateScheduleSelectors();
            LoadTributeConfig();
            ApplyTributeToUi();

            LargeFileScanPathBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            FolderSizeRootBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            BackupSourceBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            BackupTargetBox.Text = Path.Combine(_dataDirectory, "Yedekler");

            SelectRecommended();
            ApplyTheme(ThemeMode.Light);
            RefreshContrastAudit();
            ShowPanel("Dashboard");

            SetStatus("Hazır");
            AppendLog("Temizlik ve Bakım Merkezi Professional v3.1.2 başlatıldı.");
            AppendLog($"Yönetici yetkisi: {(IsAdministrator() ? "Var" : "Yok")}");
            FeatureHubOutputBox.Text = "Professional 9 Özellik Merkezi hazır. İstediğiniz analizi başlatmak için bir düğmeye basın.";

            await RefreshScheduleStatusAsync();

            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--autorun-recommended", StringComparison.OrdinalIgnoreCase)))
            {
                AppendLog("Komut satırı parametresi algılandı: --autorun-recommended");
                _ = RunSelectedTasksAsync();
            }

            UpdateDashboardSummary();
            _ = CheckForUpdatesAsync(userInitiated: false);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var isAltOnlyPress =
                e.Key == Key.LeftAlt ||
                e.Key == Key.RightAlt ||
                (e.Key == Key.System && (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt));

            if (isAltOnlyPress)
            {
                _openMenuOnAltKeyUp = true;
                return;
            }

            if (e.Key == Key.System)
            {
                _openMenuOnAltKeyUp = false;
            }
        }

        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (!_openMenuOnAltKeyUp)
            {
                return;
            }

            if (e.Key != Key.LeftAlt && e.Key != Key.RightAlt)
            {
                return;
            }

            _openMenuOnAltKeyUp = false;
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            if (TryOpenClassicMenu())
            {
                e.Handled = true;
            }
        }

        private bool TryOpenClassicMenu()
        {
            if (MainMenu.Items.Count == 0 || MainMenu.Items[0] is not MenuItem firstItem)
            {
                return false;
            }

            MainMenu.Focus();
            firstItem.Focus();
            firstItem.IsSubmenuOpen = true;
            SetStatus("Üst menü açıldı. Yön tuşları ile gezinebilirsiniz.");
            return true;
        }

        private void NavigateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string panelKey)
            {
                return;
            }

            ShowPanel(panelKey);
        }

        private void MenuNavigate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not string panelKey)
            {
                return;
            }

            ShowPanel(panelKey);
            SetStatus($"Menü açıldı: {GetPanelDisplayName(panelKey)}");
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ShowPanel(string panelKey)
        {
            var normalizedKey = NormalizePanelKey(panelKey);
            _currentPanelKey = normalizedKey;

            DashboardPanel.Visibility = Visibility.Collapsed;
            CleanPanel.Visibility = Visibility.Collapsed;
            SystemPanel.Visibility = Visibility.Collapsed;
            ReportsPanel.Visibility = Visibility.Collapsed;
            AccessibilityPanel.Visibility = Visibility.Collapsed;
            TributePanel.Visibility = Visibility.Collapsed;
            ProPanel.Visibility = Visibility.Collapsed;

            switch (normalizedKey)
            {
                case "Dashboard":
                    DashboardPanel.Visibility = Visibility.Visible;
                    break;
                case "System":
                    SystemPanel.Visibility = Visibility.Visible;
                    break;
                case "Reports":
                    ReportsPanel.Visibility = Visibility.Visible;
                    break;
                case "Accessibility":
                    AccessibilityPanel.Visibility = Visibility.Visible;
                    break;
                case "Tribute":
                    TributePanel.Visibility = Visibility.Visible;
                    break;
                case "Pro":
                    ProPanel.Visibility = Visibility.Visible;
                    break;
                case "Clean":
                    CleanPanel.Visibility = Visibility.Visible;
                    break;
                default:
                    DashboardPanel.Visibility = Visibility.Visible;
                    break;
            }

            UpdateActiveNavigation();
            UpdateDashboardSummary();
        }

        private static string NormalizePanelKey(string panelKey)
        {
            return panelKey switch
            {
                "Dashboard" => "Dashboard",
                "Clean" => "Clean",
                "System" => "System",
                "Reports" => "Reports",
                "Accessibility" => "Accessibility",
                "Tribute" => "Tribute",
                "Pro" => "Pro",
                _ => "Dashboard",
            };
        }

        private void UpdateActiveNavigation()
        {
            if (!IsLoaded)
            {
                return;
            }

            var navButtons = new[]
            {
                NavDashboardButton,
                NavCleanButton,
                NavSystemButton,
                NavReportsButton,
                NavAccessibilityButton,
                NavTributeButton,
                NavProButton,
            };

            var activeButton = _currentPanelKey switch
            {
                "Dashboard" => NavDashboardButton,
                "Clean" => NavCleanButton,
                "System" => NavSystemButton,
                "Reports" => NavReportsButton,
                "Accessibility" => NavAccessibilityButton,
                "Tribute" => NavTributeButton,
                "Pro" => NavProButton,
                _ => NavDashboardButton,
            };

            var normalBackground = new SolidColorBrush(GetThemeColor("AccentBrush", ColorFromHex("#004A7C")));
            var normalForeground = new SolidColorBrush(GetThemeColor("AccentTextBrush", ColorFromHex("#FFFFFF")));
            var activeBackground = new SolidColorBrush(GetThemeColor("SurfaceBrush", ColorFromHex("#FFFFFF")));
            var activeForeground = new SolidColorBrush(GetThemeColor("PrimaryTextBrush", ColorFromHex("#111111")));
            var normalBorder = new SolidColorBrush(GetThemeColor("BorderBrushStrong", ColorFromHex("#314158")));

            foreach (var button in navButtons)
            {
                button.Background = normalBackground;
                button.Foreground = normalForeground;
                button.BorderBrush = normalBorder;
                button.BorderThickness = new Thickness(1.5);
                button.FontWeight = FontWeights.SemiBold;
            }

            activeButton.Background = activeBackground;
            activeButton.Foreground = activeForeground;
            activeButton.BorderBrush = normalBorder;
            activeButton.BorderThickness = new Thickness(3);
            activeButton.FontWeight = FontWeights.Bold;
        }

        private void ToggleFullScreenButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        private void ToggleFullScreen()
        {
            if (_isFullScreen)
            {
                WindowStyle = _savedWindowStyle;
                WindowState = _savedWindowState;
                ResizeMode = _savedResizeMode;
                Topmost = _savedTopmost;
                _isFullScreen = false;
                ToggleFullScreenButton.Content = "_Tam Ekran (F11)";
                SetStatus("Normal ekran moduna geçildi.");
                return;
            }

            _savedWindowState = WindowState;
            _savedWindowStyle = WindowStyle;
            _savedResizeMode = ResizeMode;
            _savedTopmost = Topmost;

            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            WindowState = WindowState.Maximized;
            _isFullScreen = true;
            ToggleFullScreenButton.Content = "_Tam Ekrandan Çık (F11)";
            SetStatus("Tam ekran moduna geçildi.");
        }

        private void ApplyLightTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(ThemeMode.Light);
        }

        private void ApplyDarkTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(ThemeMode.Dark);
        }

        private void ApplyTheme(ThemeMode mode)
        {
            _themeMode = mode;

            var appBg = mode == ThemeMode.Light ? ColorFromHex("#F4F7FC") : ColorFromHex("#0B0F14");
            var cardBg = mode == ThemeMode.Light ? ColorFromHex("#FFFFFF") : ColorFromHex("#11161D");
            var panelBg = mode == ThemeMode.Light ? ColorFromHex("#E2E9F5") : ColorFromHex("#17202B");
            var text = mode == ThemeMode.Light ? ColorFromHex("#111111") : ColorFromHex("#F8FAFC");
            var secondaryText = mode == ThemeMode.Light ? ColorFromHex("#1F2937") : ColorFromHex("#E5E7EB");
            var accent = mode == ThemeMode.Light ? ColorFromHex("#004A7C") : ColorFromHex("#8BD3FF");
            var accentText = mode == ThemeMode.Light ? ColorFromHex("#FFFFFF") : ColorFromHex("#001018");
            var strongBorder = mode == ThemeMode.Light ? ColorFromHex("#314158") : ColorFromHex("#CFE7FF");

            SetThemeBrush("AppBackgroundBrush", appBg);
            SetThemeBrush("SurfaceBrush", cardBg);
            SetThemeBrush("PanelBrush", panelBg);
            SetThemeBrush("PrimaryTextBrush", text);
            SetThemeBrush("SecondaryTextBrush", secondaryText);
            SetThemeBrush("AccentBrush", accent);
            SetThemeBrush("AccentTextBrush", accentText);
            SetThemeBrush("BorderBrushStrong", strongBorder);

            RootGrid.Background = new SolidColorBrush(appBg);
            HeaderBorder.Background = new SolidColorBrush(panelBg);
            ClassicMenuBorder.Background = new SolidColorBrush(panelBg);
            BottomNavBorder.Background = new SolidColorBrush(panelBg);
            StatusBorder.Background = new SolidColorBrush(panelBg);

            SetPanelBackground(cardBg);
            SetTextColor(text);
            UpdateActiveNavigation();
            UpdateThemeButtons();

            RefreshContrastAudit();
            SetStatus(mode == ThemeMode.Light ? "Aydınlık tema uygulandı." : "Karanlık tema uygulandı.");
            UpdateDashboardSummary();
        }

        private void UpdateThemeButtons()
        {
            var lightActive = _themeMode == ThemeMode.Light;
            ThemeLightButton.Content = lightActive ? "_Aydınlık Tema (Etkin)" : "_Aydınlık Tema";
            ThemeDarkButton.Content = lightActive ? "_Karanlık Tema" : "_Karanlık Tema (Etkin)";
            ThemeStateText.Text = $"Tema: {GetThemeDisplayName()}";
        }

        private void SetThemeBrush(string key, Color color)
        {
            Resources[key] = new SolidColorBrush(color);
        }

        private static Color ColorFromHex(string value)
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }

        private void SetPanelBackground(Color color)
        {
            var brush = new SolidColorBrush(color);
            TasksBorder.Background = brush;
            LogBorder.Background = brush;
            ToolsBorder.Background = brush;
            SchedulerBorder.Background = brush;
            DiagnosticsBorder.Background = brush;
            ReportConfigBorder.Background = brush;
            ReportInfoBorder.Background = brush;
            ReportPreviewBorder.Background = brush;
            AccessibilityBorder.Background = brush;
            WcagBorder.Background = brush;
            TributeInfoBorder.Background = brush;
            TributeEditBorder.Background = brush;
            HealthScoreBorder.Background = brush;
            ScenarioBorder.Background = brush;
            LargeFileBorder.Background = brush;
            BackupBorder.Background = brush;
            AnalyticsBorder.Background = brush;
            NetworkDiagBorder.Background = brush;
            FolderSizeBorder.Background = brush;
            ProfileBorder.Background = brush;
            NineFeaturesBorder.Background = brush;
            DashboardHeroBorder.Background = brush;
            DashboardStatusBorder.Background = brush;
            DashboardQuickActionsBorder.Background = brush;
            MainMenu.Background = brush;

            LogBox.Background = brush;
            ReportPreviewBox.Background = brush;
            PreflightOutputBox.Background = brush;
            AnalyticsOutputBox.Background = brush;
            TributeLongNarrativeBox.Background = brush;
            FeatureHubOutputBox.Background = brush;
        }

        private void SetTextColor(Color color)
        {
            var brush = new SolidColorBrush(color);
            Foreground = brush;
            LogBox.Foreground = brush;
            ReportPreviewBox.Foreground = brush;
            PreflightOutputBox.Foreground = brush;
            AnalyticsOutputBox.Foreground = brush;
            TributeLongNarrativeBox.Foreground = brush;
            FeatureHubOutputBox.Foreground = brush;
            MainMenu.Foreground = brush;
        }

        private void RunAsAdminButton_Click(object sender, RoutedEventArgs e)
        {
            RestartAsAdministrator();
        }

        private bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private void RestartAsAdministrator()
        {
            if (IsAdministrator())
            {
                AppendLog("Uygulama zaten yönetici olarak çalışıyor.");
                return;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                MessageBox.Show(this, "Çalıştırılabilir dosya yolu bulunamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = true,
                    Verb = "runas",
                });

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Yönetici moduna geçilemedi: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectRecommended_Click(object sender, RoutedEventArgs e)
        {
            SelectRecommended();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllTasksChecked(true);
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            SetAllTasksChecked(false);
        }

        private void SelectRecommended()
        {
            TaskUserTemp.IsChecked = true;
            TaskWindowsTemp.IsChecked = true;
            TaskRecycleBin.IsChecked = true;
            TaskDns.IsChecked = true;
            TaskNetworkRefresh.IsChecked = false;
            TaskResetUpdate.IsChecked = false;
            TaskBrowserCache.IsChecked = false;
            AppendLog("Önerilen görev seçimi yüklendi.");
        }

        private void SetAllTasksChecked(bool isChecked)
        {
            TaskUserTemp.IsChecked = isChecked;
            TaskWindowsTemp.IsChecked = isChecked;
            TaskRecycleBin.IsChecked = isChecked;
            TaskDns.IsChecked = isChecked;
            TaskNetworkRefresh.IsChecked = isChecked;
            TaskResetUpdate.IsChecked = isChecked;
            TaskBrowserCache.IsChecked = isChecked;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _ = RunSelectedTasksAsync();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            AppendLog("Durdurma isteği gönderildi.");
        }

        private async Task RunSelectedTasksAsync()
        {
            if (_cancellationTokenSource != null)
            {
                SetStatus("Zaten çalışan bir işlem var.");
                return;
            }

            var tasks = GetSelectedTasks();
            if (tasks.Count == 0)
            {
                SetStatus("Çalıştırılacak görev seçin.");
                return;
            }

            if (tasks.Any(t => t.RequiresAdmin) && !IsAdministrator())
            {
                var answer = MessageBox.Show(
                    this,
                    "Seçili görevler yönetici yetkisi gerektiriyor. Yönetici moduna geçilsin mi?",
                    "Yönetici Yetkisi",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (answer == MessageBoxResult.Yes)
                {
                    RestartAsAdministrator();
                }

                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;
            SetUiRunningState(true);
            ProgressBar.Value = 0;

            var completed = 0;
            try
            {
                foreach (var task in tasks)
                {
                    token.ThrowIfCancellationRequested();
                    SetStatus($"Çalışıyor: {task.Name}");
                    AppendLog($"Başladı: {task.Name}");

                    var record = new TaskExecutionRecord
                    {
                        TaskName = task.Name,
                        StartedAt = DateTime.Now,
                        Status = "Running",
                    };
                    _executionHistory.Add(record);

                    try
                    {
                        await task.Action(token);
                        record.Status = "Completed";
                        completed++;
                        AppendLog($"Tamamlandı: {task.Name}");
                    }
                    catch (OperationCanceledException)
                    {
                        record.Status = "Canceled";
                        record.Detail = "Kullanıcı iptal etti";
                        AppendLog($"İptal edildi: {task.Name}");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        record.Status = "Failed";
                        record.Detail = ex.Message;
                        AppendLog($"Hata: {task.Name} -> {ex.Message}");
                    }
                    finally
                    {
                        record.FinishedAt = DateTime.Now;
                    }

                    ProgressBar.Value = (double)completed / tasks.Count * 100.0;
                }

                SetStatus($"İşlem bitti. Tamamlanan: {completed}/{tasks.Count}");
            }
            catch (OperationCanceledException)
            {
                SetStatus("İşlem kullanıcı tarafından durduruldu.");
            }
            finally
            {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
                SetUiRunningState(false);
            }
        }

        private void SetUiRunningState(bool running)
        {
            StartButton.IsEnabled = !running;
            CancelButton.IsEnabled = running;
            SaveLogButton.IsEnabled = !running;
            SaveJsonReportButton.IsEnabled = !running;
        }

        private List<CleanupTask> GetSelectedTasks()
        {
            var list = new List<CleanupTask>();

            if (TaskUserTemp.IsChecked == true)
            {
                list.Add(new CleanupTask("Kullanıcı temp temizliği", false, CleanUserTempAsync));
            }

            if (TaskWindowsTemp.IsChecked == true)
            {
                list.Add(new CleanupTask("Windows temp temizliği", true, CleanWindowsTempAsync));
            }

            if (TaskRecycleBin.IsChecked == true)
            {
                list.Add(new CleanupTask("Geri dönüşüm kutusu", false, EmptyRecycleBinAsync));
            }

            if (TaskDns.IsChecked == true)
            {
                list.Add(new CleanupTask("DNS temizliği", true, ResetDnsAsync));
            }

            if (TaskNetworkRefresh.IsChecked == true)
            {
                list.Add(new CleanupTask("Ağ yenileme", true, RefreshNetworkAsync));
            }

            if (TaskResetUpdate.IsChecked == true)
            {
                list.Add(new CleanupTask("Windows Update sıfırlama", true, ResetWindowsUpdateAsync));
            }

            if (TaskBrowserCache.IsChecked == true)
            {
                list.Add(new CleanupTask("Tarayıcı önbellek temizliği", false, CleanBrowserCachesAsync));
            }

            return list;
        }

        private async Task CleanUserTempAsync(CancellationToken token)
        {
            await Task.Run(() =>
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var dirs = new[]
                {
                    Path.GetTempPath(),
                    Path.Combine(local, "Temp"),
                    Path.Combine(local, "Microsoft", "Windows", "INetCache"),
                };

                var deleted = DeleteContents(dirs, token);
                AppendLog($"Kullanıcı temp temizliği tamamlandı. Silinen öğe: {deleted}");
            }, token);
        }

        private async Task CleanWindowsTempAsync(CancellationToken token)
        {
            await Task.Run(() =>
            {
                var windowsTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                var deleted = DeleteContents(new[] { windowsTemp }, token);
                AppendLog($"Windows temp temizliği tamamlandı. Silinen öğe: {deleted}");
            }, token);
        }

        private async Task EmptyRecycleBinAsync(CancellationToken token)
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var result = SHEmptyRecycleBin(IntPtr.Zero, null, RecycleFlagNoConfirmation | RecycleFlagNoProgressUI | RecycleFlagNoSound);
                if (result != 0)
                {
                    throw new InvalidOperationException($"Geri dönüşüm kutusu boşaltılamadı. Kod: 0x{result:X8}");
                }
            }, token);
        }

        private async Task ResetDnsAsync(CancellationToken token)
        {
            await RunAndLogAsync("ipconfig", "/flushdns", token, true);
            await RunAndLogAsync("ipconfig", "/registerdns", token, false);
        }

        private async Task RefreshNetworkAsync(CancellationToken token)
        {
            await RunAndLogAsync("ipconfig", "/release", token, false);
            await RunAndLogAsync("ipconfig", "/renew", token, false);
            await RunAndLogAsync("arp", "-d *", token, false);
            await RunAndLogAsync("nbtstat", "-R", token, false);
            await RunAndLogAsync("nbtstat", "-RR", token, false);
        }

        private async Task ResetWindowsUpdateAsync(CancellationToken token)
        {
            var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var softwareDistribution = Path.Combine(windowsPath, "SoftwareDistribution");
            var backupPath = Path.Combine(windowsPath, $"SoftwareDistribution.bak.{DateTime.Now:yyyyMMddHHmmss}");

            await RunAndLogAsync("net", "stop wuauserv", token, false);
            await RunAndLogAsync("net", "stop bits", token, false);

            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(softwareDistribution))
                {
                    Directory.Move(softwareDistribution, backupPath);
                    AppendLog($"SoftwareDistribution yedeklendi: {backupPath}");
                }

                Directory.CreateDirectory(softwareDistribution);
            }, token);

            await RunAndLogAsync("net", "start bits", token, false);
            await RunAndLogAsync("net", "start wuauserv", token, false);
        }

        private async Task CleanBrowserCachesAsync(CancellationToken token)
        {
            await Task.Run(() =>
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var dirs = new[]
                {
                    Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"),
                    Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache"),
                    Path.Combine(local, "Mozilla", "Firefox", "Profiles"),
                    Path.Combine(user, "AppData", "Local", "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache"),
                };

                var deleted = DeleteContents(dirs, token);
                AppendLog($"Tarayıcı önbelleği temizliği tamamlandı. Silinen öğe: {deleted}");
            }, token);
        }

        private static int DeleteContents(IEnumerable<string> roots, CancellationToken token)
        {
            var deleted = 0;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                AttributesToSkip = 0,
            };

            foreach (var root in roots.Where(Directory.Exists))
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                        deleted++;
                    }
                    catch
                    {
                        // Kilitli veya yetkisiz dosyalar atlanır.
                    }
                }

                foreach (var dir in Directory.EnumerateDirectories(root, "*", options).OrderByDescending(d => d.Length))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch
                    {
                        // Açık tutulan klasörler atlanır.
                    }
                }
            }

            return deleted;
        }

        private async Task RunAndLogAsync(string fileName, string args, CancellationToken token, bool throwIfFailed)
        {
            AppendLog($"> {fileName} {args}");
            var result = await RunCommandAsync(fileName, args, token);

            foreach (var line in result.OutputLines.Take(12))
            {
                AppendLog($"  {line}");
            }

            if (result.OutputLines.Count > 12)
            {
                AppendLog($"  ... ({result.OutputLines.Count - 12} satır daha)");
            }

            if (result.ExitCode != 0)
            {
                var message = $"Komut başarısız: {fileName} {args} (Kod: {result.ExitCode})";
                if (throwIfFailed)
                {
                    throw new InvalidOperationException(message);
                }

                AppendLog(message);
            }
        }

        private static async Task<CommandResult> RunCommandAsync(string fileName, string arguments, CancellationToken token)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = startInfo };
            var lines = new List<string>();
            var gate = new object();

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    lock (gate)
                    {
                        lines.Add(e.Data.Trim());
                    }
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    lock (gate)
                    {
                        lines.Add(e.Data.Trim());
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }

                throw;
            }

            return new CommandResult(process.ExitCode, lines);
        }

        private async void CreateSystemSnapshot_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("Sistem özeti", false, _ =>
            {
                var snapshot = BuildSystemSnapshot();
                PreflightOutputBox.Text = snapshot;
                AppendLog("Sistem özeti üretildi.");
                return Task.CompletedTask;
            });
        }

        private string BuildSystemSnapshot()
        {
            var lines = new List<string>
            {
                $"Tarih: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"Bilgisayar: {Environment.MachineName}",
                $"Kullanıcı: {Environment.UserName}",
                $"İşletim sistemi: {RuntimeInformation.OSDescription}",
                $".NET: {RuntimeInformation.FrameworkDescription}",
                $"Yönetici: {IsAdministrator()}",
                $"Çalışma süresi (dk): {Environment.TickCount64 / 60000}",
                "Diskler:",
            };

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var freeGb = drive.AvailableFreeSpace / (1024d * 1024d * 1024d);
                var totalGb = drive.TotalSize / (1024d * 1024d * 1024d);
                lines.Add($"- {drive.Name} {drive.DriveFormat} | Boş: {freeGb:F1} GB / Toplam: {totalGb:F1} GB");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private async void RunSfcVerify_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("SFC Verify", true, token => RunAndLogAsync("sfc", "/verifyonly", token, false));
        }

        private async void RunDismCheckHealth_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("DISM CheckHealth", true, token => RunAndLogAsync("dism", "/Online /Cleanup-Image /CheckHealth", token, false));
        }

        private void OpenUserTempFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenPath(Path.GetTempPath());
        }

        private void OpenReportsFolder_Click(object sender, RoutedEventArgs e)
        {
            EnsureDirectories();
            OpenPath(_reportsDirectory);
        }

        private async void CreateSchedule_Click(object sender, RoutedEventArgs e)
        {
            if (ScheduleHourBox.SelectedItem is not string hour || ScheduleMinuteBox.SelectedItem is not string minute)
            {
                SetScheduleStatus("Saat ve dakika seçin.");
                return;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                SetScheduleStatus("Çalıştırılabilir dosya yolu bulunamadı.");
                return;
            }

            var args = $"/Create /SC DAILY /TN \"{_scheduleTaskName}\" /TR \"\\\"{executable}\\\" --autorun-recommended\" /ST {hour}:{minute} /F";
            await RunSingleOperationAsync("Zamanlayıcı oluşturma", true, token => RunAndLogAsync("schtasks", args, token, true));
            await RefreshScheduleStatusAsync();
        }

        private async void DeleteSchedule_Click(object sender, RoutedEventArgs e)
        {
            var args = $"/Delete /TN \"{_scheduleTaskName}\" /F";
            await RunSingleOperationAsync("Zamanlayıcı silme", true, token => RunAndLogAsync("schtasks", args, token, false));
            await RefreshScheduleStatusAsync();
        }

        private async Task RefreshScheduleStatusAsync()
        {
            var result = await RunCommandAsync("schtasks", $"/Query /TN \"{_scheduleTaskName}\"", CancellationToken.None);
            SetScheduleStatus(result.ExitCode == 0 ? "Günlük zamanlayıcı etkin." : "Günlük zamanlayıcı etkin değil.");
        }

        private void SetScheduleStatus(string message)
        {
            ScheduleStatusText.Text = message;
            RaiseLiveRegionChanged(ScheduleStatusText);
            UpdateDashboardSummary();
        }

        private void PopulateScheduleSelectors()
        {
            ScheduleHourBox.Items.Clear();
            ScheduleMinuteBox.Items.Clear();

            for (var i = 0; i < 24; i++)
            {
                ScheduleHourBox.Items.Add(i.ToString("D2"));
            }

            for (var i = 0; i < 60; i += 5)
            {
                ScheduleMinuteBox.Items.Add(i.ToString("D2"));
            }

            ScheduleHourBox.SelectedItem = DateTime.Now.Hour.ToString("D2");
            var minute = DateTime.Now.Minute - DateTime.Now.Minute % 5;
            ScheduleMinuteBox.SelectedItem = minute.ToString("D2");
        }

        private async void RunPreflightCheck_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("Ön uçuş kontrolü", false, _ =>
            {
                var lines = new List<string>
                {
                    $"Yönetici modu: {(IsAdministrator() ? "Var" : "Yok")}",
                    $"Rapor klasörü mevcut: {Directory.Exists(_reportsDirectory)}",
                    $"Kullanıcı temp mevcut: {Directory.Exists(Path.GetTempPath())}",
                    $"İnternet host tanımı: {NetworkTestHostBox.Text}",
                    $"Tema: {GetThemeDisplayName()}",
                };

                PreflightOutputBox.Text = string.Join(Environment.NewLine, lines);
                AppendLog("Ön uçuş kontrolü tamamlandı.");
                return Task.CompletedTask;
            });
        }

        private async void GenerateStartupReport_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("Başlangıç raporu", false, _ =>
            {
                EnsureDirectories();
                var path = Path.Combine(_reportsDirectory, $"startup-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                File.WriteAllText(path, BuildSystemSnapshot(), Encoding.UTF8);
                AppendLog($"Başlangıç raporu oluşturuldu: {path}");
                LastReportPathText.Text = $"Son rapor: {path}";
                return Task.CompletedTask;
            });
        }

        private async void AnalyzeCriticalServices_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("Servis sağlığı", false, async token =>
            {
                var services = new[] { "wuauserv", "bits", "Dnscache" };
                var lines = new List<string>();
                foreach (var service in services)
                {
                    var result = await RunCommandAsync("sc", $"query {service}", token);
                    var state = result.OutputLines.FirstOrDefault(l => l.Contains("STATE", StringComparison.OrdinalIgnoreCase)) ?? "STATE bulunamadı";
                    lines.Add($"{service}: {state}");
                }

                PreflightOutputBox.Text = string.Join(Environment.NewLine, lines);
            });
        }

        private async void GenerateEventLogSummary_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("Olay günlüğü özeti", false, async token =>
            {
                var result = await RunCommandAsync("wevtutil", "qe System /c:30 /rd:true /f:text", token);
                var text = string.Join(Environment.NewLine, result.OutputLines.Take(120));
                PreflightOutputBox.Text = text;
                AppendLog("Olay günlüğü özeti üretildi.");
            });
        }

        private void SaveLogButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Log dosyasını kaydet",
                Filter = "Metin dosyası (*.txt)|*.txt|Tüm dosyalar (*.*)|*.*",
                FileName = $"temizlik-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            File.WriteAllText(dialog.FileName, LogBox.Text, Encoding.UTF8);
            AppendLog($"Log kaydedildi: {dialog.FileName}");
        }

        private void SaveJsonReportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var fullPath = SaveJsonReport();
                LastReportPathText.Text = $"Son rapor: {fullPath}";
                SetStatus("JSON raporu oluşturuldu.");
            }
            catch (Exception ex)
            {
                AppendLog($"Rapor oluşturma hatası: {ex.Message}");
                SetStatus("Rapor oluşturulamadı.");
            }
        }

        private string SaveJsonReport()
        {
            EnsureDirectories();
            var report = new ApplicationReport
            {
                Title = string.IsNullOrWhiteSpace(ReportTitleBox.Text) ? "Temizlik ve Bakım Raporu v3" : ReportTitleBox.Text.Trim(),
                GeneratedAt = DateTime.Now,
                Tribute = _tributeConfig,
                SelectedTasks = GetSelectedTasks().Select(t => t.Name).ToList(),
                ExecutionHistory = _executionHistory.ToList(),
                LogText = LogBox.Text,
                SystemSnapshot = IncludeSystemSnapshotInReport.IsChecked == true ? BuildSystemSnapshot() : null,
            };

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            var fileName = $"{SanitizeFileName(report.Title)}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            var path = Path.Combine(_reportsDirectory, fileName);
            File.WriteAllText(path, json, Encoding.UTF8);
            ReportPreviewBox.Text = json;
            AppendLog($"JSON raporu yazıldı: {path}");
            return path;
        }

        private async void CreateEmergencySupportPackage_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("Acil destek paketi", false, _ =>
            {
                EnsureDirectories();
                var packageRoot = Path.Combine(_dataDirectory, "SupportPackage");
                if (Directory.Exists(packageRoot))
                {
                    Directory.Delete(packageRoot, true);
                }

                Directory.CreateDirectory(packageRoot);
                File.WriteAllText(Path.Combine(packageRoot, "log.txt"), LogBox.Text, Encoding.UTF8);
                File.WriteAllText(Path.Combine(packageRoot, "system.txt"), BuildSystemSnapshot(), Encoding.UTF8);

                var lastJson = Directory.GetFiles(_reportsDirectory, "*.json").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(lastJson))
                {
                    File.Copy(lastJson, Path.Combine(packageRoot, Path.GetFileName(lastJson)), true);
                }

                var zip = Path.Combine(_reportsDirectory, $"support-package-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                ZipFile.CreateFromDirectory(packageRoot, zip, CompressionLevel.Optimal, false);
                LastReportPathText.Text = $"Son paket: {zip}";
                AppendLog($"Acil destek paketi üretildi: {zip}");
                return Task.CompletedTask;
            });
        }

        private void ApplyAccessibility_Click(object sender, RoutedEventArgs e)
        {
            _screenReaderAnnouncementsEnabled = ScreenReaderAnnouncementsCheck.IsChecked == true;
            FontSize = FontScaleSlider.Value;
            LogBox.FontSize = Math.Max(14, FontScaleSlider.Value - 1);
            ReportPreviewBox.FontSize = Math.Max(14, FontScaleSlider.Value - 1);
            PreflightOutputBox.FontSize = Math.Max(14, FontScaleSlider.Value - 1);
            AnalyticsOutputBox.FontSize = Math.Max(14, FontScaleSlider.Value - 1);
            TributeLongNarrativeBox.FontSize = Math.Max(14, FontScaleSlider.Value - 1);
            FeatureHubOutputBox.FontSize = Math.Max(14, FontScaleSlider.Value - 1);

            if (HighContrastModeCheck.IsChecked == true)
            {
                ApplyTheme(ThemeMode.Dark);
            }

            RefreshContrastAudit();
            SetStatus("Erişilebilirlik ayarları uygulandı.");
        }

        private void ResetAccessibility_Click(object sender, RoutedEventArgs e)
        {
            FontScaleSlider.Value = 18;
            HighContrastModeCheck.IsChecked = false;
            ScreenReaderAnnouncementsCheck.IsChecked = true;
            _screenReaderAnnouncementsEnabled = true;
            FontSize = 18;
            ApplyTheme(ThemeMode.Light);
            SetStatus("Erişilebilirlik ayarları varsayılana döndürüldü.");
        }

        private void RefreshContrastAudit_Click(object sender, RoutedEventArgs e)
        {
            RefreshContrastAudit();
        }

        private void RefreshContrastAudit()
        {
            var textColor = ((SolidColorBrush)Foreground).Color;
            var cardBgColor = ((SolidColorBrush)TasksBorder.Background).Color;
            var buttonBg = GetThemeColor("AccentBrush", ColorFromHex("#004A7C"));
            var buttonFg = GetThemeColor("AccentTextBrush", ColorFromHex("#FFFFFF"));
            var headerBg = ((SolidColorBrush)HeaderBorder.Background).Color;

            var bodyRatio = CalculateContrastRatio(textColor, cardBgColor);
            var buttonRatio = CalculateContrastRatio(buttonFg, buttonBg);
            var headerRatio = CalculateContrastRatio(textColor, headerBg);
            var minRatio = new[] { bodyRatio, buttonRatio, headerRatio }.Min();
            var pass = minRatio >= 7.0;

            ContrastInfoText.Text =
                $"Ana metin / kart: {bodyRatio:F2}:1{Environment.NewLine}" +
                $"Düğme metni / düğme zemin: {buttonRatio:F2}:1{Environment.NewLine}" +
                $"Başlık metni / başlık zemin: {headerRatio:F2}:1{Environment.NewLine}" +
                $"WCAG AAA sonucu (normal metin >= 7:1): {(pass ? "Geçti" : "Geçemedi")}";
        }

        private Color GetThemeColor(string key, Color fallback)
        {
            if (Resources[key] is SolidColorBrush brush)
            {
                return brush.Color;
            }

            return fallback;
        }

        private static double CalculateContrastRatio(Color a, Color b)
        {
            static double Channel(double c)
            {
                c /= 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }

            static double Luminance(Color c)
            {
                return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
            }

            var l1 = Luminance(a);
            var l2 = Luminance(b);
            var lighter = Math.Max(l1, l2);
            var darker = Math.Min(l1, l2);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private void OpenSelectedSource_Click(object sender, RoutedEventArgs e)
        {
            if (SourceList.SelectedItem is not ListBoxItem item || item.Content is not string url)
            {
                SetStatus("Önce bir kaynak seçin.");
                return;
            }

            OpenUrl(url);
            AppendLog($"Kaynak açıldı: {url}");
        }

        private void LoadTributeConfig()
        {
            _tributeConfig = new TributeConfig
            {
                PersonName = "Olcay Aşçı",
                LegacyAppName = "temizlik v2.cmd",
                TributeMessage = "Bu uygulama, Vedat Güldü tarafından; temizlik v2.cmd mirasını üreten Olcay Aşçı'nın emeğine, mücadelesine ve erişilebilir teknoloji idealine saygı için v3 olarak geliştirilmiştir.",
                LongNarrative = BuildDefaultLongTributeNarrative(),
            };

            try
            {
                if (!File.Exists(_tributeConfigPath))
                {
                    return;
                }

                var json = File.ReadAllText(_tributeConfigPath, Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<TributeConfig>(json);
                if (loaded != null)
                {
                    _tributeConfig = loaded;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"İthaf yapılandırması okunamadı: {ex.Message}");
            }
        }

        private string BuildDefaultLongTributeNarrative()
        {
             return "Olcay Aşçı, teknolojiyi yalnızca kod yazma işi olarak görmeyen; onu günlük yaşamı kolaylaştıran, insan onurunu koruyan ve eşit erişimi güçlendiren bir sorumluluk alanı olarak ele alan bir isimdir. " +
                 "Onun yaklaşımında yazılım, insanın hayatına değer katmıyorsa eksik kalır. Temizlik v2.cmd gibi pratik çözümler üretmesi de bu çizginin yansımasıdır: karmaşık bakım adımlarını sadeleştirmek, kullanıcıyı teknik ayrıntılarda boğmadan sonuç odaklı bir deneyim sunmak.\r\n\r\n" +
                 "Olcay Aşçı'yı anlatırken özellikle vurgulanması gereken nokta, erişilebilirlik ve kapsayıcı tasarım konularında verdiği mücadeledir. " +
                 "Ekran okuyucu kullanan kullanıcıların, düşük görüşe sahip bireylerin ve farklı öğrenme/algılama ihtiyaçları olan insanların teknolojiye eşit düzeyde erişebilmesi için görünmeyen engelleri görünür kılmaya çalışmıştır. " +
                 "Bu mücadele, yalnızca teknik bir tercih değil; aynı zamanda adalet, eşitlik ve dijital haklar meselesi olarak ele alınmıştır.\r\n\r\n" +
                 "Mücadelesinin zor tarafı çoğu zaman görünmezdi: 'herkese uygunmuş gibi görünen' ama gerçekte birçok kullanıcıyı dışarıda bırakan tasarımlara karşı çıkmak, " +
                 "erişilebilirlikten ödün veren hızlı ama kırılgan çözümler yerine daha sürdürülebilir ve kapsayıcı bir yol önermek, " +
                 "standartların uygulanmadığı ortamlarda kullanıcı adına ısrarla doğruyu savunmak. " +
                 "Bu nedenle onun adı, sadece bir geliştirici profili değil; erişilebilir teknoloji kültürünü diri tutan bir emek ve vicdan çizgisi olarak anılır.\r\n\r\n" +
                 "Temizlik ve Bakım Merkezi v3, bu emeği saygıyla yaşatmak amacıyla oluşturulmuştur. Vedat Güldü tarafından geliştirilen bu sürüm; temizlik v2.cmd mirasını korurken güvenliği, okunabilirliği, klavye ile kullanımı, ekran okuyucu uyumluluğunu ve bakım kapsamını ileri seviyeye taşımayı hedefler. " +
                 "Buradaki ithaf, yalnızca geçmişi anmak için yazılmış bir metin değildir; her kullanıcıya eşit fırsat sunan teknoloji anlayışına verilmiş, sürdürülebilir bir sözün ifadesidir.";
        }

        private void ApplyTributeToUi()
        {
            TributePersonBox.Text = _tributeConfig.PersonName;
            LegacyAppBox.Text = _tributeConfig.LegacyAppName;
            TributeMessageBox.Text = _tributeConfig.TributeMessage;
            TributeLongNarrativeBox.Text = _tributeConfig.LongNarrative;

            TributeBannerText.Text = $"Vedat Güldü tarafından, {_tributeConfig.PersonName} anısına";
            ResearchSummaryText.Text = "Olcay Aşçı, kullanıcı merkezli teknoloji yaklaşımı; erişilebilirlik, kapsayıcı tasarım ve görünmeyen engellere karşı verdiği tutarlı mücadele ile bu sürümün temel ilham kaynağıdır.";
            TributePreviewText.Text = $"{_tributeConfig.TributeMessage}{Environment.NewLine}Kişi: {_tributeConfig.PersonName}{Environment.NewLine}Miras: {_tributeConfig.LegacyAppName}";
        }

        private void ApplyTributeButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateTributeConfigFromUi();
            ApplyTributeToUi();
            SetStatus("İthaf metni uygulandı.");
        }

        private void SaveTributeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureDirectories();
                UpdateTributeConfigFromUi();
                var json = JsonSerializer.Serialize(_tributeConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_tributeConfigPath, json, Encoding.UTF8);
                SetStatus("İthaf metni kalıcı olarak kaydedildi.");
                AppendLog($"İthaf ayarı kaydedildi: {_tributeConfigPath}");
            }
            catch (Exception ex)
            {
                SetStatus("İthaf kaydedilemedi.");
                AppendLog($"İthaf kaydetme hatası: {ex.Message}");
            }
        }

        private void UpdateTributeConfigFromUi()
        {
            _tributeConfig.PersonName = string.IsNullOrWhiteSpace(TributePersonBox.Text) ? "Olcay Aşçı" : TributePersonBox.Text.Trim();
            _tributeConfig.LegacyAppName = string.IsNullOrWhiteSpace(LegacyAppBox.Text) ? "temizlik v2.cmd" : LegacyAppBox.Text.Trim();
            _tributeConfig.TributeMessage = string.IsNullOrWhiteSpace(TributeMessageBox.Text)
                ? "Bu uygulama, Vedat Güldü tarafından Olcay Aşçı anısına geliştirilmiştir."
                : TributeMessageBox.Text.Trim();

            _tributeConfig.LongNarrative = string.IsNullOrWhiteSpace(TributeLongNarrativeBox.Text)
                ? BuildDefaultLongTributeNarrative()
                : TributeLongNarrativeBox.Text.Trim();
        }

        private void AnalyzeHealthScore_Click(object sender, RoutedEventArgs e)
        {
            var (score, recommendations) = CalculateHealthScore();
            HealthScoreText.Text = $"Skor: {score}/100";
            HealthRecommendationsList.Items.Clear();
            foreach (var recommendation in recommendations)
            {
                HealthRecommendationsList.Items.Add(recommendation);
            }

            SetStatus("Sağlık skoru hesaplandı.");
        }

        private (int Score, List<string> Recommendations) CalculateHealthScore()
        {
            var score = 100;
            var recommendations = new List<string>();

            var cDrive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase));
            if (cDrive != null)
            {
                var ratio = (double)cDrive.AvailableFreeSpace / cDrive.TotalSize;
                if (ratio < 0.15)
                {
                    score -= 30;
                    recommendations.Add("Disk boş alanı düşük. Büyük Dosya Radar önerilir.");
                }
                else if (ratio < 0.25)
                {
                    score -= 15;
                    recommendations.Add("Disk boş alanı orta. Düzenli temizlik önerilir.");
                }
            }

            if (!IsAdministrator())
            {
                score -= 10;
                recommendations.Add("Yönetici modunda çalıştırma önerilir.");
            }

            var failed = _executionHistory.Count(x => x.Status == "Failed");
            if (failed > 0)
            {
                score -= Math.Min(30, failed * 5);
                recommendations.Add("Geçmişte başarısız görevler var. Ön Uçuş Kontrolü çalıştırın.");
            }

            if (_executionHistory.Count == 0)
            {
                score -= 10;
                recommendations.Add("Henüz bakım geçmişi yok. İlk bakım çalıştırılmalı.");
            }

            recommendations.Add("Olcay Aşçı anısına tasarlanan v3 profilleri ile düzenli bakım önerilir.");
            score = Math.Clamp(score, 0, 100);
            return (score, recommendations);
        }

        private void ApplyHealthRecommendations_Click(object sender, RoutedEventArgs e)
        {
            var (_, recs) = CalculateHealthScore();
            if (recs.Any(r => r.Contains("Disk boş", StringComparison.OrdinalIgnoreCase)))
            {
                TaskUserTemp.IsChecked = true;
                TaskRecycleBin.IsChecked = true;
            }

            if (recs.Any(r => r.Contains("Yönetici", StringComparison.OrdinalIgnoreCase)))
            {
                AppendLog("Öneri: Yönetici modu ile tekrar çalıştırın.");
            }

            SetStatus("Sağlık önerileri uygulandı.");
        }

        private void PopulateScenarioPresets()
        {
            ScenarioPresetBox.Items.Clear();
            ScenarioPresetBox.Items.Add("Hızlı Günlük Temizlik");
            ScenarioPresetBox.Items.Add("Derin Temizlik + Ağ Onarım");
            ScenarioPresetBox.Items.Add("Güncelleme Kurtarma");
            ScenarioPresetBox.Items.Add("Sessiz Güvenli Profil");
            ScenarioPresetBox.Items.Add("Erişilebilirlik Odaklı Profil");
            ScenarioPresetBox.SelectedIndex = 0;
        }

        private void ApplyScenarioPreset_Click(object sender, RoutedEventArgs e)
        {
            var selected = ScenarioPresetBox.SelectedItem?.ToString() ?? string.Empty;
            SetAllTasksChecked(false);

            switch (selected)
            {
                case "Hızlı Günlük Temizlik":
                    TaskUserTemp.IsChecked = true;
                    TaskRecycleBin.IsChecked = true;
                    TaskDns.IsChecked = true;
                    ScenarioInfoText.Text = "Hızlı profil uygulandı.";
                    break;

                case "Derin Temizlik + Ağ Onarım":
                    TaskUserTemp.IsChecked = true;
                    TaskWindowsTemp.IsChecked = true;
                    TaskRecycleBin.IsChecked = true;
                    TaskDns.IsChecked = true;
                    TaskNetworkRefresh.IsChecked = true;
                    ScenarioInfoText.Text = "Derin profil uygulandı.";
                    break;

                case "Güncelleme Kurtarma":
                    TaskWindowsTemp.IsChecked = true;
                    TaskResetUpdate.IsChecked = true;
                    TaskDns.IsChecked = true;
                    ScenarioInfoText.Text = "Güncelleme kurtarma profili uygulandı.";
                    break;

                case "Sessiz Güvenli Profil":
                    TaskUserTemp.IsChecked = true;
                    TaskRecycleBin.IsChecked = true;
                    ScenarioInfoText.Text = "Sessiz profil uygulandı.";
                    break;

                case "Erişilebilirlik Odaklı Profil":
                    TaskUserTemp.IsChecked = true;
                    TaskDns.IsChecked = true;
                    ShowPanel("Accessibility");
                    ScenarioInfoText.Text = "Erişilebilirlik profili uygulandı.";
                    break;

                default:
                    SelectRecommended();
                    break;
            }

            AppendLog($"Senaryo uygulandı: {selected}");
        }

        private async void ScanLargeFiles_Click(object sender, RoutedEventArgs e)
        {
            var root = LargeFileScanPathBox.Text.Trim();
            if (!Directory.Exists(root))
            {
                SetStatus("Geçerli klasör seçin.");
                return;
            }

            if (!int.TryParse(LargeFileMinMbBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minMb) || minMb < 1)
            {
                SetStatus("Minimum MB değeri geçersiz.");
                return;
            }

            await RunSingleOperationAsync("Büyük dosya tarama", false, async token =>
            {
                var threshold = minMb * 1024L * 1024L;
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false,
                };

                var records = await Task.Run(() =>
                {
                    var result = new List<LargeFileRecord>();
                    foreach (var file in Directory.EnumerateFiles(root, "*", options))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var info = new FileInfo(file);
                            if (info.Length >= threshold)
                            {
                                result.Add(new LargeFileRecord { Path = info.FullName, SizeBytes = info.Length });
                            }
                        }
                        catch
                        {
                            // Hata veren dosyalar atlanır.
                        }
                    }

                    return result.OrderByDescending(r => r.SizeBytes).Take(250).ToList();
                }, token);

                _lastLargeFiles.Clear();
                _lastLargeFiles.AddRange(records);

                Dispatcher.Invoke(() =>
                {
                    LargeFilesList.Items.Clear();
                    foreach (var item in _lastLargeFiles)
                    {
                        LargeFilesList.Items.Add($"{item.SizeBytes / (1024d * 1024d * 1024d):F2} GB | {item.Path}");
                    }
                });
            });
        }

        private void ExportLargeFilesCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_lastLargeFiles.Count == 0)
            {
                SetStatus("Önce tarama yapın.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Büyük dosya listesini CSV kaydet",
                Filter = "CSV (*.csv)|*.csv|Tüm dosyalar (*.*)|*.*",
                FileName = $"buyuk-dosya-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Path,SizeBytes,SizeMB,SizeGB");
            foreach (var item in _lastLargeFiles)
            {
                var sizeMb = item.SizeBytes / (1024d * 1024d);
                var sizeGb = item.SizeBytes / (1024d * 1024d * 1024d);
                sb.AppendLine($"\"{item.Path.Replace("\"", "\"\"")}\",{item.SizeBytes},{sizeMb:F2},{sizeGb:F2}");
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            AppendLog($"CSV oluşturuldu: {dialog.FileName}");
        }

        private async void CreateZipBackup_Click(object sender, RoutedEventArgs e)
        {
            var source = BackupSourceBox.Text.Trim();
            var target = BackupTargetBox.Text.Trim();

            if (!Directory.Exists(source))
            {
                SetStatus("Yedek kaynak klasörü bulunamadı.");
                return;
            }

            await RunSingleOperationAsync("ZIP yedek", false, async token =>
            {
                Directory.CreateDirectory(target);
                var zipPath = Path.Combine(target, $"yedek-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
                await Task.Run(() => ZipFile.CreateFromDirectory(source, zipPath, CompressionLevel.Optimal, true), token);
                AppendLog($"ZIP yedek oluşturuldu: {zipPath}");
            });
        }

        private async void CreateRestorePoint_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync(
                "Geri yükleme noktası",
                true,
                token => RunAndLogAsync(
                    "powershell",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"Checkpoint-Computer -Description 'TemizlikV3' -RestorePointType MODIFY_SETTINGS\"",
                    token,
                    false));
        }

        private void AnalyzeReportHistory_Click(object sender, RoutedEventArgs e)
        {
            var summary = BuildHistorySummary();
            AnalyticsOutputBox.Text =
                $"Toplam rapor: {summary.TotalReports}{Environment.NewLine}" +
                $"Toplam görev kaydı: {summary.TotalTaskRecords}{Environment.NewLine}" +
                $"Başarılı görev: {summary.CompletedTasks}{Environment.NewLine}" +
                $"Hatalı görev: {summary.FailedTasks}{Environment.NewLine}" +
                $"Başarı oranı: {summary.SuccessRate:P1}{Environment.NewLine}" +
                $"Son rapor: {summary.LastReportDate:yyyy-MM-dd HH:mm:ss}";
        }

        private void WriteTrendRecommendations_Click(object sender, RoutedEventArgs e)
        {
            var summary = BuildHistorySummary();
            var tips = new List<string>();

            if (summary.TotalReports < 3)
            {
                tips.Add("Daha güçlü trend analizi için en az 3 rapor biriktirin.");
            }

            if (summary.SuccessRate < 0.85)
            {
                tips.Add("Başarı oranı düşük. Ön Uçuş Kontrolü ve Sessiz Profil önerilir.");
            }
            else
            {
                tips.Add("Başarı oranı iyi. Haftalık derin temizlik planı önerilir.");
            }

            tips.Add("Büyük Dosya Radar taramasını ayda bir çalıştırın.");
            tips.Add("Bu v3 sürümü Olcay Aşçı anısına sürdürülebilir bakım yaklaşımı taşır.");

            AnalyticsOutputBox.Text += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, tips.Select(t => $"- {t}"));
        }

        private ReportHistorySummary BuildHistorySummary()
        {
            EnsureDirectories();
            var summary = new ReportHistorySummary();
            var files = Directory.GetFiles(_reportsDirectory, "*.json", SearchOption.TopDirectoryOnly);
            summary.TotalReports = files.Length;

            foreach (var file in files)
            {
                try
                {
                    var text = File.ReadAllText(file, Encoding.UTF8);
                    var report = JsonSerializer.Deserialize<ApplicationReport>(text);
                    if (report == null)
                    {
                        continue;
                    }

                    summary.TotalTaskRecords += report.ExecutionHistory.Count;
                    summary.CompletedTasks += report.ExecutionHistory.Count(r => r.Status == "Completed");
                    summary.FailedTasks += report.ExecutionHistory.Count(r => r.Status == "Failed");
                    if (report.GeneratedAt > summary.LastReportDate)
                    {
                        summary.LastReportDate = report.GeneratedAt;
                    }
                }
                catch
                {
                    // Bozuk JSON raporları atlanır.
                }
            }

            if (summary.TotalTaskRecords > 0)
            {
                summary.SuccessRate = (double)summary.CompletedTasks / summary.TotalTaskRecords;
            }

            return summary;
        }

        private async void RunNetworkDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            var host = string.IsNullOrWhiteSpace(NetworkTestHostBox.Text) ? "1.1.1.1" : NetworkTestHostBox.Text.Trim();
            await RunSingleOperationAsync("Ağ tanılama", false, async token =>
            {
                await RunAndLogAsync("ping", $"-n 4 {host}", token, false);
                await RunAndLogAsync("nslookup", host, token, false);
            });
        }

        private async void CleanBrowserCaches_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("Tarayıcı önbellek temizliği", false, token => CleanBrowserCachesAsync(token));
        }

        private async void AnalyzeFolderSizes_Click(object sender, RoutedEventArgs e)
        {
            var root = FolderSizeRootBox.Text.Trim();
            if (!Directory.Exists(root))
            {
                SetStatus("Klasör analiz kökü geçersiz.");
                return;
            }

            await RunSingleOperationAsync("Klasör boyut analizi", false, async token =>
            {
                var results = await Task.Run(() =>
                {
                    var list = new List<(string Path, long Size)>();
                    foreach (var dir in Directory.GetDirectories(root))
                    {
                        token.ThrowIfCancellationRequested();
                        list.Add((dir, GetDirectorySizeSafe(dir)));
                    }

                    return list.OrderByDescending(i => i.Size).Take(120).ToList();
                }, token);

                Dispatcher.Invoke(() =>
                {
                    FolderSizeList.Items.Clear();
                    foreach (var item in results)
                    {
                        FolderSizeList.Items.Add($"{item.Size / (1024d * 1024d):F1} MB | {item.Path}");
                    }
                });
            });
        }

        private static long GetDirectorySizeSafe(string dir)
        {
            try
            {
                var size = 0L;
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false,
                };

                foreach (var file in Directory.EnumerateFiles(dir, "*", options))
                {
                    try
                    {
                        size += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Inaccessible file ignored.
                    }
                }

                return size;
            }
            catch
            {
                return 0L;
            }
        }

        private void ExportMaintenanceProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsureDirectories();
                var profile = new MaintenanceProfile
                {
                    Theme = _themeMode.ToString(),
                    FontScale = FontScaleSlider.Value,
                    Tasks = GetTaskSelectionDictionary(),
                };

                var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_profilePath, json, Encoding.UTF8);
                AppendLog($"Profil dışa aktarıldı: {_profilePath}");
            }
            catch (Exception ex)
            {
                AppendLog($"Profil dışa aktarma hatası: {ex.Message}");
            }
        }

        private void ImportMaintenanceProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(_profilePath))
                {
                    SetStatus("İçe aktarılacak profil bulunamadı.");
                    return;
                }

                var json = File.ReadAllText(_profilePath, Encoding.UTF8);
                var profile = JsonSerializer.Deserialize<MaintenanceProfile>(json);
                if (profile == null)
                {
                    return;
                }

                ApplyTaskSelectionDictionary(profile.Tasks);
                FontScaleSlider.Value = profile.FontScale;
                FontSize = profile.FontScale;

                if (Enum.TryParse<ThemeMode>(profile.Theme, out var mode))
                {
                    ApplyTheme(mode);
                }

                AppendLog("Profil içe aktarıldı.");
            }
            catch (Exception ex)
            {
                AppendLog($"Profil içe aktarma hatası: {ex.Message}");
            }
        }

        private async void GenerateStartupAppsReport_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("Başlangıç uygulamaları raporu", false, _ =>
            {
                var lines = new List<string>
                {
                    "Başlangıç uygulamaları raporu",
                    $"Tarih: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    string.Empty,
                };

                var currentUserEntries = ReadRunRegistryEntries(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run");
                var localMachineEntries = ReadRunRegistryEntries(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run");

                lines.Add("HKCU Run kayıtları:");
                lines.AddRange(currentUserEntries.Count == 0 ? new[] { "- Kayıt yok" } : currentUserEntries.Select(s => $"- {s}"));
                lines.Add(string.Empty);

                lines.Add("HKLM Run kayıtları:");
                lines.AddRange(localMachineEntries.Count == 0 ? new[] { "- Kayıt yok" } : localMachineEntries.Select(s => $"- {s}"));
                lines.Add(string.Empty);

                var startupUser = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var startupCommon = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
                lines.Add($"Kullanıcı başlangıç klasörü: {startupUser}");
                lines.Add($"Ortak başlangıç klasörü: {startupCommon}");

                SetFeatureHubOutput("Başlangıç uygulamaları", lines);
                return Task.CompletedTask;
            });
        }

        private async void CaptureRamProcessSnapshot_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("RAM ve süreç anlık görünümü", false, _ =>
            {
                var lines = new List<string>
                {
                    "RAM ve süreç anlık görünümü",
                    $"Tarih: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    string.Empty,
                    "En yüksek bellek kullanan 15 süreç:",
                };

                foreach (var process in Process.GetProcesses().OrderByDescending(p => p.WorkingSet64).Take(15))
                {
                    try
                    {
                        lines.Add($"- {process.ProcessName} | PID={process.Id} | RAM={process.WorkingSet64 / (1024d * 1024d):F1} MB");
                    }
                    catch
                    {
                        // Bazı süreç bilgileri erişim kısıtı nedeniyle okunamayabilir.
                    }
                }

                SetFeatureHubOutput("RAM görünümü", lines);
                return Task.CompletedTask;
            });
        }

        private async void GenerateDiskHealthSummary_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("Disk sağlık özeti", false, _ =>
            {
                var lines = new List<string>
                {
                    "Disk sağlık özeti",
                    $"Tarih: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    string.Empty,
                };

                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    var freeRatio = (double)drive.AvailableFreeSpace / drive.TotalSize;
                    var status = freeRatio switch
                    {
                        < 0.10 => "Kritik",
                        < 0.20 => "Dikkat",
                        < 0.30 => "Orta",
                        _ => "İyi",
                    };

                    lines.Add($"- {drive.Name} | Dosya Sistemi: {drive.DriveFormat} | Boş Alan: {drive.AvailableFreeSpace / (1024d * 1024d * 1024d):F1} GB | Durum: {status}");
                }

                SetFeatureHubOutput("Disk özeti", lines);
                return Task.CompletedTask;
            });
        }

        private async void AnalyzeTempFootprint_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("Temp boyut analizi", false, _ =>
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var tempDirs = new[]
                {
                    Path.GetTempPath(),
                    Path.Combine(local, "Temp"),
                    Path.Combine(local, "Microsoft", "Windows", "INetCache"),
                };

                var lines = new List<string>
                {
                    "Temp boyut analizi",
                    $"Tarih: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    string.Empty,
                };

                foreach (var dir in tempDirs)
                {
                    if (!Directory.Exists(dir))
                    {
                        lines.Add($"- {dir} | Bulunamadı");
                        continue;
                    }

                    var size = GetDirectorySizeSafe(dir);
                    lines.Add($"- {dir} | Boyut: {size / (1024d * 1024d):F1} MB");
                }

                SetFeatureHubOutput("Temp analizi", lines);
                return Task.CompletedTask;
            });
        }

        private async void RunMultiDnsLatencyTest_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("DNS çoklu gecikme testi", false, async token =>
            {
                var hosts = new[] { "1.1.1.1", "8.8.8.8", "9.9.9.9" };
                var lines = new List<string>
                {
                    "DNS çoklu gecikme testi",
                    $"Tarih: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    string.Empty,
                };

                foreach (var host in hosts)
                {
                    var result = await RunCommandAsync("ping", $"-n 3 {host}", token);
                    var summaryLine = result.OutputLines.LastOrDefault(l => l.Contains("Average", StringComparison.OrdinalIgnoreCase) || l.Contains("Ortalama", StringComparison.OrdinalIgnoreCase));
                    lines.Add(summaryLine == null
                        ? $"- {host}: Özet satırı bulunamadı"
                        : $"- {host}: {summaryLine}");
                }

                SetFeatureHubOutput("DNS gecikme testi", lines);
            });
        }

        private async void BackupHostsFile_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("Hosts dosyası yedekleme", false, _ =>
            {
                EnsureDirectories();
                var hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "etc", "hosts");
                if (!File.Exists(hostsPath))
                {
                    throw new FileNotFoundException("Hosts dosyası bulunamadı.", hostsPath);
                }

                var backupPath = Path.Combine(_reportsDirectory, $"hosts-yedek-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                File.Copy(hostsPath, backupPath, true);
                SetFeatureHubOutput("Hosts yedeği", new[]
                {
                    "Hosts dosyası başarıyla yedeklendi.",
                    $"Kaynak: {hostsPath}",
                    $"Hedef: {backupPath}",
                });
                return Task.CompletedTask;
            });
        }

        private async void GenerateLast24hErrorSummary_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("Son 24 saat hata özeti", false, async token =>
            {
                var query = "qe System /q:\"*[System[(Level=2) and TimeCreated[timediff(@SystemTime)<=86400000]]]\" /f:text /c:40";
                var result = await RunCommandAsync("wevtutil", query, token);
                var lines = result.OutputLines.Take(180).ToList();
                if (lines.Count == 0)
                {
                    lines.Add("Son 24 saatte kritik hata kaydı bulunamadı veya erişilemedi.");
                }

                SetFeatureHubOutput("Son 24 saat hata özeti", lines);
            });
        }

        private async void AnalyzeStartupImpact_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("Başlangıç etki analizi", false, _ =>
            {
                var userEntries = ReadRunRegistryEntries(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run");
                var machineEntries = ReadRunRegistryEntries(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run");
                var startupFiles = 0;

                var startupFolders = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                };

                foreach (var folder in startupFolders.Where(Directory.Exists))
                {
                    startupFiles += Directory.GetFiles(folder).Length;
                }

                var total = userEntries.Count + machineEntries.Count + startupFiles;
                var risk = total switch
                {
                    > 20 => "Yüksek etki",
                    > 10 => "Orta etki",
                    _ => "Düşük etki",
                };

                SetFeatureHubOutput("Başlangıç etki analizi", new[]
                {
                    $"Kullanıcı Run kaydı: {userEntries.Count}",
                    $"Sistem Run kaydı: {machineEntries.Count}",
                    $"Başlangıç klasörü öğesi: {startupFiles}",
                    $"Toplam başlangıç yükü: {total}",
                    $"Genel değerlendirme: {risk}",
                });
                return Task.CompletedTask;
            });
        }

        private async void CopyMaintenanceDigest_Click(object sender, RoutedEventArgs e)
        {
            await RunSingleOperationAsync("Bakım özetini panoya kopyalama", false, _ =>
            {
                var digest = new StringBuilder();
                digest.AppendLine("Temizlik ve Bakım Merkezi Professional - Bakım Özeti");
                digest.AppendLine($"Tarih: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                digest.AppendLine($"Tema: {GetThemeDisplayName()}");
                digest.AppendLine($"Seçili görev sayısı: {GetSelectedTasks().Count}");
                digest.AppendLine($"Toplam görev geçmişi: {_executionHistory.Count}");
                digest.AppendLine($"Son durum: {StatusText.Text}");
                digest.AppendLine($"İthaf: {_tributeConfig.PersonName}");

                Clipboard.SetText(digest.ToString());
                SetFeatureHubOutput("Panoya kopyalandı", new[]
                {
                    "Bakım özeti panoya kopyalandı.",
                    "Dış uygulamalara yapıştırabilirsiniz.",
                });
                return Task.CompletedTask;
            });
        }

        private List<string> ReadRunRegistryEntries(RegistryKey root, string path)
        {
            var entries = new List<string>();
            try
            {
                using var key = root.OpenSubKey(path);
                if (key == null)
                {
                    return entries;
                }

                foreach (var name in key.GetValueNames())
                {
                    var value = key.GetValue(name)?.ToString() ?? string.Empty;
                    entries.Add($"{name} => {value}");
                }
            }
            catch (Exception ex)
            {
                entries.Add($"Kayıt okunamadı: {ex.Message}");
            }

            return entries;
        }

        private string GetThemeDisplayName()
        {
            return _themeMode == ThemeMode.Dark ? "Karanlık" : "Aydınlık";
        }

        private string GetApplicationVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null ? "3.1.1" : $"{version.Major}.{version.Minor}.{version.Build}";
        }

        private void SetFeatureHubOutput(string title, IEnumerable<string> lines)
        {
            var content = new StringBuilder();
            content.AppendLine(title);
            content.AppendLine(new string('-', 58));
            foreach (var line in lines)
            {
                content.AppendLine(line);
            }

            FeatureHubOutputBox.Text = content.ToString();
            AppendLog($"Ek özellik çıktısı güncellendi: {title}");
        }

        private Dictionary<string, bool> GetTaskSelectionDictionary()
        {
            return new Dictionary<string, bool>
            {
                [nameof(TaskUserTemp)] = TaskUserTemp.IsChecked == true,
                [nameof(TaskWindowsTemp)] = TaskWindowsTemp.IsChecked == true,
                [nameof(TaskRecycleBin)] = TaskRecycleBin.IsChecked == true,
                [nameof(TaskDns)] = TaskDns.IsChecked == true,
                [nameof(TaskNetworkRefresh)] = TaskNetworkRefresh.IsChecked == true,
                [nameof(TaskResetUpdate)] = TaskResetUpdate.IsChecked == true,
                [nameof(TaskBrowserCache)] = TaskBrowserCache.IsChecked == true,
            };
        }

        private void ApplyTaskSelectionDictionary(Dictionary<string, bool> dict)
        {
            bool TryGet(string key) => dict.TryGetValue(key, out var value) && value;
            TaskUserTemp.IsChecked = TryGet(nameof(TaskUserTemp));
            TaskWindowsTemp.IsChecked = TryGet(nameof(TaskWindowsTemp));
            TaskRecycleBin.IsChecked = TryGet(nameof(TaskRecycleBin));
            TaskDns.IsChecked = TryGet(nameof(TaskDns));
            TaskNetworkRefresh.IsChecked = TryGet(nameof(TaskNetworkRefresh));
            TaskResetUpdate.IsChecked = TryGet(nameof(TaskResetUpdate));
            TaskBrowserCache.IsChecked = TryGet(nameof(TaskBrowserCache));
        }

        private async Task RunSingleOperationAsync(string name, bool requiresAdmin, Func<CancellationToken, Task> action)
        {
            if (_cancellationTokenSource != null)
            {
                SetStatus("Önce mevcut işlemi tamamlayın.");
                return;
            }

            if (requiresAdmin && !IsAdministrator())
            {
                var answer = MessageBox.Show(this, "Bu işlem yönetici yetkisi gerektiriyor. Yönetici moduna geçilsin mi?", "Yönetici Yetkisi", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer == MessageBoxResult.Yes)
                {
                    RestartAsAdministrator();
                }

                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;
            SetUiRunningState(true);
            ProgressBar.Value = 0;

            try
            {
                SetStatus($"Çalışıyor: {name}");
                await action(token);
                ProgressBar.Value = 100;
                SetStatus($"Tamamlandı: {name}");
            }
            catch (OperationCanceledException)
            {
                SetStatus($"İptal edildi: {name}");
            }
            catch (Exception ex)
            {
                AppendLog($"Hata ({name}): {ex.Message}");
                SetStatus($"Başarısız: {name}");
            }
            finally
            {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
                SetUiRunningState(false);
            }
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(_dataDirectory);
            Directory.CreateDirectory(_reportsDirectory);
        }

        private static string SanitizeFileName(string input)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                input = input.Replace(c, '_');
            }

            return input;
        }

        private void OpenPath(string path)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = path,
                UseShellExecute = true,
            });
        }

        private void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }

        private void StartRecommendedFromDashboard_Click(object sender, RoutedEventArgs e)
        {
            SelectRecommended();
            ShowPanel("Clean");
            _ = RunSelectedTasksAsync();
        }

        private void ToggleHighContrastQuick_Click(object sender, RoutedEventArgs e)
        {
            HighContrastModeCheck.IsChecked = true;
            ScreenReaderAnnouncementsCheck.IsChecked = true;
            _screenReaderAnnouncementsEnabled = true;
            ApplyTheme(ThemeMode.Dark);
            ShowPanel("Accessibility");
            SetStatus("Yüksek kontrast görünümü açıldı.");
        }

        private async void CheckUpdatesNow_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(userInitiated: true);
        }

        private async Task CheckForUpdatesAsync(bool userInitiated)
        {
            if (_isUpdateCheckInProgress)
            {
                if (userInitiated)
                {
                    MessageBox.Show(this, "Güncelleme denetimi zaten çalışıyor.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return;
            }

            _isUpdateCheckInProgress = true;
            _lastUpdateSummary = "Güncelleme denetleniyor...";
            UpdateDashboardSummary();

            try
            {
                var versionText = GetApplicationVersion();
                if (!Version.TryParse(versionText, out var currentVersion))
                {
                    currentVersion = new Version(3, 1, 1);
                }

                var result = await GitHubUpdateService.CheckLatestReleaseAsync(
                    UpdateRepoOwner,
                    UpdateRepoName,
                    currentVersion,
                    CancellationToken.None);

                if (!result.IsUpdateAvailable)
                {
                    _lastUpdateSummary = $"Güncel sürüm kullanılıyor ({result.CurrentVersion}).";
                    AppendLog(_lastUpdateSummary);
                    if (userInitiated)
                    {
                        MessageBox.Show(this, _lastUpdateSummary, "Güncelleme Denetimi", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    return;
                }

                _lastUpdateSummary = $"Yeni sürüm bulundu: {result.LatestVersion}";
                AppendLog($"Güncelleme bulundu. Mevcut: {result.CurrentVersion}, Yeni: {result.LatestVersion}");

                var notesPreview = string.IsNullOrWhiteSpace(result.ReleaseNotes)
                    ? "Sürüm notu bulunamadı."
                    : result.ReleaseNotes.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).FirstOrDefault() ?? "Sürüm notu bulunamadı.";

                var message =
                    $"Yeni sürüm hazır.\r\n\r\n" +
                    $"Mevcut sürüm: {result.CurrentVersion}\r\n" +
                    $"Yeni sürüm: {result.LatestVersion}\r\n" +
                    $"Başlık: {result.ReleaseTitle}\r\n\r\n" +
                    $"Özet: {notesPreview}\r\n\r\n" +
                    "Güncellemeyi şimdi otomatik indirip yüklemek ister misiniz?";

                var answer = MessageBox.Show(this, message, "Güncelleme Mevcut", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Yes)
                {
                    var started = await TryStartAutomaticUpdateAsync(result);
                    if (!started)
                    {
                        var fallback = MessageBox.Show(
                            this,
                            "Otomatik güncelleme başlatılamadı. İndirme sayfası açılsın mı?",
                            "Otomatik Güncelleme",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);
                        if (fallback == MessageBoxResult.Yes)
                        {
                            OpenUrl(result.DownloadUrl);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _lastUpdateSummary = "Güncelleme denetimi başarısız.";
                AppendLog($"Güncelleme denetimi hatası: {ex.Message}");
                if (userInitiated)
                {
                    MessageBox.Show(this, $"Güncelleme denetlenemedi: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally
            {
                _isUpdateCheckInProgress = false;
                UpdateDashboardSummary();
            }
        }

        private async Task<bool> TryStartAutomaticUpdateAsync(UpdateCheckResult result)
        {
            if (string.IsNullOrWhiteSpace(result.DownloadUrl) ||
                !Uri.TryCreate(result.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
                !downloadUri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                AppendLog("Otomatik güncelleme için doğrudan kurulum dosyası bulunamadı.");
                return false;
            }

            try
            {
                SetStatus("Güncelleme paketi indiriliyor...");

                var updateTempDirectory = Path.Combine(Path.GetTempPath(), "TemizlikBakimMerkeziProfessional", "Updater");
                Directory.CreateDirectory(updateTempDirectory);

                var setupFileName = $"TemizlikBakimMerkezi-Professional-v{result.LatestVersion.ToString().Replace('.', '_')}-Setup.exe";
                var setupPath = Path.Combine(updateTempDirectory, setupFileName);

                using (var response = await UpdateDownloadClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None))
                {
                    response.EnsureSuccessStatusCode();
                    await using var sourceStream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
                    await using var targetStream = new FileStream(setupPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await sourceStream.CopyToAsync(targetStream);
                }

                AppendLog($"Güncelleme paketi indirildi: {setupPath}");
                SetStatus("Kurulum başlatılıyor...");

                var currentExePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(currentExePath))
                {
                    currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(currentExePath))
                {
                    throw new InvalidOperationException("Çalışan uygulama yolu belirlenemedi.");
                }

                var updaterScriptPath = Path.Combine(updateTempDirectory, $"run-update-{Guid.NewGuid():N}.cmd");
                var scriptLines = new[]
                {
                    "@echo off",
                    "setlocal",
                    $"set \"SETUP={setupPath}\"",
                    $"set \"APP={currentExePath}\"",
                    "timeout /t 2 /nobreak >nul",
                    "start \"\" /wait \"%SETUP%\" /SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS",
                    "if %errorlevel% neq 0 goto end",
                    "if exist \"%APP%\" start \"\" \"%APP%\"",
                    ":end",
                    "del \"%SETUP%\" >nul 2>&1",
                    "del \"%~f0\" >nul 2>&1",
                };

                File.WriteAllLines(updaterScriptPath, scriptLines, new UTF8Encoding(false));

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{updaterScriptPath}\"\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = updateTempDirectory,
                });

                MessageBox.Show(
                    this,
                    "Güncelleme indirildi. Kurulum başlatıldı ve uygulama kapanacak. Kurulum tamamlandığında uygulama otomatik yeniden açılır.",
                    "Otomatik Güncelleme",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                AppendLog("Otomatik güncelleme başlatıldı.");
                Application.Current.Shutdown();
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"Otomatik güncelleme başlatılamadı: {ex.Message}");
                SetStatus("Otomatik güncelleme başlatılamadı.");
                return false;
            }
        }

        private static HttpClient CreateUpdateDownloadClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "TemizlikBakimMerkeziProfessional-Updater/3.1.2");
            return client;
        }

        private void UpdateDashboardSummary()
        {
            if (!IsLoaded)
            {
                return;
            }

            DashboardThemeText.Text = $"Tema: {GetThemeDisplayName()}";
            DashboardScheduleStateText.Text = $"Zamanlama: {ScheduleStatusText.Text}";
            DashboardLastStatusText.Text = $"Son durum: {StatusText.Text}";
            DashboardUpdateStateText.Text = $"Güncelleme: {_lastUpdateSummary}";
            DashboardLastReportText.Text = LastReportPathText.Text;
        }

        private void AppendLog(string message)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText(line);
                LogBox.ScrollToEnd();
            });
        }

        private void SetStatus(string message)
        {
            _statusSequence++;
            StatusText.Text = message;
            ActionFeedbackText.Text = $"[{DateTime.Now:HH:mm:ss}] {message} (#{_statusSequence})";
            RaiseLiveRegionChanged(StatusText);
            RaiseLiveRegionChanged(ActionFeedbackText);
            UpdateDashboardSummary();
        }

        private static string GetPanelDisplayName(string key)
        {
            return key switch
            {
                "Dashboard" => "Ana Sayfa",
                "Clean" => "Temizlik",
                "System" => "Sistem",
                "Reports" => "Raporlar",
                "Accessibility" => "Erişilebilirlik",
                "Tribute" => "İthaf",
                "Pro" => "V3 Özellikler",
                _ => "Ana Sayfa",
            };
        }

        private void RaiseLiveRegionChanged(FrameworkElement element)
        {
            if (!_screenReaderAnnouncementsEnabled)
            {
                return;
            }

            var peer = FrameworkElementAutomationPeer.FromElement(element)
                ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        private sealed class CleanupTask
        {
            public CleanupTask(string name, bool requiresAdmin, Func<CancellationToken, Task> action)
            {
                Name = name;
                RequiresAdmin = requiresAdmin;
                Action = action;
            }

            public string Name { get; }

            public bool RequiresAdmin { get; }

            public Func<CancellationToken, Task> Action { get; }
        }

        private sealed class CommandResult
        {
            public CommandResult(int exitCode, List<string> outputLines)
            {
                ExitCode = exitCode;
                OutputLines = outputLines;
            }

            public int ExitCode { get; }

            public List<string> OutputLines { get; }
        }

        private sealed class TaskExecutionRecord
        {
            public string TaskName { get; set; } = string.Empty;

            public DateTime StartedAt { get; set; }

            public DateTime FinishedAt { get; set; }

            public string Status { get; set; } = string.Empty;

            public string Detail { get; set; } = string.Empty;
        }

        private sealed class LargeFileRecord
        {
            public string Path { get; set; } = string.Empty;

            public long SizeBytes { get; set; }
        }

        private sealed class TributeConfig
        {
            public string PersonName { get; set; } = string.Empty;

            public string LegacyAppName { get; set; } = string.Empty;

            public string TributeMessage { get; set; } = string.Empty;

            public string LongNarrative { get; set; } = string.Empty;
        }

        private sealed class ApplicationReport
        {
            public string Title { get; set; } = string.Empty;

            public DateTime GeneratedAt { get; set; }

            public TributeConfig Tribute { get; set; } = new();

            public List<string> SelectedTasks { get; set; } = new();

            public List<TaskExecutionRecord> ExecutionHistory { get; set; } = new();

            public string LogText { get; set; } = string.Empty;

            public string? SystemSnapshot { get; set; }
        }

        private sealed class ReportHistorySummary
        {
            public int TotalReports { get; set; }

            public int TotalTaskRecords { get; set; }

            public int CompletedTasks { get; set; }

            public int FailedTasks { get; set; }

            public double SuccessRate { get; set; }

            public DateTime LastReportDate { get; set; }
        }

        private sealed class MaintenanceProfile
        {
            public string Theme { get; set; } = ThemeMode.Light.ToString();

            public double FontScale { get; set; } = 18;

            public Dictionary<string, bool> Tasks { get; set; } = new();
        }

        private enum ThemeMode
        {
            Light,
            Dark,
        }
    }
}

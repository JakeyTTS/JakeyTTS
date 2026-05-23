using JakeyTTS.Core;
using JakeyTTS.Views;
using JakeyTTS.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using JakeyTTS.Plugins;
using JakeyTTS.UserActions;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using WinRT.Interop;

namespace JakeyTTS
{
    public sealed partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }

        private const int WM_HOTKEY = 0x0312;
        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);

        [DllImport("Comctl32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc callback, uint uIdSubclass, IntPtr dwRefData);

        [DllImport("Comctl32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        private SubclassProc _subclassProcDelegate;

        // Memory cache directory to match injected plugin identifiers to their web interface target URLs
        private readonly Dictionary<string, string> _dynamicPluginUrls = new Dictionary<string, string>();

        public MainWindow()
        {
            Instance = this;
            this.InitializeComponent();

            this.Title = "JakeyTTS";
            SystemBackdrop = new MicaBackdrop();
            ExtendsContentIntoTitleBar = true;

            SetTitleBar(AppTitleBar);

            SetAppIcon();
            ApplySavedTheme();

            // Hotkey Initialization
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            _subclassProcDelegate = new SubclassProc(WindowSubclassCallback);
            SetWindowSubclass(hWnd, _subclassProcDelegate, 0, IntPtr.Zero);

            HotkeyService.Instance.Setup(hWnd);

            // Trigger safe environment teardown routines on application lifecycle closure
            this.Closed += MainWindow_Closed;

            NavView.Header = null;
            ContentFrame.Navigate(typeof(HomePage));
        }

        private IntPtr WindowSubclassCallback(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                HotkeyService.Instance.ProcessHotkey(hotkeyId);
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private void SetAppIcon()
        {
            try
            {
                IntPtr hWnd = WindowNative.GetWindowHandle(this);
                WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
                string iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");

                if (File.Exists(iconPath))
                {
                    appWindow.SetIcon(iconPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set icon: {ex.Message}");
            }
        }

        private void ApplySavedTheme()
        {
            try
            {
                var config = TwitchService.Instance.Config;
                if (config != null && this.Content is FrameworkElement rootElement)
                {
                    rootElement.RequestedTheme = (ElementTheme)config.SelectedTheme;
                }
            }
            catch { }
        }

        // Clean teardown routine to prevent zombie background processes from consuming host resources
        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            try
            {
                // Terminate the network pipeline listener and forcefully kill all child background execution bin threads
                PluginServer.Instance.Stop();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during plugin server shutdown: {ex.Message}");
            }
        }

        #region Navigation Logic

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                sender.Header = "Settings";
                ContentFrame.Navigate(typeof(SettingsPage));
                return;
            }

            var item = args.InvokedItemContainer as NavigationViewItem;
            if (item?.Tag == null) return;

            string tag = item.Tag.ToString();
            sender.Header = (tag == "Home" || tag == "Melodies" || tag == "MixedVoices" || tag == "UserActions" || tag == "Plugins" || tag == "Keybinds" || tag == "Commands" || tag == "Rewards" || tag == "SFX" || tag == "Dictionary") ? null : item.Content;

            switch (tag)
            {
                case "Home":
                    ContentFrame.Navigate(typeof(HomePage));
                    break;
                case "TTSConfig":
                    ContentFrame.Navigate(typeof(TTSConfigPage));
                    break;
                case "UserActions":
                    ContentFrame.Navigate(typeof(UserActionsPage));
                    break;
                case "Commands":
                    ContentFrame.Navigate(typeof(CommandsPage));
                    break;
                case "Rewards":
                    ContentFrame.Navigate(typeof(RedeemPage));
                    break;
                case "History":
                    ContentFrame.Navigate(typeof(HistoryPage));
                    break;
                case "Keybinds":
                    ContentFrame.Navigate(typeof(KeybindsPage));
                    break;
                case "Melodies":
                    ContentFrame.Navigate(typeof(MelodyPage));
                    break;
                case "SFX":
                    ContentFrame.Navigate(typeof(SoundEffectsPage));
                    break;
                case "MixedVoices":
                    ContentFrame.Navigate(typeof(JakeyTTS.MixVoices.MixedVoicesPage));
                    break;
                case "Dictionary":
                    ContentFrame.Navigate(typeof(DictionaryPage));
                    break;
                case "Plugins":
                    ContentFrame.Navigate(typeof(PluginsPage));
                    break;
                default:
                    // Dynamic navigation fallthrough handler designed for customized views registered by external plugins
                    if (_dynamicPluginUrls.TryGetValue(tag, out string embedUrl))
                    {
                        ContentFrame.Navigate(typeof(PluginWebPage), embedUrl);
                    }
                    break;
            }
        }

        public void NotifyDynamicUiRegistered(string pluginId, string pluginName, string embedUrl)
        {
            // Verify structural overlap constraints to eliminate layout duplication anomalies within menu options
            var existingItem = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == pluginId);

            if (existingItem == null)
            {
                // Map the targeted connection string location coordinates to the key context profile properties
                _dynamicPluginUrls[pluginId] = embedUrl;

                // Instantiate a new navigation option item block container directly inside the system dashboard viewport frame
                var newItem = new NavigationViewItem
                {
                    Content = pluginName,
                    Tag = pluginId,
                    Icon = new SymbolIcon(Symbol.Globe)
                };

                NavView.MenuItems.Add(newItem);
                Log($"🌐 Dynamic UI Page registered for plugin: {pluginName}");
            }
        }

        #endregion

        #region Log

        public void Log(string message)
        {
            if (this.DispatcherQueue == null) return;

            this.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (LogBlock != null)
                    {
                        LogBlock.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\r\n";
                        LogScroll?.ChangeView(0, LogScroll.ScrollableHeight, 1);
                    }
                }
                catch { }
            });
        }

        private void LogToggle_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (LogRow.Height.Value > 0)
            {
                // Collapse
                LogRow.Height = new GridLength(0);
                LogBorder.Visibility = Visibility.Collapsed;
                LogToggleBtn.Content = "Show Log";

                FontIcon showIcon = new FontIcon();
                showIcon.FontFamily = new FontFamily("Segoe Fluent Icons");
                showIcon.Glyph = "\uEBE8";
                LogToggleBtn.Icon = showIcon;
            }
            else
            {
                // Expand
                LogRow.Height = new GridLength(150);
                LogBorder.Visibility = Visibility.Visible;
                LogToggleBtn.Content = "Hide Log";
                LogToggleBtn.Icon = new SymbolIcon(Symbol.List);
            }
        }
        #endregion
    }

    #region Helper Dynamic Page Class
    /// <summary>
    /// Isolated page controller that builds out an adaptive WebView2 container framework 
    /// mapped inside standard WinUI 3 workspace constraints.
    /// </summary>
    public class PluginWebPage : Page
    {
        private readonly WebView2 _webView;

        public PluginWebPage()
        {
            _webView = new WebView2();
            this.Content = _webView;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is string url && !string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    _webView.Source = new Uri(url);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load plugin URL: {ex.Message}");
                }
            }
        }
    }
    #endregion
}

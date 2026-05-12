using System;
using System.IO;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;
using JakeyTTS.UserActions;

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
            sender.Header = (tag == "Home" || tag == "Melodies" || tag == "MixedVoices" || tag=="UserActions") ? null : item.Content;

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
                LogRow.Height = new GridLength(0);
                LogBorder.Visibility = Visibility.Collapsed;
                LogToggleBtn.Content = "Show Log";
                LogToggleBtn.Icon = new SymbolIcon(Symbol.Memo);
            }
            else
            {
                LogRow.Height = new GridLength(150);
                LogBorder.Visibility = Visibility.Visible;
                LogToggleBtn.Content = "Hide Log";
                LogToggleBtn.Icon = new SymbolIcon(Symbol.List);
            }
        }
        #endregion
    }
}
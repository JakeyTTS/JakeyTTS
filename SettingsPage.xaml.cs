using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace JakeyTTS
{
    public sealed partial class SettingsPage : Page
    {
        private readonly TwitchService _service = TwitchService.Instance;

        public SettingsPage()
        {
            this.InitializeComponent();
            ThemeSelector.SelectedIndex = _service.Config.SelectedTheme;
        }

        private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeSelector.SelectedIndex != -1)
            {
                int themeIndex = ThemeSelector.SelectedIndex;
                _service.Config.SelectedTheme = themeIndex;
                _service.Config.Save();

                if (MainWindow.Instance.Content is FrameworkElement rootElement)
                {
                    rootElement.RequestedTheme = (ElementTheme)themeIndex;
                }
            }
        }

        private async void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            // Requirement for Dialogs in WinUI 3
            ConfirmResetDialog.XamlRoot = this.XamlRoot;

            var result = await ConfirmResetDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                PerformFullReset();
            }
        }

        private void PerformFullReset()
        {
            try
            {
                // 1. Disconnect the service if it's currently running
                _ = _service.Disconnect();

                // 2. Wipe the config state in memory
                _service.Config.ResetToDefaults();

                // 3. Save the wiped state back to config.json
                _service.Config.Save();

                MainWindow.Instance?.Log("⚠️ Factory Reset Complete. Default commands loaded.");

                // 4. Update the Theme Radio Buttons in case it was changed
                ThemeSelector.SelectedIndex = 0;

                // 5. Navigate back to HomePage to force a UI refresh (e.g., Auth buttons)
                this.Frame.Navigate(typeof(HomePage));
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.Log($"❌ Reset failed: {ex.Message}");
            }
        }
    }
}
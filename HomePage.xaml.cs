using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace JakeyTTS
{
    public sealed partial class HomePage : Page
    {
        private readonly TwitchService _service = TwitchService.Instance;

        public HomePage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            UpdateUIState();
        }

        private void UpdateUIState()
        {
            bool isConfigured = !string.IsNullOrEmpty(_service.Config.Token);
            bool isRunning = _service.IsConnected;

            // Reset visibilities
            ConfigureBtn.Visibility = Visibility.Collapsed;
            StartBtn.Visibility = Visibility.Collapsed;
            StopBtn.Visibility = Visibility.Collapsed;

            if (!isConfigured)
            {
                ConfigureBtn.Visibility = Visibility.Visible;
            }
            else
            {
                if (isRunning) StopBtn.Visibility = Visibility.Visible;
                else StartBtn.Visibility = Visibility.Visible;
            }
        }

        private void ConfigureBtn_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to the new settings page
            this.Frame.Navigate(typeof(TTSConfigPage));
        }

        private async void ServiceToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_service.IsConnected) await _service.Disconnect();
            else await _service.Connect();

            UpdateUIState();
        }
    }
}
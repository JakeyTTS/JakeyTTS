using JakeyTTS.Core;
using JakeyTTS.Views;
using JakeyTTS.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace JakeyTTS.Views
{
    public sealed partial class HomePage : Page
    {
        private readonly TwitchService _service = TwitchService.Instance;

        public HomePage() { this.InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            _service.ConnectionStateChanged += OnServiceChanged;
            UpdateUIState();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _service.ConnectionStateChanged -= OnServiceChanged;
        }

        private void OnServiceChanged(object? sender, EventArgs e)
        {
            // Sync with UI thread
            this.DispatcherQueue.TryEnqueue(() => UpdateUIState());
        }

        private void UpdateUIState()
        {
            bool isConfigured = !string.IsNullOrEmpty(_service.Config.Token);
            bool isRunning = _service.IsConnected;

            ConfigureBtn.Visibility = isConfigured ? Visibility.Collapsed : Visibility.Visible;
            StartBtn.Visibility = (!isRunning && isConfigured) ? Visibility.Visible : Visibility.Collapsed;
            StopBtn.Visibility = (isRunning && isConfigured) ? Visibility.Visible : Visibility.Collapsed;

            if (isConfigured && _service.Config.ScopesVersion < 1)
            {
                ScopesInfoBar.IsOpen = true;
                StartBtn.IsEnabled = false; // Block starting service if out of date
            }
            else
            {
                ScopesInfoBar.IsOpen = false;
                StartBtn.IsEnabled = true;
            }
        }

        private void GoToSettings_Click(object sender, RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(TTSConfigPage));
        }

        private async void ServiceToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_service.IsConnected) await _service.Disconnect();
            else await _service.Connect();
        }

        private void ConfigureBtn_Click(object sender, RoutedEventArgs e) => this.Frame.Navigate(typeof(TTSConfigPage));
    }
}


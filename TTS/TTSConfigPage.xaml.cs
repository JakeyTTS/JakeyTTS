using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KokoroSharp;
using KokoroSharp.Core;
using JakeyTTS.Twitch;

namespace JakeyTTS
{
    public sealed partial class TTSConfigPage : Page
    {
        private readonly TwitchService _service = TwitchService.Instance;

        // Mapping display names to the Kokoro Engine language enum
        private readonly Dictionary<string, KokoroLanguage> _languageMap = new Dictionary<string, KokoroLanguage>
        {
            { "Spanish", KokoroLanguage.Spanish },
            { "English (US)", KokoroLanguage.AmericanEnglish },
            { "English (UK)", KokoroLanguage.BritishEnglish },
            { "French", KokoroLanguage.French },
            { "Japanese", KokoroLanguage.Japanese }
        };

        public TTSConfigPage()
        {
            this.InitializeComponent();
            InitializeUI();

            // EVENT SUBSCRIPTION: 
            // The engine indexes 50 voice files in the background on startup.
            // This listener refreshes the UI automatically once that background task completes.
            _service.TtsEngineReady += (s, e) => {
                this.DispatcherQueue.TryEnqueue(() => LoadVoicesForSelectedLanguage());
            };
        }

        /// <summary>
        /// Populates the UI elements with saved configuration values.
        /// </summary>
        private void InitializeUI()
        {
            VolumeSlider.Value = _service.Config.GlobalVolume;

            // 1. Setup Audio Output Devices (up to 3 simultaneous outputs)
            var devices = _service.GetAudioDevices();
            AudioDeviceCombo.ItemsSource = devices;
            AudioDeviceCombo2.ItemsSource = devices;
            AudioDeviceCombo3.ItemsSource = devices;

            AudioDeviceCombo.SelectedItem = devices.Contains(_service.Config.SelectedAudioDevice)
                ? _service.Config.SelectedAudioDevice : "Default System Device";

            AudioDeviceCombo2.SelectedItem = devices.Contains(_service.Config.SelectedAudioDevice2)
                ? _service.Config.SelectedAudioDevice2 : "None";

            AudioDeviceCombo3.SelectedItem = devices.Contains(_service.Config.SelectedAudioDevice3)
                ? _service.Config.SelectedAudioDevice3 : "None";

            // 2. Setup Language Selector
            LanguageCombo.ItemsSource = _languageMap.Keys.ToList();
            int savedIndex = _service.Config.LanguageIndex;
            LanguageCombo.SelectedIndex = (savedIndex >= 0 && savedIndex < _languageMap.Count) ? savedIndex : 0;

            // 3. Initial attempt to load voices (might show "Loading" if engine is still starting)
            LoadVoicesForSelectedLanguage();

            // 4. Setup Toggles
            ReadChatToggle.IsChecked = _service.Config.ReadChatEnabled;
            TestModeToggle.IsChecked = _service.Config.TestModeActive;

            UpdateAccountStatus();
            UpdateServiceButton();
        }

        #region Audio & Voice Logic

        /// <summary>
        /// Filters and displays voices based on the selected language.
        /// Handles the "Loading" state if the engine hasn't finished indexing files.
        /// </summary>
        private void LoadVoicesForSelectedLanguage()
        {
            if (LanguageCombo.SelectedItem == null) return;

            string selectedKey = LanguageCombo.SelectedItem.ToString();
            if (!_languageMap.TryGetValue(selectedKey, out var selectedLang)) return;

            try
            {
                // Accessing the shared voice list from the Kokoro library
                var voices = KokoroVoiceManager.GetVoices(selectedLang);

                if (voices != null && voices.Any())
                {
                    var voiceNames = voices.Select(v => v.Name).OrderBy(n => n).ToList();
                    VoiceCombo.ItemsSource = voiceNames;

                    var savedVoice = _service.Config.DefaultVoice;

                    // Re-select the saved voice if it exists in the current language, else pick first
                    if (!string.IsNullOrEmpty(savedVoice) && voiceNames.Contains(savedVoice))
                        VoiceCombo.SelectedItem = savedVoice;
                    else
                        VoiceCombo.SelectedIndex = 0;

                    LogUI($"✅ Voices loaded for {selectedKey}.");
                }
                else
                {
                    // Placeholder while background indexing is in progress
                    VoiceCombo.ItemsSource = new List<string> { "Engine loading voices..." };
                }
            }
            catch (Exception ex) { LogUI($"❌ Error loading voices: {ex.Message}"); }

            _service.Config.LanguageIndex = LanguageCombo.SelectedIndex;
            _service.Config.Save();
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadVoicesForSelectedLanguage();
        }

        private void VoiceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VoiceCombo.SelectedItem is string voiceName && voiceName != "Engine loading voices...")
            {
                _service.Config.DefaultVoice = voiceName;
                _service.Config.Save();
            }
        }

        private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_service?.Config != null)
            {
                _service.Config.GlobalVolume = (float)e.NewValue;
                _service.Config.Save();
            }
        }

        private void AudioDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AudioDeviceCombo.SelectedItem is string name)
            {
                _service.Config.SelectedAudioDevice = name;
                _service.Config.Save();
            }
        }

        private void AudioDeviceCombo2_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AudioDeviceCombo2.SelectedItem is string name)
            {
                _service.Config.SelectedAudioDevice2 = name;
                _service.Config.Save();
            }
        }

        private void AudioDeviceCombo3_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AudioDeviceCombo3.SelectedItem is string name)
            {
                _service.Config.SelectedAudioDevice3 = name;
                _service.Config.Save();
            }
        }

        #endregion

        #region Service & Test Handlers

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            ConnectBtn.IsEnabled = false;
            if (_service.IsConnected) await _service.Disconnect();
            else await _service.Connect();
            UpdateServiceButton();
            ConnectBtn.IsEnabled = true;
        }

        private void UpdateServiceButton()
        {
            if (_service.IsConnected)
            {
                ConnectIcon.Symbol = Symbol.Stop;
                ConnectText.Text = "Stop Service";
                ConnectBtn.Style = (Style)Application.Current.Resources["DefaultButtonStyle"];
            }
            else
            {
                ConnectIcon.Symbol = Symbol.Play;
                ConnectText.Text = "Connect & Start Service";
                ConnectBtn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            }
        }

        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(TestInput.Text))
            {
                // Play a sample message using current configuration
                await _service.ProcessAndSpeak(TestInput.Text,"test");
            }
        }

        #endregion

        #region Accounts & Settings Toggles

        private void UpdateAccountStatus()
        {
            LinkStreamerBtn.Content = string.IsNullOrEmpty(_service.Config.UserName)
                ? "Link Broadcaster Account"
                : $"Channel: {_service.Config.UserName}";

            LinkBotBtn.Content = _service.Config.IsBotConnected
                ? "Bot Account Linked"
                : "Link Bot Account (Optional)";
        }

        private async void LinkStreamer_Click(object sender, RoutedEventArgs e)
        {
            await _service.PerformAuth(false);
            UpdateAccountStatus();
        }

        private async void LinkBot_Click(object sender, RoutedEventArgs e)
        {
            await _service.PerformAuth(true);
            UpdateAccountStatus();
        }

        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            if (_service?.Config == null) return;
            _service.Config.ReadChatEnabled = ReadChatToggle.IsChecked ?? true;
            _service.Config.TestModeActive = TestModeToggle.IsChecked ?? false;
            _service.Config.Save();
        }

        #endregion

        private void LogUI(string msg) => MainWindow.Instance?.Log(msg);
    }
}
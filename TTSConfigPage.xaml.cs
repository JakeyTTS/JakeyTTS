using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using KokoroSharp;
using KokoroSharp.Core;

namespace JakeyTTS
{
    public sealed partial class TTSConfigPage : Page
    {
        private readonly TwitchService _service = TwitchService.Instance;

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
        }

        private void InitializeUI()
        {
            LanguageCombo.ItemsSource = _languageMap.Keys.ToList();
            LanguageCombo.SelectedIndex = _service.Config.LanguageIndex;

            ReadChatToggle.IsChecked = _service.Config.ReadChatEnabled;
            TestModeToggle.IsChecked = _service.Config.TestModeActive;

            UpdateAccountStatus();
            UpdateServiceButton(); // Set initial button state
        }

        #region Service & Test Handlers

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_service.IsConnected)
            {
                LogUI("🛑 Stopping service...");
                await _service.Disconnect();
            }
            else
            {
                LogUI("🔄 Attempting to connect to Twitch EventSub...");
                await _service.Connect();
            }

            UpdateServiceButton();
        }

        private void UpdateServiceButton()
        {
            if (_service.IsConnected)
            {
                ConnectIcon.Symbol = Symbol.Stop;
                ConnectText.Text = "Stop Service";
                // Optional: Change button to a different style when active
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
            if (string.IsNullOrWhiteSpace(TestInput.Text)) return;

            LogUI($"🎙 Testing Voice: {_service.Config.DefaultVoice}");
            await _service.ProcessAndSpeak(TestInput.Text);
        }

        #endregion

        #region Voice Logic

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageCombo.SelectedItem == null) return;

            string selectedKey = LanguageCombo.SelectedItem.ToString();
            var selectedLang = _languageMap[selectedKey];

            try
            {
                var voices = KokoroVoiceManager.GetVoices(selectedLang);

                if (voices != null && voices.Any())
                {
                    VoiceCombo.ItemsSource = voices.Select(v => v.Name).ToList();

                    var savedVoice = _service.Config.DefaultVoice;
                    if (!string.IsNullOrEmpty(savedVoice) && voices.Any(v => v.Name == savedVoice))
                    {
                        VoiceCombo.SelectedItem = savedVoice;
                    }
                    else
                    {
                        VoiceCombo.SelectedIndex = 0;
                    }
                }
                else
                {
                    LogUI($"⚠ No voices found for {selectedKey} in /voices folder.");
                }
            }
            catch (Exception ex)
            {
                LogUI($"❌ Directory Error: {ex.Message}");
                LogUI($"Ensure files are in: {System.IO.Path.Combine(AppContext.BaseDirectory, "voices")}");
            }

            _service.Config.LanguageIndex = LanguageCombo.SelectedIndex;
            _service.Config.Save();
        }

        private void VoiceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VoiceCombo.SelectedItem is string voiceName)
            {
                _service.Config.DefaultVoice = voiceName;
                _service.Config.Save();
            }
        }

        #endregion

        #region Account & General Settings

        private void UpdateAccountStatus()
        {
            LinkStreamerBtn.Content = string.IsNullOrEmpty(_service.Config.UserName)
                ? "Link Streamer Account" : $"Channel: {_service.Config.UserName}";

            LinkBotBtn.Content = _service.Config.IsBotConnected
                ? "Bot Linked" : "Link Bot (Optional)";
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
            _service.Config.ReadChatEnabled = ReadChatToggle.IsChecked ?? true;
            _service.Config.TestModeActive = TestModeToggle.IsChecked ?? false;
            _service.Config.Save();
        }

        #endregion

        private void LogUI(string msg) => MainWindow.Instance?.Log(msg);
    }
}
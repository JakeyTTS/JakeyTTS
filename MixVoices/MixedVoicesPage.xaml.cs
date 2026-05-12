using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KokoroSharp;
using KokoroSharp.Core;

namespace JakeyTTS.MixVoices
{
    public sealed partial class MixedVoicesPage : Page
    {
        private readonly TwitchService _service = TwitchService.Instance;
        private bool _isNormalizing = false;

        public ObservableCollection<MixedVoiceItem> MixList { get; set; }
        public List<string> Languages { get; set; }

        private readonly Dictionary<string, KokoroLanguage> _languageMap = new()
        {
            { "Spanish", KokoroLanguage.Spanish },
            { "English (US)", KokoroLanguage.AmericanEnglish },
            { "English (UK)", KokoroLanguage.BritishEnglish },
            { "French", KokoroLanguage.French },
            { "Japanese", KokoroLanguage.Japanese }
        };

        public MixedVoicesPage()
        {
            this.InitializeComponent();
            Languages = _languageMap.Keys.ToList();

            // Load saved configurations
            MixList = new ObservableCollection<MixedVoiceItem>(_service.Config.MixedVoices ?? new());
            MixGrid.ItemsSource = MixList;

            // Handle background engine completion
            _service.TtsEngineReady += (s, e) => this.DispatcherQueue.TryEnqueue(() => {
                if (MixGrid.SelectedItem is MixedVoiceItem item)
                {
                    // Force refresh components list to reload voice ComboBoxes
                    var current = item.Components;
                    ComponentsList.ItemsSource = null;
                    ComponentsList.ItemsSource = current;
                }
            });

            MixGrid.SelectionChanged += (s, e) => {
                if (MixGrid.SelectedItem is MixedVoiceItem item)
                    ComponentsList.ItemsSource = item.Components;
                else
                    ComponentsList.ItemsSource = null;
            };
        }

        /// <summary>
        /// Populates the voice list immediately when the UI loads to prevent blank selections.
        /// </summary>
        private void LanguageBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox langBox && langBox.DataContext is VoiceWeight weight)
            {
                // Safely identify the language from the voice name prefix
                string voiceName = weight.VoiceName ?? "";
                string voicePrefix = voiceName.Split('_').FirstOrDefault()?.ToLower();
                string identifiedLang = "English (US)"; // Default

                if (voicePrefix == "es") identifiedLang = "Spanish";
                else if (voicePrefix == "af" || voicePrefix == "am") identifiedLang = "English (US)";
                else if (voicePrefix == "bf" || voicePrefix == "bm") identifiedLang = "English (UK)";
                else if (voicePrefix == "ff") identifiedLang = "French";
                else if (voicePrefix == "jf" || voicePrefix == "jm") identifiedLang = "Japanese";

                langBox.SelectedItem = identifiedLang;

                // Force initial population of the voice sibling ComboBox
                PopulateVoiceList(langBox, identifiedLang, voiceName);
            }
        }

        private void ComponentLanguage_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb && cb.SelectedItem is string langName)
            {
                PopulateVoiceList(cb, langName);
            }
        }

        /// <summary>
        /// Robust helper to find and populate the Voice selection ComboBox.
        /// </summary>
        private void PopulateVoiceList(ComboBox langBox, string langName, string currentVoice = null)
        {
            if (!_languageMap.TryGetValue(langName, out var lang)) return;

            try
            {
                var voices = KokoroVoiceManager.GetVoices(lang).Select(v => v.Name).OrderBy(n => n).ToList();

                // Use the visual tree parent safely
                if (langBox.Parent is Grid parentGrid)
                {
                    var voiceBox = parentGrid.Children.OfType<ComboBox>().ElementAtOrDefault(1);
                    if (voiceBox != null)
                    {
                        voiceBox.ItemsSource = voices;

                        // Select current voice or first available
                        if (!string.IsNullOrEmpty(currentVoice) && voices.Contains(currentVoice))
                            voiceBox.SelectedItem = currentVoice;
                        else if (voices.Any())
                            voiceBox.SelectedIndex = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error populating voices: {ex.Message}");
            }
        }

        private void AddMix_Click(object sender, RoutedEventArgs e)
        {
            // Use the App default voice as the starting point
            string defaultVoice = _service.Config.DefaultVoice ?? "af_bella";

            var newItem = new MixedVoiceItem
            {
                Name = "Hybrid_" + (MixList.Count + 1),
                IsEnabled = true,
                Components = new ObservableCollection<VoiceWeight>()
            };

            newItem.Components.Add(new VoiceWeight { VoiceName = defaultVoice, Weight = 1.0f });

            MixList.Insert(0, newItem);
            MixGrid.SelectedItem = newItem;
        }

        private void DeleteMix_Click(object sender, RoutedEventArgs e)
        {
            MixedVoiceItem itemToDelete = null;

            if (sender is Button btn && btn.DataContext is MixedVoiceItem cardItem)
                itemToDelete = cardItem;
            else
                itemToDelete = MixGrid.SelectedItem as MixedVoiceItem;

            if (itemToDelete != null)
            {
                MixList.Remove(itemToDelete);
                if (MixGrid.SelectedItem == itemToDelete) MixGrid.SelectedItem = null;
            }
        }

        private async void TestMix_Click(object sender, RoutedEventArgs e)
        {
            if (MixGrid.SelectedItem is MixedVoiceItem item)
            {
                SaveInternal();
                await _service.ProcessAndSpeak($"[mix:{item.Name}] This is a voice mix preview.");
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveInternal();
            MainWindow.Instance.Log("💾 Mixed voices library updated.");
        }

        private void SaveInternal()
        {
            SaveBtn?.Focus(FocusState.Programmatic);
            _service.Config.MixedVoices = MixList.ToList();
            _service.Config.Save();
        }

        private void AddComponent_Click(object sender, RoutedEventArgs e)
        {
            if (MixGrid.SelectedItem is MixedVoiceItem item)
            {
                string defaultVoice = _service.Config.DefaultVoice ?? "af_bella";
                item.Components.Add(new VoiceWeight { VoiceName = defaultVoice, Weight = 0f });
                RebalanceAll(item);
            }
        }

        private void RebalanceAll(MixedVoiceItem item)
        {
            _isNormalizing = true;
            if (item.Components.Count == 0) return;
            float share = 1.0f / item.Components.Count;
            foreach (var c in item.Components) c.Weight = share;
            _isNormalizing = false;
        }

        private void RemoveComponent_Click(object sender, RoutedEventArgs e)
        {
            if (MixGrid.SelectedItem is MixedVoiceItem item && (sender as Button)?.DataContext is VoiceWeight weight)
            {
                item.Components.Remove(weight);
                RebalanceAll(item);
            }
        }

        private void WeightSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isNormalizing || MixGrid.SelectedItem is not MixedVoiceItem item || item.Components.Count <= 1) return;

            _isNormalizing = true;
            if (sender is Slider slider && slider.DataContext is VoiceWeight changedComponent)
            {
                float newValue = (float)e.NewValue;
                float totalOtherWeights = item.Components.Where(c => c != changedComponent).Sum(c => c.Weight);
                float targetRemaining = 1.0f - newValue;

                if (totalOtherWeights > 0.001f)
                {
                    foreach (var comp in item.Components.Where(c => c != changedComponent))
                        comp.Weight = (comp.Weight / totalOtherWeights) * targetRemaining;
                }
                else
                {
                    float share = targetRemaining / (item.Components.Count - 1);
                    foreach (var comp in item.Components.Where(c => c != changedComponent)) comp.Weight = share;
                }
            }
            _isNormalizing = false;
        }
    }
}
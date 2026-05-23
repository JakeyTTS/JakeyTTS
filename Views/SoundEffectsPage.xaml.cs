using JakeyTTS.Core;
using JakeyTTS.Views;
using JakeyTTS.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace JakeyTTS.Views
{
    public sealed partial class SoundEffectsPage : Page
    {
        public ObservableCollection<SoundEffectItem> SfxList { get; set; }
        public ObservableCollection<SoundEffectItem> FilteredSfxList { get; set; }

        public SoundEffectsPage()
        {
            this.InitializeComponent();
            var existing = TwitchService.Instance.Config.SoundEffects ?? new List<SoundEffectItem>();
            SfxList = new ObservableCollection<SoundEffectItem>(existing);
            FilteredSfxList = new ObservableCollection<SoundEffectItem>(existing);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string query = SearchBox.Text?.ToLower() ?? "";
            FilteredSfxList.Clear();
            foreach (var item in SfxList)
            {
                if (string.IsNullOrWhiteSpace(query) || 
                    (item.TagName?.ToLower().Contains(query) == true) || 
                    (item.FileName?.ToLower().Contains(query) == true))
                {
                    FilteredSfxList.Add(item);
                }
            }
        }

        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.FileTypeFilter.Add(".wav");
            picker.FileTypeFilter.Add(".mp3");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                string soundsDir = Path.Combine(AppConfig.BaseFolder, "sounds");
                if (!Directory.Exists(soundsDir)) Directory.CreateDirectory(soundsDir);

                string destPath = Path.Combine(soundsDir, file.Name);
                File.Copy(file.Path, destPath, true);

                var newItem = new SoundEffectItem
                {
                    TagName = Path.GetFileNameWithoutExtension(file.Name).Replace(" ", "").ToLower(),
                    FileName = file.Name,
                    IsEnabled = true
                };
                SfxList.Add(newItem);
                ApplyFilter();
                
                MainWindow.Instance.Log($"🎵 Imported SFX: {file.Name}");
            }
        }

        private async void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SoundEffectItem item)
            {
                if (string.IsNullOrEmpty(item.TagName)) return;
                await TtsEngine.Instance.PlaySoundEffect(item.TagName);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SoundEffectItem selected)
            {
                SfxList.Remove(selected);
                FilteredSfxList.Remove(selected);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            TwitchService.Instance.Config.SoundEffects = SfxList.ToList();
            TwitchService.Instance.Config.Save();
            MainWindow.Instance.Log("💾 SFX Settings saved.");
        }
    }
}

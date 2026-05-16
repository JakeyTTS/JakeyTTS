using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.Runtime.InteropServices;
using JakeyTTS.Melodies;

namespace JakeyTTS
{
    public sealed partial class MelodyPage : Page
    {
        public ObservableCollection<Melody> AllMelodies { get; set; } = new();

        public MelodyPage()
        {
            this.InitializeComponent();
            LoadMelodies();
        }

        private void LoadMelodies()
        {
            MelodyService.Instance.Initialize();
            AllMelodies = new ObservableCollection<Melody>(MelodyService.Instance.Melodies);
            UpdateUIList("");
        }

        private void UpdateUIList(string filter)
        {
            var filtered = string.IsNullOrWhiteSpace(filter)
                ? AllMelodies
                : new ObservableCollection<Melody>(AllMelodies.Where(m => m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)));
            MelodyCardsList.ItemsSource = filtered;
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => UpdateUIList(sender.Text);

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button).Tag is Melody m)
                this.Frame.Navigate(typeof(MelodyEditorPage), m);
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            var m = new Melody { Name = "new_melody", Points = new List<MelodyPoint> { new() { TimePct = 0, Pitch = 1 }, new() { TimePct = 1, Pitch = 1 } } };
            this.Frame.Navigate(typeof(MelodyEditorPage), m);
        }

        private async void PlayPreview_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button).Tag is Melody m)
            {
                // FIXED: Redirigido de forma segura al nuevo Singleton TtsEngine
                await TtsEngine.Instance.ProcessAndSpeak($"[melody:{m.Name}] This is a melody preview test.", "test");
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button).Tag is Melody m)
            {
                MelodyService.Instance.Delete(m);
                AllMelodies.Remove(m);
                UpdateUIList(SearchBox.Text);
            }
        }

        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();

                // Obtener HWND de la forma oficial de WinUI 3
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);

                if (hwnd == IntPtr.Zero)
                {
                    MainWindow.Instance.Log("❌ Error: Could not get Window Handle.");
                    return;
                }

                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                picker.ViewMode = PickerViewMode.List;
                picker.FileTypeFilter.Add(".json");

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    string json = await File.ReadAllTextAsync(file.Path);
                    var m = JsonSerializer.Deserialize<Melody>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (m != null)
                    {
                        MelodyService.Instance.Save(m);
                        LoadMelodies();
                        MainWindow.Instance.Log($"✅ Imported: {m.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.Instance.Log($"❌ Picker Error: {ex.Message}");
            }
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button).Tag is Melody m)
            {
                try
                {
                    var picker = new FileSavePicker();
                    IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                    picker.SuggestedFileName = m.Name;
                    picker.FileTypeChoices.Add("JSON File", new List<string>() { ".json" });

                    var file = await picker.PickSaveFileAsync();
                    if (file != null)
                    {
                        string json = JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(file.Path, json);
                        MainWindow.Instance.Log($"💾 Exported: {m.Name}");
                    }
                }
                catch (Exception ex) { MainWindow.Instance.Log($"❌ Export Error: {ex.Message}"); }
            }
        }

        private void OnIsEnabledToggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch ts && ts.DataContext is Melody m)
                MelodyService.Instance.Save(m);
        }
    }
}
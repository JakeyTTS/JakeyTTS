using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Reflection;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Windows.Storage.Pickers;
using WinRT.Interop;
using JakeyTTS.Melodies;

namespace JakeyTTS
{
    public sealed partial class SettingsPage : Page
    {
        private readonly TwitchService _service = TwitchService.Instance;
        public string AppVersionString { get; set; }

        public SettingsPage()
        {
            this.InitializeComponent();
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            AppVersionString = $"Version {version.Major}.{version.Minor}.{version.Build}";
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
            ConfirmResetDialog.XamlRoot = this.XamlRoot;
            var result = await ConfirmResetDialog.ShowAsync();
            if (result == ContentDialogResult.Primary) PerformFullReset();
        }

        private void PerformFullReset()
        {
            try
            {
                _ = _service.Disconnect();
                _service.Config.ResetToDefaults();
                _service.Config.Save();
                MainWindow.Instance?.Log("⚠️ Factory Reset Complete.");
                ThemeSelector.SelectedIndex = 0;
                this.Frame.Navigate(typeof(HomePage));
            }
            catch (Exception ex) { MainWindow.Instance?.Log($"❌ Reset failed: {ex.Message}"); }
        }

        #region Backup & Restore Logic

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker();
            IntPtr hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
            InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Zip Archive", new List<string>() { ".zip" });
            picker.SuggestedFileName = $"JakeyTTS_Backup_{DateTime.Now:yyyyMMdd}";

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), "JakeyBackup_" + Guid.NewGuid());
                Directory.CreateDirectory(tempPath);

                // 1. Prepare Config (Strip sensitive tokens)
                var configCopy = AppConfig.Load();
                configCopy.Token = "";
                configCopy.BotToken = "";
                configCopy.IsBotConnected = false;

                if (ChkBackupCommands.IsChecked == false) configCopy.Commands.Clear();
                if (ChkBackupRewards.IsChecked == false) configCopy.Redeems.Clear();
                if (ChkBackupSFX.IsChecked == false) configCopy.SoundEffects.Clear();

                string json = JsonSerializer.Serialize(configCopy, JakeyJsonContext.Default.AppConfig);
                File.WriteAllText(Path.Combine(tempPath, "config_backup.json"), json);

                // 2. Copy Folders (SFX & Melodies)
                if (ChkBackupSFX.IsChecked == true)
                {
                    string soundsDir = Path.Combine(AppConfig.BaseFolder, "sounds");
                    if (Directory.Exists(soundsDir)) CopyDirectory(soundsDir, Path.Combine(tempPath, "sounds"));
                }

                if (ChkBackupMelodies.IsChecked == true)
                {
                    string melodiesDir = Path.Combine(AppConfig.BaseFolder, "melodies");
                    if (Directory.Exists(melodiesDir)) CopyDirectory(melodiesDir, Path.Combine(tempPath, "melodies"));
                }

                // 3. Compress and Cleanup
                if (File.Exists(file.Path)) File.Delete(file.Path);
                ZipFile.CreateFromDirectory(tempPath, file.Path);
                Directory.Delete(tempPath, true);

                MainWindow.Instance.Log("✅ Backup exported successfully!");
            }
            catch (Exception ex) { MainWindow.Instance.Log($"❌ Export failed: {ex.Message}"); }
        }

        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            IntPtr hwnd = WindowNative.GetWindowHandle(MainWindow.Instance);
            InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add(".zip");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            try
            {
                string extractPath = Path.Combine(Path.GetTempPath(), "JakeyImport_" + Guid.NewGuid());
                ZipFile.ExtractToDirectory(file.Path, extractPath);

                string backupJsonPath = Path.Combine(extractPath, "config_backup.json");
                if (!File.Exists(backupJsonPath)) throw new Exception("Invalid backup file.");

                string json = File.ReadAllText(backupJsonPath);
                var imported = JsonSerializer.Deserialize(json, JakeyJsonContext.Default.AppConfig);
                bool isCombine = RadioCombine.IsChecked == true;

                if (imported != null)
                {
                    // 1. Commands
                    if (ChkBackupCommands.IsChecked == true)
                    {
                        if (isCombine)
                        {
                            foreach (var cmd in imported.Commands)
                                if (!_service.Config.Commands.Any(c => c.Trigger == cmd.Trigger))
                                    _service.Config.Commands.Add(cmd);
                        }
                        else _service.Config.Commands = imported.Commands;
                    }

                    // 2. Rewards
                    if (ChkBackupRewards.IsChecked == true)
                    {
                        if (isCombine)
                        {
                            foreach (var rw in imported.Redeems)
                                if (!_service.Config.Redeems.Any(r => r.Id == rw.Id))
                                    _service.Config.Redeems.Add(rw);
                        }
                        else _service.Config.Redeems = imported.Redeems;
                    }

                    // 3. Sound Effects (Config + Files)
                    if (ChkBackupSFX.IsChecked == true)
                    {
                        string soundsDir = Path.Combine(AppConfig.BaseFolder, "sounds");
                        string importedSounds = Path.Combine(extractPath, "sounds");

                        if (Directory.Exists(importedSounds))
                        {
                            if (!isCombine && Directory.Exists(soundsDir)) Directory.Delete(soundsDir, true);
                            CopyDirectory(importedSounds, soundsDir);
                        }

                        if (isCombine)
                        {
                            foreach (var sfx in imported.SoundEffects)
                                if (!_service.Config.SoundEffects.Any(s => s.TagName == sfx.TagName))
                                    _service.Config.SoundEffects.Add(sfx);
                        }
                        else _service.Config.SoundEffects = imported.SoundEffects;
                    }

                    // 4. Melodies (Files + Service Refresh)
                    if (ChkBackupMelodies.IsChecked == true)
                    {
                        string melodiesDir = Path.Combine(AppConfig.BaseFolder, "melodies");
                        string importedMelodies = Path.Combine(extractPath, "melodies");

                        if (Directory.Exists(importedMelodies))
                        {
                            // If replace mode, clear local melodies folder first
                            if (!isCombine && Directory.Exists(melodiesDir))
                                Directory.Delete(melodiesDir, true);

                            CopyDirectory(importedMelodies, melodiesDir);

                            // CRITICAL: Reload the MelodyService from disk
                            MelodyService.Instance.Initialize();
                        }
                    }

                    _service.Config.Save();
                    MainWindow.Instance.Log(isCombine ? "✅ Backup merged successfully!" : "✅ Backup restored (Replaced)!");

                    if (string.IsNullOrEmpty(_service.Config.Token))
                        MainWindow.Instance.Log("⚠️ Security: Please re-link your Twitch account.");
                }

                Directory.Delete(extractPath, true);
            }
            catch (Exception ex) { MainWindow.Instance.Log($"❌ Import failed: {ex.Message}"); }
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
            foreach (var subDir in Directory.GetDirectories(sourceDir))
                CopyDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
        }

        #endregion
    }
}
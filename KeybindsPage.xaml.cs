using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using Windows.UI.Core;

namespace JakeyTTS
{
    public sealed partial class KeybindsPage : Page
    {
        private Button? _activeButton;

        public KeybindsPage() { this.InitializeComponent(); LoadKeys(); }

        private void LoadKeys()
        {
            var c = TwitchService.Instance.Config;
            BtnToggle.Content = FormatKey(c.ToggleServiceMod, c.ToggleServiceKey);
            BtnStop.Content = FormatKey(c.StopAudioMod, c.StopAudioKey);
            BtnReplay.Content = FormatKey(c.ReplayLastMod, c.ReplayLastKey);
        }

        private void Record_Click(object sender, RoutedEventArgs e)
        {
            _activeButton = (Button)sender;
            _activeButton.Content = "Listening...";
            _activeButton.KeyDown += OnKeyCaptured;
        }

        private void OnKeyCaptured(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (_activeButton == null) return;
            if (e.Key == VirtualKey.Control || e.Key == VirtualKey.Shift || e.Key == VirtualKey.Menu) return;

            e.Handled = true;
            var c = TwitchService.Instance.Config;

            // Detect Modifiers
            int mod = 0;
            var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            var alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
            var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);

            if (ctrl.HasFlag(CoreVirtualKeyStates.Down)) mod |= 0x0002;
            if (alt.HasFlag(CoreVirtualKeyStates.Down)) mod |= 0x0001;
            if (shift.HasFlag(CoreVirtualKeyStates.Down)) mod |= 0x0004;

            int vk = (int)e.Key;
            if (_activeButton == BtnToggle) { c.ToggleServiceKey = vk; c.ToggleServiceMod = mod; }
            else if (_activeButton == BtnStop) { c.StopAudioKey = vk; c.StopAudioMod = mod; }
            else if (_activeButton == BtnReplay) { c.ReplayLastKey = vk; c.ReplayLastMod = mod; }

            _activeButton.KeyDown -= OnKeyCaptured;
            LoadKeys();
            SaveAndRefresh();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            var tag = (string)((Button)sender).Tag;
            var c = TwitchService.Instance.Config;
            if (tag == "Toggle") { c.ToggleServiceKey = 0; c.ToggleServiceMod = 0; }
            // ... repeat for Stop and Replay ...
            LoadKeys();
            SaveAndRefresh();
        }

        private void Save_Click(object sender, RoutedEventArgs e) => SaveAndRefresh();

        private void SaveAndRefresh()
        {
            TwitchService.Instance.Config.Save();
            HotkeyService.Instance.RefreshHotkeys();
            _activeButton = null;
        }

        private string FormatKey(int mod, int vk)
        {
            if (vk == 0) return "None";
            string s = "";
            if ((mod & 0x0002) != 0) s += "Ctrl+";
            if ((mod & 0x0001) != 0) s += "Alt+";
            if ((mod & 0x0004) != 0) s += "Shift+";
            return s + ((VirtualKey)vk).ToString();
        }
    }
}
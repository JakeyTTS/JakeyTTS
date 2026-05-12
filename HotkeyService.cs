using System;
using System.Runtime.InteropServices;
using System.Linq;

namespace JakeyTTS
{
    public class HotkeyService
    {
        [DllImport("user32.dll")] // Importing the RegisterHotKey function from user32.dll
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] // Importing the UnregisterHotKey function from user32.dll
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public enum HotkeyAction { ToggleService = 1, StopAudio = 2, ReplayLast = 3 }
        private static HotkeyService? _instance;
        public static HotkeyService Instance => _instance ??= new HotkeyService();

        private IntPtr _hwnd;

        public void Setup(IntPtr hwnd) { _hwnd = hwnd; RefreshHotkeys(); }

        /*
         * Called when the user changes hotkeys in the settings, unregisters old hotkeys and registers new ones based on the current configuration.
         */
        public void RefreshHotkeys()
        {
            var c = TwitchService.Instance.Config;
            UnregisterHotKey(_hwnd, 1); UnregisterHotKey(_hwnd, 2); UnregisterHotKey(_hwnd, 3);

            if (c.ToggleServiceKey > 0) RegisterHotKey(_hwnd, 1, (uint)c.ToggleServiceMod, (uint)c.ToggleServiceKey);
            if (c.StopAudioKey > 0) RegisterHotKey(_hwnd, 2, (uint)c.StopAudioMod, (uint)c.StopAudioKey);
            if (c.ReplayLastKey > 0) RegisterHotKey(_hwnd, 3, (uint)c.ReplayLastMod, (uint)c.ReplayLastKey);
        }

        /*
         * Called by MainWindow when a hotkey is pressed, executes the corresponding action based on the hotkey ID.
         */
        public async void ProcessHotkey(int id)
        {
            var s = TwitchService.Instance;
            switch ((HotkeyAction)id)
            {
                case HotkeyAction.ToggleService:
                    if (s.IsConnected) await s.Disconnect(); else await s.Connect();
                    break;
                case HotkeyAction.StopAudio:
                    s.StopCurrentTTS();
                    break;
                case HotkeyAction.ReplayLast:
                    var last = s.History.FirstOrDefault();
                    if (last != null) await s.ProcessAndSpeak(last.Message);
                    break;
            }
        }
    }
}
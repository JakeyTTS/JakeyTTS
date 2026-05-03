using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using Windows.Storage;

namespace JakeyTTS
{
    public class AppConfig
    {
        public string Token { get; set; }
        public string BroadcasterId { get; set; }
        public string UserName { get; set; }
        public string BotToken { get; set; }
        public string BotUserId { get; set; }
        public bool IsBotConnected { get; set; }
        public int LanguageIndex { get; set; }
        public string DefaultVoice { get; set; } = "af_bella";
        public bool ReadChatEnabled { get; set; } = true;
        public bool TestModeActive { get; set; } = false;
        public int SelectedTheme { get; set; } = 0;
        public List<CommandItem> Commands { get; set; }
        public List<RedeemItem> Redeems { get; set; }

        private static string ConfigPath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "config.json");

        public AppConfig() { InitializeDefaults(); }

        private void InitializeDefaults()
        {
            Commands = new List<CommandItem>
            {
                new CommandItem
                {
                    Trigger = "!tts",
                    // IMPORTANTE: Los ejemplos van entre "" para que el chat no los borre
                    Response = "🎙 JakeyTTS: Usa \"[pause:500]\" para pausas y \"[speed:1.2]\" para velocidad. Ejemplo: Hola \"[pause:500]\" amigo.",
                    IsEnabled = true,
                    ShouldSpeak = false,
                    ShouldReplyInChat = true
                },
                new CommandItem
                {
                    Trigger = "!hello",
                    Response = "¡Hola {user}! [speed:0.8] Bienvenido al stream.",
                    IsEnabled = true,
                    ShouldSpeak = true,
                    ShouldReplyInChat = true
                }
            };
            Redeems = new List<RedeemItem>();
        }

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new AppConfig();
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig();
            }
            catch { return new AppConfig(); }
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { MainWindow.Instance?.Log($"❌ Error Save: {ex.Message}"); }
        }

        public void ResetToDefaults() { InitializeDefaults(); }
    }
}
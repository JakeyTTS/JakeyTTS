using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Windows.System;

namespace JakeyTTS
{
    public class AppConfig
    {
        // --- Properties ---
        public string Token { get; set; } = string.Empty; // Twitch OAuth Token
        public string BroadcasterId { get; set; } = string.Empty; // Twitch Broadcaster ID
        public string UserName { get; set; } = string.Empty; // Twitch Username
        public string BotToken { get; set; } = string.Empty; // Twitch Bot OAuth Token
        public string BotUserId { get; set; } = string.Empty; // Twitch Bot User ID
        public bool IsBotConnected { get; set; } = false; // Indicates if bot account is linked and connected
        public int LanguageIndex { get; set; } = 0; // Index for language selection in UI
        public string DefaultVoice { get; set; } = "af_bella"; // Default voice for TTS
        public bool ReadChatEnabled { get; set; } = true; // Whether to read chat messages aloud
        public bool TestModeActive { get; set; } = false;
        public int SelectedTheme { get; set; } = 0;
        public string SelectedAudioDevice { get; set; } = "Default System Device"; 
        public string SelectedAudioDevice2 { get; set; } = "None"; // Optional
        public string SelectedAudioDevice3 { get; set; } = "None"; // Optional
        public float GlobalVolume { get; set; } = 1.0f;

        public List<CommandItem> Commands { get; set; } = new();
        public List<RedeemItem> Redeems { get; set; } = new();
        public List<SoundEffectItem> SoundEffects { get; set; } = new();

        public int ToggleServiceKey { get; set; } = 0;
        public int ToggleServiceMod { get; set; } = 0;
        public int StopAudioKey { get; set; } = 0;
        public int StopAudioMod { get; set; } = 0;
        public int ReplayLastKey { get; set; } = 0;
        public int ReplayLastMod { get; set; } = 0;

        public List<MixedVoiceItem> MixedVoices { get; set; } = new();

        public UserActionsConfig UserActions { get; set; } = new();
        // --- Paths ---
        public static string BaseFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JakeyTTS");
        public static string VoicesFolder => Path.Combine(BaseFolder, "voices");
        private static string ConfigPath => Path.Combine(BaseFolder, "config.json");

        public AppConfig()
        {
            InitializeDefaults();
        }

        public void InitializeDefaults()
        {
            Token = string.Empty;
            BroadcasterId = string.Empty;
            UserName = string.Empty;
            BotToken = string.Empty;
            BotUserId = string.Empty;
            IsBotConnected = false;
            ReadChatEnabled = true;
            GlobalVolume = 1.0f;
            SelectedAudioDevice = "Default System Device";
            SelectedAudioDevice2 = "None";
            SelectedAudioDevice3 = "None";

            ToggleServiceKey = (int)VirtualKey.F9; // Default F9
            StopAudioKey = (int)VirtualKey.F10;    // Default F10
            ReplayLastKey = (int)VirtualKey.F11;   // Default F11

            UserActions = new UserActionsConfig();
            Commands = new List<CommandItem>
            {
                new CommandItem {
                    Trigger = "!tts",
                    Response = "🎙 JakeyTTS: \"[speed+0.8]\" Slower \"[normal]\"\" [speed+1.5]\" Faster! Use\" [pause+1]\" for 1s pause or \"[normal]\" to reset speed.",
                    IsEnabled = true, ShouldSpeak = false, ShouldReplyInChat = true
                },
                new CommandItem {
                    Trigger = "!hello",
                    Response = "Hello {user}! Welcome to the stream.",
                    IsEnabled = true, ShouldSpeak = true, ShouldReplyInChat = true
                }
            };
            Redeems = new List<RedeemItem>();
            SoundEffects = new List<SoundEffectItem>();

            MixedVoices = new List<MixedVoiceItem>();
        }

        // --- NEW: Reset Method ---
        public void ResetToDefaults()
        {
            InitializeDefaults();
            Save();
        }

        public static AppConfig Load()
        {
            if (!File.Exists(ConfigPath)) return new AppConfig();
            try
            {
                string json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize(json, JakeyJsonContext.Default.AppConfig) ?? new AppConfig();
            }
            catch { return new AppConfig(); }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(BaseFolder);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JakeyJsonContext.Default.AppConfig));
            }
            catch { }
        }
    }
}
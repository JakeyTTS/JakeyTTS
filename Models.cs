using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Text.Json;

namespace JakeyTTS
{
    public class BaseNotify : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    
        public class CommandItem
        {
            public string Trigger { get; set; } = "!";
            public string Response { get; set; } = "";
            public bool IsEnabled { get; set; } = true;
            public bool ShouldSpeak { get; set; } = true;
            public bool ShouldReplyInChat { get; set; } = false;
            public bool ReplyAsBot { get; set; } = false;
        }
    

    public class RedeemItem : BaseNotify
    {
        public string Id { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string Name { get; set; }
        public string FixedText { get; set; } = "{user} redeemed {target}";
        public bool ShouldReplyInChat { get; set; } = false;
        public bool ReplyAsBot { get; set; } = false;
    }

    public class TtsEntry
    {
        public string User { get; set; }
        public string Message { get; set; }
        public string Time { get; set; }
        public TtsEntry(string user, string message, string time)
        {
            User = user;
            Message = message;
            Time = time;
        }
    }

    [JsonSerializable(typeof(AppConfig))]
    [JsonSerializable(typeof(CommandItem))]
    [JsonSerializable(typeof(RedeemItem))]
    [JsonSerializable(typeof(List<RedeemItem>))]
    [JsonSerializable(typeof(JsonElement))] // Necesario para las respuestas de la API de Twitch
    internal partial class JakeyJsonContext : JsonSerializerContext
    {
    }
}
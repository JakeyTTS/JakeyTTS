using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using JakeyTTS.Melodies;

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
        public bool IsEnabled { get; set; } = true;
        public string Trigger { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public bool ShouldSpeak { get; set; } = true;
        public bool ShouldReplyInChat { get; set; } = false;
        public bool ReplyAsBot { get; set; } = false;
        public bool SendWebsocket { get; set; } = false;
        public string WebsocketParam { get; set; } = string.Empty;

        private string _triggerPlugin = "None";
        public string TriggerPlugin
        {
            get
            {
                if (_triggerPlugin == "None" && SendWebsocket)
                {
                    return "All";
                }
                return _triggerPlugin;
            }
            set
            {
                _triggerPlugin = value;
                SendWebsocket = (value != "None");
            }
        }
    }


    public class RedeemItem : BaseNotify
    {
        public string Id { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string Name { get; set; }
        public string FixedText { get; set; } = "{user} redeemed {target}";
        public bool ShouldReplyInChat { get; set; } = false;
        public bool ReplyAsBot { get; set; } = false;

        private bool _sendWebsocket = false;
        public bool SendWebsocket
        {
            get => _sendWebsocket;
            set { if (_sendWebsocket == value) return; _sendWebsocket = value; OnPropertyChanged(); }
        }

        private string _websocketParam = string.Empty;
        public string WebsocketParam
        {
            get => _websocketParam;
            set { if (_websocketParam == value) return; _websocketParam = value; OnPropertyChanged(); }
        }

        private string _triggerPlugin = "None";
        public string TriggerPlugin
        {
            get
            {
                if (_triggerPlugin == "None" && SendWebsocket)
                {
                    return "All";
                }
                return _triggerPlugin;
            }
            set
            {
                if (_triggerPlugin == value) return;
                _triggerPlugin = value;
                SendWebsocket = (value != "None");
                OnPropertyChanged();
            }
        }
    }

    public class TriggerOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class TtsEntry
    {
        public string User { get; set; }
        public string Message { get; set; }
        public string Time { get; set; }
        public string Source { get; set; } // "Command" or "Reward"

        public TtsEntry(string user, string message, string time, string source)
        {
            User = user;
            Message = message;
            Time = time;
            Source = source;
        }
    }

    public class SoundEffectItem : BaseNotify
    {
        public string TagName { get; set; } // e.g., "laugh" -> triggers [laugh]
        public string FileName { get; set; } // The name of the file in AppData
        public bool IsEnabled { get; set; } = true;

        [JsonIgnore]
        public string FullPath => Path.Combine(AppConfig.BaseFolder, "sounds", FileName);
    }

    public class VoiceWeight : BaseNotify
    {
        private string _voiceName = "";
        public string VoiceName
        {
            get => _voiceName;
            set { if (_voiceName == value) return; _voiceName = value; OnPropertyChanged(); }
        }

        private float _weight = 0.5f;
        public float Weight
        {
            get => _weight;
            set
            {
                if (Math.Abs(_weight - value) < 0.0001f) return;
                _weight = value;
                OnPropertyChanged();
            }
        }
    }

    public class MixedVoiceItem : BaseNotify
    {
        private string _name = "";
        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; OnPropertyChanged(); }
        }

        public ObservableCollection<VoiceWeight> Components { get; set; } = new();

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (_isEnabled == value) return; _isEnabled = value; OnPropertyChanged(); }
        }
    }

    public class UserActionItem : BaseNotify
    {
        private double _threshold = 0;
        public double Threshold { get => _threshold; set { _threshold = value; OnPropertyChanged(); } }

        private string _response = "";
        public string Response { get => _response; set { _response = value; OnPropertyChanged(); } }

        private bool _isEnabled = true;
        public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); } }

        private bool _shouldPlayUserMessage = true;
        public bool ShouldPlayUserMessage { get => _shouldPlayUserMessage; set { _shouldPlayUserMessage = value; OnPropertyChanged(); } }
    }

    public class UserActionsConfig : BaseNotify
    {
        public ObservableCollection<UserActionItem> BitActions { get; set; } = new();
        public ObservableCollection<UserActionItem> SubActions { get; set; } = new();
        public ObservableCollection<UserActionItem> StreakActions { get; set; } = new();

        private string _subGoalReachedResponse = "Goal reached! {goal_title}";
        public string SubGoalReachedResponse { get => _subGoalReachedResponse; set { _subGoalReachedResponse = value; OnPropertyChanged(); } }

        private string _followerGoalReachedResponse = "We reached our follower goal! {goal_title}";
        public string FollowerGoalReachedResponse { get => _followerGoalReachedResponse; set { _followerGoalReachedResponse = value; OnPropertyChanged(); } }

        private string _bitsGoalReachedResponse = "Bits goal completed! {goal_title}";
        public string BitsGoalReachedResponse { get => _bitsGoalReachedResponse; set { _bitsGoalReachedResponse = value; OnPropertyChanged(); } }

        private string _pointsGoalReachedResponse = "Channel Points goal completed! {goal_title}";
        public string PointsGoalReachedResponse { get => _pointsGoalReachedResponse; set { _pointsGoalReachedResponse = value; OnPropertyChanged(); } }
    }


    [JsonSerializable(typeof(AppConfig))]
    [JsonSerializable(typeof(CommandItem))]
    [JsonSerializable(typeof(RedeemItem))]
    [JsonSerializable(typeof(Melody))]
    [JsonSerializable(typeof(MelodyPoint))]
    [JsonSerializable(typeof(List<Melody>))]
    [JsonSerializable(typeof(List<MelodyPoint>))]
    [JsonSerializable(typeof(List<CommandItem>))]
    [JsonSerializable(typeof(List<RedeemItem>))]
    [JsonSerializable(typeof(List<SoundEffectItem>))]
    [JsonSerializable(typeof(MixedVoiceItem))]
    [JsonSerializable(typeof(VoiceWeight))]
    [JsonSerializable(typeof(List<MixedVoiceItem>))]
    [JsonSerializable(typeof(List<VoiceWeight>))]
    [JsonSerializable(typeof(UserActionsConfig))]
    [JsonSerializable(typeof(UserActionItem))]
    [JsonSerializable(typeof(List<UserActionItem>))]
    [JsonSerializable(typeof(JsonElement))] // Needed for API Twitch responses
    internal partial class JakeyJsonContext : JsonSerializerContext
    {
    }
}
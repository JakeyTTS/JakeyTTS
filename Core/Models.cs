using JakeyTTS.Core;
using JakeyTTS.Views;
using JakeyTTS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using JakeyTTS.Melodies;

namespace JakeyTTS.Core
{
    public class BaseNotify : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class VariableStore : BaseNotify
    {
        public ObservableCollection<StringPair> Scalars { get; set; } = new();
        public ObservableCollection<StringListPair> Lists { get; set; } = new();
    }

    public class StringPair : BaseNotify
    {
        private string _key = "";
        public string Key { get => _key; set { _key = value; OnPropertyChanged(); } }

        private string _value = "";
        public string Value { get => _value; set { _value = value; OnPropertyChanged(); } }

        private bool _isNumber = false;
        public bool IsNumber 
        { 
            get => _isNumber; 
            set 
            { 
                _isNumber = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(NumberVisibility));
                OnPropertyChanged(nameof(TextVisibility));
            } 
        }

        [JsonIgnore]
        public Microsoft.UI.Xaml.Visibility NumberVisibility => _isNumber ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        [JsonIgnore]
        public Microsoft.UI.Xaml.Visibility TextVisibility => !_isNumber ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        [JsonIgnore]
        public double NumericValue
        {
            get
            {
                if (double.TryParse(_value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double res)) return res;
                return 0;
            }
            set
            {
                Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                OnPropertyChanged();
            }
        }
    }

    public class StringListPair : BaseNotify
    {
        private string _key = "";
        public string Key { get => _key; set { _key = value; OnPropertyChanged(); } }

        public ObservableCollection<string> Values { get; set; } = new();
    }

    public enum CommandCondition
    {
        Always,
        IfUserProvidedMessage,
        IfNoMessageProvided,
        IfVariableMatch,
        IfVariableListIsEmpty,
        IfRandomChance
    }

    public interface IActionableItem : System.ComponentModel.INotifyPropertyChanged
    {
        bool UseActionBlocks { get; set; }
        bool GenerateRandomVariable { get; set; }
        string RandomTargetScope { get; set; }
        string RandomTargetVariable { get; set; }
        bool RandomIsFloat { get; set; }
        double RandomMin { get; set; }
        string RandomMinString { get; set; }
        double RandomMax { get; set; }
        string RandomMaxString { get; set; }
        VariableStore LocalVariables { get; set; }
        ObservableCollection<CommandAction> Actions { get; set; }
        System.Collections.ObjectModel.ObservableCollection<string> ScopeOptions { get; }
        void UpgradeToBlocks();
    }

    public class CommandAction : BaseNotify
    {
        private CommandCondition _condition = CommandCondition.Always;
        public CommandCondition Condition { get => _condition; set { _condition = value; OnPropertyChanged(); } }

        // Condition matching
        private string _conditionVariable = "";
        public string ConditionVariable { get => _conditionVariable; set { _conditionVariable = value; OnPropertyChanged(); } }

        private string _conditionOperator = "==";
        public string ConditionOperator { get => _conditionOperator; set { _conditionOperator = value; OnPropertyChanged(); } }

        private string _conditionValue = "";
        public string ConditionValue { get => _conditionValue; set { _conditionValue = value; OnPropertyChanged(); } }

        // Outputs
        private string _response = "";
        public string Response { get => _response; set { _response = value; OnPropertyChanged(); } }

        private bool _shouldSpeak = false;
        public bool ShouldSpeak { get => _shouldSpeak; set { _shouldSpeak = value; OnPropertyChanged(); } }

        private bool _shouldReplyInChat = false;
        public bool ShouldReplyInChat { get => _shouldReplyInChat; set { _shouldReplyInChat = value; OnPropertyChanged(); } }

        private bool _replyAsBot = false;
        public bool ReplyAsBot { get => _replyAsBot; set { _replyAsBot = value; OnPropertyChanged(); } }

        private string _triggerPlugin = "None";
        public string TriggerPlugin { get => _triggerPlugin; set { _triggerPlugin = value; OnPropertyChanged(); } }

        private string _websocketParam = "";
        public string WebsocketParam { get => _websocketParam; set { _websocketParam = value; OnPropertyChanged(); } }

        // Wait
        private int _waitMs = 0;
        public int WaitMs { get => _waitMs; set { _waitMs = value; OnPropertyChanged(); } }

        // Variable Update
        private bool _updateVariable = false;
        public bool UpdateVariable { get => _updateVariable; set { _updateVariable = value; OnPropertyChanged(); } }

        private bool _updateVariableFirst = false;
        public bool UpdateVariableFirst { get => _updateVariableFirst; set { _updateVariableFirst = value; OnPropertyChanged(); } }

        private string _targetVariable = "";
        public string TargetVariable { get => _targetVariable; set { _targetVariable = value; OnPropertyChanged(); } }

        private string _variableScope = "Global";
        public string VariableScope 
        { 
            get => _variableScope; 
            set { _variableScope = value; OnPropertyChanged(); OnPropertyChanged(nameof(ScopedVariables)); } 
        }

        [JsonIgnore]
        public IActionableItem ParentItem { get; set; }

        private string _conditionScope = "Local";
        public string ConditionScope { get => _conditionScope; set { _conditionScope = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConditionScopedVariables)); } }

        [JsonIgnore]
        public ObservableCollection<string> ConditionScopedVariables
        {
            get
            {
                var list = new ObservableCollection<string>();
                VariableStore store = (ConditionScope == "Local" && ParentItem != null) ? ParentItem.LocalVariables : TwitchService.Instance.Config.GlobalVariables;
                if (store != null)
                {
                    foreach (var s in store.Scalars) list.Add(s.Key);
                    foreach (var l in store.Lists) list.Add(l.Key);
                }
                if (ParentItem != null && ParentItem.GenerateRandomVariable && ParentItem.RandomTargetScope == ConditionScope)
                {
                    if (!list.Contains(ParentItem.RandomTargetVariable)) list.Add(ParentItem.RandomTargetVariable);
                }
                return list;
            }
        }

        [JsonIgnore]
        public ObservableCollection<string> ScopedVariables
        {
            get
            {
                var list = new ObservableCollection<string>();
                VariableStore store = (VariableScope == "Local" && ParentItem != null) ? ParentItem.LocalVariables : TwitchService.Instance.Config.GlobalVariables;
                if (store != null)
                {
                    foreach (var s in store.Scalars) list.Add(s.Key);
                    foreach (var l in store.Lists) list.Add(l.Key);
                }
                if (ParentItem != null && ParentItem.GenerateRandomVariable && ParentItem.RandomTargetScope == VariableScope)
                {
                    if (!list.Contains(ParentItem.RandomTargetVariable)) list.Add(ParentItem.RandomTargetVariable);
                }
                return list;
            }
        }

        private string _variableOperator = "Set";
        public string VariableOperator { get => _variableOperator; set { _variableOperator = value; OnPropertyChanged(); } }

        private string _variableValue = "";
        public string VariableValue { get => _variableValue; set { _variableValue = value; OnPropertyChanged(); } }
    }


    public class CommandItem : BaseNotify, IActionableItem
    {
        public bool IsEnabled { get; set; } = true;
        public string Trigger { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public bool ShouldSpeak { get; set; } = true;
        public bool ShouldReplyInChat { get; set; } = false;
        public bool ReplyAsBot { get; set; } = false;
        public bool SendWebsocket { get; set; } = false;
        public string WebsocketParam { get; set; } = string.Empty;
        
        // Permissions
        public bool AllowBroadcaster { get; set; } = true;
        public bool AllowModerator { get; set; } = true;
        public bool AllowVIP { get; set; } = true;
        public bool AllowEveryone { get; set; } = true;

        // Blocks System
        private bool _useActionBlocks = false;
        public bool UseActionBlocks
        {
            get => _useActionBlocks;
            set { if (_useActionBlocks == value) return; _useActionBlocks = value; OnPropertyChanged(); }
        }
        
        // Command Randomizer
        private bool _generateRandomVariable = false;
        public bool GenerateRandomVariable { get => _generateRandomVariable; set { _generateRandomVariable = value; OnPropertyChanged(); } }

        private string _randomTargetScope = "Local";
        public string RandomTargetScope { get => _randomTargetScope; set { _randomTargetScope = value; OnPropertyChanged(); } }

        public System.Collections.ObjectModel.ObservableCollection<string> ScopeOptions { get; } = new System.Collections.ObjectModel.ObservableCollection<string> { "Local", "Global" };

        private string _randomTargetVariable = "RandomRoll";
        public string RandomTargetVariable { get => _randomTargetVariable; set { _randomTargetVariable = value; OnPropertyChanged(); } }
        
        private bool _randomIsFloat = false;
        public bool RandomIsFloat { get => _randomIsFloat; set { _randomIsFloat = value; OnPropertyChanged(); } }

        private double _randomMin = 1;
        public double RandomMin { get => _randomMin; set { _randomMin = value; OnPropertyChanged(); OnPropertyChanged(nameof(RandomMinString)); } }

        public string RandomMinString
        {
            get => _randomMin.ToString(System.Globalization.CultureInfo.InvariantCulture);
            set { if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val)) RandomMin = val; else OnPropertyChanged(); }
        }

        private double _randomMax = 100;
        public double RandomMax { get => _randomMax; set { _randomMax = value; OnPropertyChanged(); OnPropertyChanged(nameof(RandomMaxString)); } }

        public string RandomMaxString
        {
            get => _randomMax.ToString(System.Globalization.CultureInfo.InvariantCulture);
            set { if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val)) RandomMax = val; else OnPropertyChanged(); }
        }
        
        public VariableStore LocalVariables { get; set; } = new();

        public ObservableCollection<CommandAction> Actions { get; set; } = new();

        public void UpgradeToBlocks()
        {
            if (UseActionBlocks) return;
            Actions.Clear();

            var newBlock = new CommandAction 
            { 
                Condition = CommandCondition.Always,
                Response = Response,
                ShouldSpeak = ShouldSpeak,
                ShouldReplyInChat = ShouldReplyInChat,
                ReplyAsBot = ReplyAsBot,
                TriggerPlugin = TriggerPlugin,
                WebsocketParam = WebsocketParam
            };
            
            Actions.Add(newBlock);
            UseActionBlocks = true;
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
                _triggerPlugin = value;
                SendWebsocket = (value != "None");
            }
        }
    }


    public class RedeemItem : BaseNotify, IActionableItem
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

        // Blocks System
        private bool _useActionBlocks = false;
        public bool UseActionBlocks
        {
            get => _useActionBlocks;
            set { if (_useActionBlocks == value) return; _useActionBlocks = value; OnPropertyChanged(); }
        }
        
        // Command Randomizer
        private bool _generateRandomVariable = false;
        public bool GenerateRandomVariable { get => _generateRandomVariable; set { _generateRandomVariable = value; OnPropertyChanged(); } }

        private string _randomTargetScope = "Local";
        public string RandomTargetScope { get => _randomTargetScope; set { _randomTargetScope = value; OnPropertyChanged(); } }

        public System.Collections.ObjectModel.ObservableCollection<string> ScopeOptions { get; } = new System.Collections.ObjectModel.ObservableCollection<string> { "Local", "Global" };

        private string _randomTargetVariable = "RandomRoll";
        public string RandomTargetVariable { get => _randomTargetVariable; set { _randomTargetVariable = value; OnPropertyChanged(); } }
        
        private bool _randomIsFloat = false;
        public bool RandomIsFloat { get => _randomIsFloat; set { _randomIsFloat = value; OnPropertyChanged(); } }

        private double _randomMin = 1;
        public double RandomMin { get => _randomMin; set { _randomMin = value; OnPropertyChanged(); OnPropertyChanged(nameof(RandomMinString)); } }

        public string RandomMinString
        {
            get => _randomMin.ToString(System.Globalization.CultureInfo.InvariantCulture);
            set { if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val)) RandomMin = val; else OnPropertyChanged(); }
        }

        private double _randomMax = 100;
        public double RandomMax { get => _randomMax; set { _randomMax = value; OnPropertyChanged(); OnPropertyChanged(nameof(RandomMaxString)); } }

        public string RandomMaxString
        {
            get => _randomMax.ToString(System.Globalization.CultureInfo.InvariantCulture);
            set { if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val)) RandomMax = val; else OnPropertyChanged(); }
        }
        
        public VariableStore LocalVariables { get; set; } = new();

        public ObservableCollection<CommandAction> Actions { get; set; } = new();

        public void UpgradeToBlocks()
        {
            if (UseActionBlocks) return;
            Actions.Clear();

            var newBlock = new CommandAction 
            { 
                Condition = CommandCondition.Always,
                Response = FixedText,
                ShouldSpeak = true, // Redeems default to speak
                ShouldReplyInChat = ShouldReplyInChat,
                ReplyAsBot = ReplyAsBot,
                TriggerPlugin = TriggerPlugin,
                WebsocketParam = WebsocketParam
            };
            
            Actions.Add(newBlock);
            UseActionBlocks = true;
        }
    }

    public class TriggerOption
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class PronunciationItem : BaseNotify
    {
        private string _word = "";
        public string Word
        {
            get => _word;
            set { if (_word != value) { _word = value; OnPropertyChanged(); } }
        }

        private string _replacement = "";
        public string Replacement
        {
            get => _replacement;
            set { if (_replacement != value) { _replacement = value; OnPropertyChanged(); } }
        }

        private bool _isRegex;
        public bool IsRegex
        {
            get => _isRegex;
            set { if (_isRegex != value) { _isRegex = value; OnPropertyChanged(); } }
        }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (_isEnabled != value) { _isEnabled = value; OnPropertyChanged(); } }
        }
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
    [JsonSerializable(typeof(VariableStore))]
    [JsonSerializable(typeof(CommandAction))]
    [JsonSerializable(typeof(StringPair))]
    [JsonSerializable(typeof(StringListPair))]
    [JsonSerializable(typeof(JsonElement))] // Needed for API Twitch responses
    internal partial class JakeyJsonContext : JsonSerializerContext
    {
    }
}

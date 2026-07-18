using JakeyTTS.Core;
using JakeyTTS.Views;
using JakeyTTS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JakeyTTS.Melodies;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Websockets;

namespace JakeyTTS
{
    public class TwitchService
    {
        private static TwitchService? _instance;
        public static TwitchService Instance => _instance ??= new TwitchService();

        public event EventHandler? ConnectionStateChanged;

        private const string ClientId = "vf3ugrnhvufgcdc7veyajsah8iw75m";
        private const string RedirectUri = "http://localhost:8888/";
        private static readonly Random _rng = new Random();

        public AppConfig Config { get; set; }
        public EventSubWebsocketClient? Client { get; private set; }
        public bool IsConnected => Client != null;
        public ObservableCollection<TtsEntry> History { get; } = new();

        private readonly HttpClient _http = new HttpClient();

        private TwitchService()
        {
            Config = AppConfig.Load();
            MelodyService.Instance.Initialize();

            _ = TtsEngine.Instance;
            PluginServer.Instance.Start();
        }

        #region User Actions Logic
        private async Task HandleCheer(object? s, ChannelCheerArgs e)
        {
            var ev = e.Payload.Event;
            if (Config.UserActions?.BitActions == null) return;

            var action = Config.UserActions.BitActions
                .Where(a => a.IsEnabled && ev.Bits >= a.Threshold)
                .OrderByDescending(a => a.Threshold).FirstOrDefault();

            if (action != null)
            {
                string res = ProcessScript(action.Response, ev.UserName, ev.Message ?? "", "");
                res = res.Replace("{bits}", ev.Bits.ToString(), StringComparison.OrdinalIgnoreCase);

                AddToHistory(ev.UserName, $"{ev.Bits} bits", "Bits");
                await TtsEngine.Instance.ProcessAndSpeak(res, "bits");

                if (action.ShouldPlayUserMessage && !string.IsNullOrWhiteSpace(ev.Message))
                {
                    string cleanUserMessage = Regex.Replace(ev.Message, @"\[.*?\]", "").Trim();
                    if (!string.IsNullOrWhiteSpace(cleanUserMessage))
                    {
                        await TtsEngine.Instance.ProcessAndSpeak($"[reset][pause:500] {cleanUserMessage}", "bits");
                    }
                }
            }
        }

        /* HandleSubscriptionMessage is responsible for processing incoming subscription events, determining the appropriate response based on 
         * user-configured actions for both cumulative months and streak months, and then generating the final message to be spoken via TTS. 
         * It also handles the optional reading of the subscriber's custom message, ensuring that any Twitch-specific formatting is cleaned out before being spoken.
         */
        private async Task HandleSubscriptionMessage(object? s, ChannelSubscriptionMessageArgs e)
        {
            var ev = e.Payload.Event;
            if (Config.UserActions == null) return;

            int cumulativeMonths = ev.CumulativeMonths;
            int? streakMonths = ev.StreakMonths;

            var streakAction = Config.UserActions.StreakActions?.Where(a => a.IsEnabled && streakMonths >= a.Threshold).OrderByDescending(a => a.Threshold).FirstOrDefault();
            var subAction = Config.UserActions.SubActions?.Where(a => a.IsEnabled && cumulativeMonths >= a.Threshold).OrderByDescending(a => a.Threshold).FirstOrDefault();

            string response = "{user} subscribed for {months} months!";
            bool shouldPlayText = true;

            if (streakAction != null) { response = streakAction.Response; shouldPlayText = streakAction.ShouldPlayUserMessage; }
            else if (subAction != null) { response = subAction.Response; shouldPlayText = subAction.ShouldPlayUserMessage; }

            string finalMsg = ProcessScript(response, ev.UserName, ev.Message?.Text ?? "", "");
            finalMsg = finalMsg.Replace("{months}", cumulativeMonths.ToString(), StringComparison.OrdinalIgnoreCase)
                               .Replace("{streak}", streakMonths.ToString(), StringComparison.OrdinalIgnoreCase);

            AddToHistory(ev.UserName, "Subscription", "Sub");
            await TtsEngine.Instance.ProcessAndSpeak(finalMsg, "subs");

            string userWrittenText = ev.Message?.Text;
            if (shouldPlayText && !string.IsNullOrWhiteSpace(userWrittenText))
            {
                string cleanUserMessage = Regex.Replace(userWrittenText, @"\[.*?\]", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleanUserMessage))
                {
                    await TtsEngine.Instance.ProcessAndSpeak($"[reset][pause:500] {cleanUserMessage}", "subs");
                }
            }
        }

        /* HandleGoalProgress listens for updates on channel goals and checks if any configured user actions should be triggered when a goal is completed.
         */
        private async Task HandleGoalProgress(object? s, ChannelGoalProgressArgs e)
        {
            var ev = e.Payload.Event;
            if (ev.CurrentAmount >= ev.TargetAmount && Config.UserActions != null)
            {
                string goalType = ev.Type.ToLowerInvariant();
                string responseTemplate = "";

                if (goalType.Contains("sub")) responseTemplate = Config.UserActions.SubGoalReachedResponse;
                else if (goalType.Contains("follow")) responseTemplate = Config.UserActions.FollowerGoalReachedResponse;
                else if (goalType.Contains("bit")) responseTemplate = Config.UserActions.BitsGoalReachedResponse;
                else if (goalType.Contains("point")) responseTemplate = Config.UserActions.PointsGoalReachedResponse;
                else responseTemplate = "Goal {goal_title} completed!";

                if (!string.IsNullOrEmpty(responseTemplate))
                {
                    string msg = responseTemplate.Replace("{goal_title}", ev.Description, StringComparison.OrdinalIgnoreCase);
                    msg = ProcessScript(msg, "", "", "");
                    await TtsEngine.Instance.ProcessAndSpeak(msg, "goals");
                }
            }
        }
        #endregion

        #region Twitch Connectivity

        /*
         * Connect is responsible for establishing a WebSocket connection to Twitch's EventSub service, 
         * subscribing to relevant events based on the user's configuration, and setting up event handlers to process 
         * incoming Twitch events such as chat messages, reward redemptions, cheers, subscriptions, and goal progress updates. 
         * It also ensures that the UI is updated to reflect the connection status and that any necessary cleanup is performed 
         * if a previous connection exists.
         */
        public async Task Connect()
        {
            if (string.IsNullOrEmpty(Config.Token)) return;
            try
            {
                if (Client != null) await Disconnect();
                Client = new EventSubWebsocketClient();
                Client.WebsocketConnected += async (s, e) => {
                    await Subscribe("channel.chat.message", Client.SessionId);
                    await Subscribe("channel.channel_points_custom_reward_redemption.add", Client.SessionId);
                    await Subscribe("channel.cheer", Client.SessionId);
                    await Subscribe("channel.subscription.message", Client.SessionId);
                    await Subscribe("channel.goal.progress", Client.SessionId);
                    LogUI("🚀 Twitch connected.");
                    ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
                };
                Client.WebsocketDisconnected += async (s, e) => {
                    LogUI("⚠️ Twitch connection lost. Attempting to reconnect...");
                    await Task.Delay(3000);
                    try { if (Client != null) await Client.ReconnectAsync(); } catch { }
                };
                Client.WebsocketReconnected += async (s, e) => {
                    LogUI("🔁 Twitch reconnected. Re-subscribing to events...");
                    await Subscribe("channel.chat.message", Client.SessionId);
                    await Subscribe("channel.channel_points_custom_reward_redemption.add", Client.SessionId);
                    await Subscribe("channel.cheer", Client.SessionId);
                    await Subscribe("channel.subscription.message", Client.SessionId);
                    await Subscribe("channel.goal.progress", Client.SessionId);
                };
                Client.ErrorOccurred += (s, e) => {
                    LogUI($"⚠️ Twitch websocket error: {e.Exception?.Message}");
                };
                Client.ChannelChatMessage += HandleChatMessage;
                Client.ChannelPointsCustomRewardRedemptionAdd += HandleRewardRedemption;
                Client.ChannelCheer += HandleCheer;
                Client.ChannelSubscriptionMessage += HandleSubscriptionMessage;
                Client.ChannelGoalProgress += HandleGoalProgress;
                await Client.ConnectAsync();
            }
            catch { }
        }

        public async Task Disconnect()
        {
            if (Client != null)
            {
                TtsEngine.Instance.Stop();
                await Client.DisconnectAsync();
                Client = null;
                LogUI("🛑 Service stopped.");
                ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /* HandleChatMessage processes incoming chat messages, checks if they match any configured command triggers, verifies user permissions based on badges, and executes the corresponding command actions or responses. 
         * It also handles the optional reading of chat messages via TTS if enabled in the configuration.
         */
        private async Task HandleChatMessage(object? s, ChannelChatMessageArgs e)
        {
            var ev = e.Payload.Event;
            string msg = ev.Message.Text.Trim();
            
            string[] parts = msg.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            string firstWord = parts[0];

            var cmd = Config.Commands?.FirstOrDefault(c => firstWord.Equals(c.Trigger, StringComparison.OrdinalIgnoreCase));
            if (cmd != null && cmd.IsEnabled)
            {
                // PERMISSIONS CHECK
                bool hasPerm = false;
                if (cmd.AllowEveryone) hasPerm = true;
                else if (ev.Badges != null)
                {
                    if (cmd.AllowBroadcaster && ev.Badges.Any(b => b.SetId == "broadcaster")) hasPerm = true;
                    if (cmd.AllowModerator && ev.Badges.Any(b => b.SetId == "moderator")) hasPerm = true;
                    if (cmd.AllowVIP && ev.Badges.Any(b => b.SetId == "vip" || b.SetId == "founder")) hasPerm = true;
                }
                
                if (!hasPerm) return;

                if (cmd.GenerateRandomVariable)
                {
                    double roll;
                    if (cmd.RandomIsFloat)
                    {
                        roll = (cmd.RandomMax - cmd.RandomMin) * _rng.NextDouble() + cmd.RandomMin;
                    }
                    else
                    {
                        roll = _rng.Next((int)Math.Round(cmd.RandomMin), (int)Math.Round(cmd.RandomMax) + 1);
                    }
                    UpdateVariable(cmd.RandomTargetScope, cmd.RandomTargetVariable, "Set", roll.ToString(System.Globalization.CultureInfo.InvariantCulture), cmd, isNumber: true);
                }

                if (cmd.UseActionBlocks)
                {
                    await ExecuteActionBlocks(cmd, cmd.Trigger, ev.ChatterUserName, msg);
                }
                else
                {
                    string res = ProcessScript(cmd.Response, ev.ChatterUserName, msg, cmd.Trigger, cmd);
                    if (cmd.ShouldReplyInChat) await SendChatReply(res, cmd.ReplyAsBot);
                    AddToHistory(ev.ChatterUserName, res, "Command");
                    if (!string.IsNullOrEmpty(cmd.TriggerPlugin) && cmd.TriggerPlugin != "None")
                    {
                        PluginServer.Instance.NotifyTriggerEvent("command", cmd.Trigger, cmd.WebsocketParam, ev.ChatterUserName, msg, cmd.TriggerPlugin);
                    }
                    if (cmd.ShouldSpeak) await TtsEngine.Instance.ProcessAndSpeak(res, "commands");
                }
            }
            else if (Config.ReadChatEnabled)
            {
                if (ev.ChatterUserId == Config.BroadcasterId && !Config.TestModeActive) return;
                AddToHistory(ev.ChatterUserName, msg, "Chat");
                await TtsEngine.Instance.ProcessAndSpeak(msg, "chat");
            }
        }


        /* ExecuteActionBlocks iterates through the defined action blocks for a command, evaluates their conditions, 
         * and executes the corresponding actions such as updating variables, sending chat replies, speaking responses via TTS, and triggering plugins. 
         * It supports various condition types including message presence, variable comparisons, list emptiness, and random chance, 
         * allowing for complex command behaviors based on user input and dynamic variables.
         */
        private async Task ExecuteActionBlocks(IActionableItem cmd, string trigger, string sender, string fullMessage)
        {
            foreach (var action in cmd.Actions)
            {
                // CHECK CONDITION
                bool conditionMet = true;
                string cleanMsg = fullMessage;
                if (!string.IsNullOrEmpty(trigger) && fullMessage.StartsWith(trigger, StringComparison.OrdinalIgnoreCase))
                    cleanMsg = fullMessage.Substring(trigger.Length).Trim();
                
                bool hasMsg = !string.IsNullOrWhiteSpace(cleanMsg);

                if (action.Condition == CommandCondition.IfUserProvidedMessage && !hasMsg) conditionMet = false;
                else if (action.Condition == CommandCondition.IfNoMessageProvided && hasMsg) conditionMet = false;
                else if (action.Condition == CommandCondition.IfVariableMatch)
                {
                    string varValue = GetVariableValue(action.ConditionScope, action.ConditionVariable, cmd);
                    string compareValue = ProcessScript(action.ConditionValue, sender, fullMessage, trigger, cmd, cmd is RedeemItem ri ? ri.Name : null);
                    
                    if (action.ConditionOperator == "==") conditionMet = (varValue == compareValue);
                    else if (action.ConditionOperator == "!=") conditionMet = (varValue != compareValue);
                    else if (action.ConditionOperator == "Contains") conditionMet = varValue.Contains(compareValue, StringComparison.OrdinalIgnoreCase);
                    else if (action.ConditionOperator == ">") { if (double.TryParse(varValue, out double v1) && double.TryParse(compareValue, out double v2)) conditionMet = v1 > v2; else conditionMet = false; }
                    else if (action.ConditionOperator == "<") { if (double.TryParse(varValue, out double v1) && double.TryParse(compareValue, out double v2)) conditionMet = v1 < v2; else conditionMet = false; }
                }
                else if (action.Condition == CommandCondition.IfVariableListIsEmpty)
                {
                    VariableStore store = (action.ConditionScope == "Local" && cmd != null) ? cmd.LocalVariables : Config.GlobalVariables;
                    var lst = store.Lists.FirstOrDefault(l => l.Key.Equals(action.ConditionVariable, StringComparison.OrdinalIgnoreCase));
                    conditionMet = (lst == null || lst.Values.Count == 0);
                }
                else if (action.Condition == CommandCondition.IfRandomChance)
                {
                    if (double.TryParse(action.ConditionValue, out double chance))
                    {
                        double roll = _rng.NextDouble() * 100.0;
                        conditionMet = roll <= chance;
                    }
                    else
                    {
                        conditionMet = false;
                    }
                }

                if (!conditionMet) continue;

                // EXECUTE ACTIONS FOR THIS BLOCK
                if (action.UpdateVariable && action.UpdateVariableFirst && !string.IsNullOrWhiteSpace(action.TargetVariable))
                {
                    UpdateVariable(action.VariableScope, action.TargetVariable, action.VariableOperator, ProcessScript(action.VariableValue, sender, fullMessage, trigger, cmd, cmd is RedeemItem ri2 ? ri2.Name : null), cmd);
                }

                string processedResponse = ProcessScript(action.Response, sender, fullMessage, trigger, cmd, cmd is RedeemItem ri3 ? ri3.Name : null);
                
                if (action.ShouldReplyInChat && !string.IsNullOrWhiteSpace(processedResponse))
                {
                    await SendChatReply(processedResponse, action.ReplyAsBot);
                }
                
                if (action.ShouldSpeak && !string.IsNullOrWhiteSpace(processedResponse))
                {
                    AddToHistory(sender, processedResponse, "Command Block");
                    await TtsEngine.Instance.ProcessAndSpeak(processedResponse, "commands");
                }
                
                if (action.TriggerPlugin != "None")
                {
                    PluginServer.Instance.NotifyTriggerEvent("command", trigger, action.WebsocketParam, sender, fullMessage, action.TriggerPlugin);
                }

                if (action.UpdateVariable && !action.UpdateVariableFirst && !string.IsNullOrWhiteSpace(action.TargetVariable))
                {
                    UpdateVariable(action.VariableScope, action.TargetVariable, action.VariableOperator, ProcessScript(action.VariableValue, sender, fullMessage, trigger, cmd, cmd is RedeemItem ri4 ? ri4.Name : null), cmd);
                }

                if (action.WaitMs > 0)
                {
                    await Task.Delay(action.WaitMs);
                }
            }
        }

        private string GetVariableValue(string scope, string key, IActionableItem? cmd)
        {
            VariableStore store = (scope == "Local" && cmd != null) ? cmd.LocalVariables : Config.GlobalVariables;
            var scalar = store.Scalars.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (scalar != null) return scalar.Value;
            var lst = store.Lists.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (lst != null && lst.Values.Count > 0) return lst.Values.Count.ToString();
            return "0";
        }

        private void UpdateVariable(string scope, string key, string op, string val, IActionableItem? cmd, bool isNumber = false)
        {
            VariableStore store = (scope == "Local" && cmd != null) ? cmd.LocalVariables : Config.GlobalVariables;
            
            if (op == "Add to List")
            {
                var lst = store.Lists.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (lst == null) { lst = new StringListPair { Key = key }; store.Lists.Add(lst); }
                lst.Values.Add(val);
                Config.Save();
                return;
            }
            if (op == "Remove from List")
            {
                var lst = store.Lists.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (lst != null) { lst.Values.Remove(val); Config.Save(); }
                return;
            }

            var scalar = store.Scalars.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (scalar == null) { scalar = new StringPair { Key = key, Value = "0" }; store.Scalars.Add(scalar); }

            if (isNumber) scalar.IsNumber = true;

            if (op == "Set") scalar.Value = val;
            else if (op == "Set Random (Min-Max)")
            {
                var parts = val.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int min) && int.TryParse(parts[1].Trim(), out int max))
                {
                    if (min > max) { int temp = min; min = max; max = temp; }
                    scalar.Value = _rng.Next(min, max + 1).ToString();
                }
            }
            else if (op == "Add") 
            { 
                double sVal = 0;
                double.TryParse(scalar.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out sVal);
                if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double vVal)) 
                    scalar.Value = (sVal + vVal).ToString(System.Globalization.CultureInfo.InvariantCulture); 
            }
            else if (op == "Subtract") 
            { 
                double sVal = 0;
                double.TryParse(scalar.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out sVal);
                if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double vVal)) 
                    scalar.Value = (sVal - vVal).ToString(System.Globalization.CultureInfo.InvariantCulture); 
            }
            
            Config.Save();
        }

        /*
         * HandleRewardRedemption processes incoming channel point reward redemption events, 
         * checks if the redeemed reward matches any configured redeems, and executes the corresponding 
         * actions such as generating random variables, executing action blocks, triggering plugins, and speaking responses via TTS.
         * */
        private async Task HandleRewardRedemption(object? sender, ChannelPointsCustomRewardRedemptionArgs e)
        {
            var ev = e.Payload.Event;
            var redeemConfig = Config.Redeems?.FirstOrDefault(r => r.Id == ev.Reward.Id);
            if (redeemConfig != null && redeemConfig.IsEnabled)
            {
                if (redeemConfig.UseActionBlocks)
                {
                    if (redeemConfig.GenerateRandomVariable && !string.IsNullOrWhiteSpace(redeemConfig.RandomTargetVariable))
                    {
                        double min = redeemConfig.RandomMin;
                        double max = redeemConfig.RandomMax;
                        if (min > max) { double tmp = min; min = max; max = tmp; }
                        string rVal;
                        if (redeemConfig.RandomIsFloat)
                        {
                            double r = min + (_rng.NextDouble() * (max - min));
                            rVal = r.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            int r = _rng.Next((int)min, (int)max + 1);
                            rVal = r.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        }
                        UpdateVariable(redeemConfig.RandomTargetScope, redeemConfig.RandomTargetVariable, "Set", rVal, redeemConfig, true);
                    }
                    await ExecuteActionBlocks(redeemConfig, "", ev.UserName, ev.UserInput ?? "");
                    return;
                }

                if (!string.IsNullOrEmpty(redeemConfig.TriggerPlugin) && redeemConfig.TriggerPlugin != "None")
                {
                    PluginServer.Instance.NotifyTriggerEvent("redeem", redeemConfig.Name, redeemConfig.WebsocketParam, ev.UserName, ev.UserInput ?? "", redeemConfig.TriggerPlugin);
                }
                string userInput = ev.UserInput ?? "";
                if (!string.IsNullOrWhiteSpace(userInput) || !string.IsNullOrWhiteSpace(redeemConfig.FixedText))
                {
                    // For legacy redeems, use FixedText if available, otherwise just use the userInput
                    string scriptToProcess = string.IsNullOrWhiteSpace(redeemConfig.FixedText) ? userInput : redeemConfig.FixedText;

                    string msg = ProcessScript(scriptToProcess, ev.UserName, userInput, "", null, redeemConfig.Name);
                    
                    if (redeemConfig.ReadUserMessage && !string.IsNullOrWhiteSpace(userInput))
                    {
                        // Append user message if it wasn't already referenced via {message}
                        if (!string.IsNullOrWhiteSpace(redeemConfig.FixedText) && !redeemConfig.FixedText.Contains("{message}", StringComparison.OrdinalIgnoreCase))
                        {
                            msg = msg + " " + userInput;
                        }
                    }

                    if (redeemConfig.ShouldReplyInChat)
                    {
                        await SendChatReply(msg, redeemConfig.ReplyAsBot);
                    }

                    AddToHistory(ev.UserName, msg, "Reward");
                    await TtsEngine.Instance.ProcessAndSpeak(msg, "hidden");
                }
            }
        }

        /// <summary>
        /// FIXED: Intercepts all text templates before writing out onto chat payloads, 
        /// recursively swapping out active plugin variables matching the {} layout configuration model rules.
        /// </summary>
        public string ProcessScript(string script, string sender, string fullMessage, string trigger = "", IActionableItem? cmd = null, string? targetOverride = null)
        {
            if (string.IsNullOrEmpty(script)) return fullMessage;

            string res = script;
            
            string[] args = fullMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!string.IsNullOrEmpty(trigger) && args.Length > 0 && args[0].Equals(trigger, StringComparison.OrdinalIgnoreCase))
                args = args.Skip(1).ToArray();

            if (!string.IsNullOrEmpty(sender))
            {
                res = res.Replace("{user}", sender, StringComparison.OrdinalIgnoreCase);
            }
            
            res = res.Replace("{message}", fullMessage ?? "", StringComparison.OrdinalIgnoreCase);

            if (targetOverride != null)
            {
                res = res.Replace("{target}", targetOverride, StringComparison.OrdinalIgnoreCase);
            }
            else if (args.Length > 0)
            {
                res = res.Replace("{target}", string.Join(" ", args), StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                res = res.Replace("{target}", "", StringComparison.OrdinalIgnoreCase);
            }

            res = Regex.Replace(res, @"\{(\d+)\}", m =>
            {
                if (int.TryParse(m.Groups[1].Value, out int idx) && idx >= 1 && idx <= args.Length)
                    return args[idx - 1];
                return "";
            });

            res = Regex.Replace(res, @"\{random:(\d+)-(\d+)\}", m => _rng.Next(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value) + 1).ToString());
            
            // New Variable Engine Evaluator
            res = Regex.Replace(res, @"\{(global|local):([a-zA-Z0-9_\-]+)(:random)?\}", m =>
            {
                string scope = m.Groups[1].Value;
                string key = m.Groups[2].Value;
                bool isRandom = m.Groups[3].Success;

                VariableStore store = (scope.Equals("local", StringComparison.OrdinalIgnoreCase) && cmd != null) ? cmd.LocalVariables : Config.GlobalVariables;
                
                if (isRandom)
                {
                    var lst = store.Lists.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                    if (lst != null && lst.Values.Count > 0) return lst.Values[_rng.Next(lst.Values.Count)];
                    return "";
                }
                else
                {
                    var scalar = store.Scalars.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                    if (scalar != null) return scalar.Value;
                    return "0";
                }
            }, RegexOptions.IgnoreCase);

            // FIXED ATOMIC INTERCEPTOR STEP: Evaluates curly braces elements and replaces them natively via memory dictionary cache map handles
            if (res.Contains("{"))
            {
                res = Regex.Replace(res, @"\{OriginalMessage\}", fullMessage, RegexOptions.IgnoreCase);
                res = Regex.Replace(res, @"\{(?<pluginVar>[a-zA-Z0-9_\-]+)\}", m =>
                {
                    string key = m.Groups["pluginVar"].Value;
                    if (key.EndsWith("_show", StringComparison.OrdinalIgnoreCase))
                    {
                        PluginServer.Instance.NotifyVariableRead(key);
                        return string.Empty;
                    }
                    if (PluginServer.Instance.GlobalVariables.TryGetValue(key, out var dynamicOutputValue))
                    {
                        return dynamicOutputValue;
                    }
                    return m.Value; // Fallback to leave the text untouched if token doesn't match active memory entries
                });
            }

            return res;
        }

        private async Task Subscribe(string type, string sessionId)
        {
            PrepareHttp(Config.Token);
            object condition = (type == "channel.chat.message") ? new { broadcaster_user_id = Config.BroadcasterId, user_id = Config.BroadcasterId } : new { broadcaster_user_id = Config.BroadcasterId };
            var body = new { type, version = "1", condition, transport = new { method = "websocket", session_id = sessionId } };
            await _http.PostAsJsonAsync("https://api.twitch.tv/helix/eventsub/subscriptions", body);
        }

        private void PrepareHttp(string token)
        {
            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _http.DefaultRequestHeaders.Add("Client-Id", ClientId);
        }

        private async Task SendChatReply(string message, bool asBot)
        {
            if (Config.TestModeActive) return;
            string cln = Regex.Replace(message, @"(?<!"")\[.*?\](?!"")", "").Trim();
            if (string.IsNullOrWhiteSpace(cln)) return;
            PrepareHttp(asBot ? Config.BotToken : Config.Token);
            await _http.PostAsJsonAsync("https://api.twitch.tv/helix/chat/messages", new { broadcaster_id = Config.BroadcasterId, sender_id = (asBot ? Config.BotUserId : Config.BroadcasterId), message = cln });
        }

        public async Task<List<RedeemItem>?> GetCustomRewards()
        {
            if (string.IsNullOrEmpty(Config.Token)) return null;
            try
            {
                PrepareHttp(Config.Token);
                var url = $"https://api.twitch.tv/helix/channel_points/custom_rewards?broadcaster_id={Config.BroadcasterId}";
                var resp = await _http.GetFromJsonAsync<JsonElement>(url);
                var list = new List<RedeemItem>();
                if (resp.TryGetProperty("data", out var data))
                    foreach (var item in data.EnumerateArray())
                        list.Add(new RedeemItem { Id = item.GetProperty("id").GetString() ?? "", Name = item.GetProperty("title").GetString() ?? "Unknown", IsEnabled = true });
                return list;
            }
            catch { return null; }
        }

        public async Task PerformAuth(bool bot)
        {
            string scp = bot ? "user:write:chat" : "user:read:chat+channel:read:redemptions+user:write:chat";
            string url = $"https://id.twitch.tv/oauth2/authorize?client_id={ClientId}&redirect_uri={RedirectUri}&response_type=token&scope={scp}&force_verify=true";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            using var lsn = new HttpListener(); lsn.Prefixes.Add(RedirectUri); lsn.Start();
            var ctx = await lsn.GetContextAsync();
            byte[] buf = System.Text.Encoding.UTF8.GetBytes("<html><body><h1>Linked!</h1><script>fetch('/token?access_token=' + new URLSearchParams(window.location.hash.substring(1)).get('access_token'))</script></body></html>");
            ctx.Response.OutputStream.Write(buf, 0, buf.Length); ctx.Response.Close();
            var tctx = await lsn.GetContextAsync();
            string? t = tctx.Request.QueryString["access_token"]; tctx.Response.Close(); lsn.Stop();
            if (!string.IsNullOrEmpty(t)) { await FetchTwitchUser(t, bot); Config.Save(); }
        }

        private async Task FetchTwitchUser(string token, bool isBot)
        {
            PrepareHttp(token);
            var res = await _http.GetStringAsync("https://api.twitch.tv/helix/users");
            using var doc = JsonDocument.Parse(res);
            var data = doc.RootElement.GetProperty("data")[0];
            if (isBot) { Config.BotToken = token; Config.BotUserId = data.GetProperty("id").GetString(); Config.IsBotConnected = true; }
            else { Config.Token = token; Config.BroadcasterId = data.GetProperty("id").GetString(); Config.UserName = data.GetProperty("display_name").GetString(); }
        }

        public void LogUI(string m) => MainWindow.Instance?.Log(m);
        private void AddToHistory(string u, string m, string source) =>
            MainWindow.Instance?.DispatcherQueue?.TryEnqueue(() => {
                History.Insert(0, new TtsEntry(u, m, DateTime.Now.ToString("HH:mm:ss"), source));
                if (History.Count > 100) History.RemoveAt(100);
            });
        #endregion
    }
}

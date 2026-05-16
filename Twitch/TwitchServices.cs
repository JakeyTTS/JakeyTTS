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

            // Arrancar dependencias e infraestructuras satélites
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
                string res = action.Response.Replace("{user}", ev.UserName, StringComparison.OrdinalIgnoreCase).Replace("{bits}", ev.Bits.ToString(), StringComparison.OrdinalIgnoreCase);
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

            string finalMsg = response.Replace("{user}", ev.UserName, StringComparison.OrdinalIgnoreCase).Replace("{months}", cumulativeMonths.ToString(), StringComparison.OrdinalIgnoreCase).Replace("{streak}", streakMonths.ToString(), StringComparison.OrdinalIgnoreCase);
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
                    await TtsEngine.Instance.ProcessAndSpeak(msg, "goals");
                }
            }
        }
        #endregion

        #region Twitch Connectivity
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

        private async Task HandleChatMessage(object? s, ChannelChatMessageArgs e)
        {
            var ev = e.Payload.Event;
            string msg = ev.Message.Text.Trim();
            var cmd = Config.Commands?.FirstOrDefault(c => msg.StartsWith(c.Trigger, StringComparison.OrdinalIgnoreCase));
            if (cmd != null && cmd.IsEnabled)
            {
                string res = ProcessScript(cmd.Response, ev.ChatterUserName, msg, cmd.Trigger);
                if (cmd.ShouldReplyInChat) await SendChatReply(res, cmd.ReplyAsBot);
                AddToHistory(ev.ChatterUserName, res, "Command");
                if (cmd.ShouldSpeak) await TtsEngine.Instance.ProcessAndSpeak(res, "commands");
            }
            else if (Config.ReadChatEnabled)
            {
                if (ev.ChatterUserId == Config.BroadcasterId && !Config.TestModeActive) return;
                AddToHistory(ev.ChatterUserName, msg, "Chat");
                await TtsEngine.Instance.ProcessAndSpeak(msg, "chat");
            }
        }

        private async Task HandleRewardRedemption(object? sender, ChannelPointsCustomRewardRedemptionArgs e)
        {
            var ev = e.Payload.Event;
            var redeemConfig = Config.Redeems?.FirstOrDefault(r => r.Id == ev.Reward.Id);
            if (redeemConfig != null && redeemConfig.IsEnabled)
            {
                string msg = ev.UserInput ?? "";
                if (!string.IsNullOrWhiteSpace(msg)) { AddToHistory(ev.UserName, msg, "Reward"); await TtsEngine.Instance.ProcessAndSpeak(msg, "redeems"); }
            }
        }

        public string ProcessScript(string script, string sender, string fullMessage, string trigger = "")
        {
            if (string.IsNullOrEmpty(script)) return fullMessage;
            string res = script.Replace("{user}", sender, StringComparison.OrdinalIgnoreCase);
            res = Regex.Replace(res, @"\{random:(\d+)-(\d+)\}", m => _rng.Next(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value) + 1).ToString());
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
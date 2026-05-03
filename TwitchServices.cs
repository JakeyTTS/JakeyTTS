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
using System.Threading;
using System.Threading.Tasks;

// Twitch & TTS Libs
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;

namespace JakeyTTS
{
    public class TwitchService
    {
        private static TwitchService _instance;
        public static TwitchService Instance => _instance ??= new TwitchService();

        private const string ClientId = "vf3ugrnhvufgcdc7veyajsah8iw75m";
        private const string RedirectUri = "http://localhost:8888/";
        private static readonly Random _rng = new Random();

        public AppConfig Config { get; set; }
        public EventSubWebsocketClient Client { get; private set; }
        public KokoroTTS TTS { get; private set; }
        public bool IsConnected => Client != null;

        public ObservableCollection<TtsEntry> History { get; } = new();
        private CancellationTokenSource _cts;
        private readonly HttpClient _http = new HttpClient();
        private SemaphoreSlim _ttsSemaphore = new SemaphoreSlim(1, 1);

        private TwitchService()
        {
            Config = AppConfig.Load();

            // Load TTS Model in background
            Task.Run(() => {
                try
                {
                    string modelPath = Path.Combine(AppContext.BaseDirectory, "kokoro-v1.0.onnx");
                    if (!File.Exists(modelPath)) return;
                    TTS = KokoroTTS.LoadModel(modelPath);
                    LogUI("🎙 Kokoro TTS Engine ready.");
                }
                catch (Exception ex) { LogUI($"❌ TTS Error: {ex.Message}"); }
            });
        }

        #region Connection & Events

        public async Task Connect()
        {
            if (string.IsNullOrEmpty(Config.Token))
            {
                LogUI("⚠ Not configured: Please link your account in Settings.");
                return;
            }

            try
            {
                if (Client != null) await Disconnect();
                Client = new EventSubWebsocketClient();

                Client.WebsocketConnected += async (s, e) =>
                {
                    LogUI("✅ Twitch Connection Live.");
                    await Subscribe("channel.chat.message", Client.SessionId);
                    await Subscribe("channel.channel_points_custom_reward_redemption.add", Client.SessionId);
                };

                Client.ChannelChatMessage += async (s, e) =>
                {
                    var ev = e.Payload.Event;
                    string msg = ev.Message.Text.Trim();

                    // 1. Custom Commands Logic (High Priority)
                    var cmd = Config.Commands?.FirstOrDefault(c => msg.StartsWith(c.Trigger, StringComparison.OrdinalIgnoreCase));
                    if (cmd != null && cmd.IsEnabled)
                    {
                        // Anti-loop: don't respond to the bot itself
                        if (ev.ChatterUserId == Config.BotUserId && !Config.TestModeActive) return;

                        string response = ProcessScript(cmd.Response, ev.ChatterUserName, msg, cmd.Trigger);

                        if (cmd.ShouldReplyInChat) await SendChatReply(response, cmd.ReplyAsBot);
                        AddToHistory(ev.ChatterUserName, response);
                        if (cmd.ShouldSpeak) await ProcessAndSpeak(response);
                        return;
                    }

                    // 2. Anti-Echo for General Chat
                    bool isSelf = ev.ChatterUserId == Config.BroadcasterId || ev.ChatterUserId == Config.BotUserId;
                    if (isSelf && !Config.TestModeActive) return;

                    // 3. General Chat Reading
                    if (Config.ReadChatEnabled)
                    {
                        LogUI($"{ev.ChatterUserName}: {msg}");
                        AddToHistory(ev.ChatterUserName, msg);
                        await ProcessAndSpeak(msg);
                    }
                };

                Client.ChannelPointsCustomRewardRedemptionAdd += async (s, e) =>
                {
                    var ev = e.Payload.Event;
                    var redeem = Config.Redeems?.FirstOrDefault(r => r.Id == ev.Reward.Id);

                    if (redeem != null && redeem.IsEnabled)
                    {
                        string script = string.IsNullOrEmpty(redeem.FixedText) ? "{user} redeemed {target}" : redeem.FixedText;
                        string response = ProcessScript(script, ev.UserName, ev.UserInput);

                        if (redeem.ShouldReplyInChat) await SendChatReply(response, redeem.ReplyAsBot);
                        AddToHistory(ev.UserName, response);
                        await ProcessAndSpeak(response);
                    }
                };

                await Client.ConnectAsync();
            }
            catch (Exception ex) { LogUI($"❌ Connection Error: {ex.Message}"); }
        }

        public async Task Disconnect()
        {
            if (Client != null)
            {
                StopCurrentTTS();
                await Client.DisconnectAsync();
                Client = null;
                LogUI("🛑 Service Stopped.");
            }
        }

        private async Task Subscribe(string type, string sessionId)
        {
            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Config.Token);
            _http.DefaultRequestHeaders.Add("Client-Id", ClientId);

            // Chat Message v1 requires user_id
            object condition = (type == "channel.chat.message")
                ? new { broadcaster_user_id = Config.BroadcasterId, user_id = Config.BroadcasterId }
                : new { broadcaster_user_id = Config.BroadcasterId };

            var body = new { type, version = "1", condition, transport = new { method = "websocket", session_id = sessionId } };
            var resp = await _http.PostAsJsonAsync("https://api.twitch.tv/helix/eventsub/subscriptions", body);

            if (!resp.IsSuccessStatusCode)
            {
                string err = await resp.Content.ReadAsStringAsync();
                LogUI($"❌ Subscription failed ({type}): {err}");
            }
        }

        #endregion

        #region Chat & Cleanup

        private async Task SendChatReply(string message, bool asBot)
        {
            if (Config.TestModeActive) return;

            try
            {
                // HIDE [] PARAMS: Strip [tags] unless they are wrapped in "" quotes
                string cleanMsg = Regex.Replace(message, @"(?<!"")\[.*?\](?!"")", "").Trim();
                cleanMsg = Regex.Replace(cleanMsg, @"\s+", " ");

                if (string.IsNullOrWhiteSpace(cleanMsg)) return;

                bool useBot = asBot && Config.IsBotConnected && !string.IsNullOrEmpty(Config.BotToken);
                string token = useBot ? Config.BotToken : Config.Token;
                string senderId = useBot ? Config.BotUserId : Config.BroadcasterId;

                _http.DefaultRequestHeaders.Clear();
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                _http.DefaultRequestHeaders.Add("Client-Id", ClientId);

                var body = new { broadcaster_id = Config.BroadcasterId, sender_id = senderId, message = cleanMsg };
                var response = await _http.PostAsJsonAsync("https://api.twitch.tv/helix/chat/messages", body);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    LogUI($"❌ Chat API Error: {error}");
                }
            }
            catch (Exception ex) { LogUI($"❌ Chat System Exception: {ex.Message}"); }
        }

        #endregion

        #region TTS Engine & Audio Control

        public async Task ProcessAndSpeak(string input)
        {
            if (TTS == null || string.IsNullOrWhiteSpace(input)) return;

            _cts = new CancellationTokenSource();

            await _ttsSemaphore.WaitAsync();
            try
            {
                var voice = KokoroVoiceManager.GetVoice(Config.DefaultVoice ?? "af_bella");
                var matches = Regex.Matches(input, @"\[(?<tag>\w+):\s*(?<val>[\d\.]+)\]|(?<text>[^\[]+)");
                float currentSpeed = 1.0f;

                foreach (Match m in matches)
                {
                    if (_cts.Token.IsCancellationRequested) break;

                    if (m.Groups["tag"].Success)
                    {
                        string tagName = m.Groups["tag"].Value.ToLower();
                        if (tagName == "pause" && int.TryParse(m.Groups["val"].Value, out int ms))
                            await Task.Delay(ms, _cts.Token);
                        else if (tagName == "speed" && float.TryParse(m.Groups["val"].Value, out float s))
                            currentSpeed = s;
                    }
                    else
                    {
                        string segment = m.Groups["text"].Value.Trim();
                        if (string.IsNullOrEmpty(segment)) continue;

                        var tcs = new TaskCompletionSource<bool>();
                        var handle = TTS.SpeakFast(segment, voice, new KokoroTTSPipelineConfig { Speed = currentSpeed });

                        using (_cts.Token.Register(() => tcs.TrySetCanceled()))
                        {
                            handle.OnSpeechCompleted += (p) => tcs.TrySetResult(true);
                            await tcs.Task;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { /* Controlled Stop */ }
            catch (Exception ex) { LogUI($"❌ Audio Error: {ex.Message}"); }
            finally { _ttsSemaphore.Release(); }
        }

        public void StopCurrentTTS()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                try { _ttsSemaphore.Release(); } catch { }
                _ttsSemaphore = new SemaphoreSlim(1, 1);
                LogUI("🛑 Audio cancelled.");
            }
        }

        #endregion

        #region Scripts, History & API

        public string ProcessScript(string script, string sender, string fullMessage, string trigger = "")
        {
            if (string.IsNullOrEmpty(script)) return fullMessage;
            string res = script.Replace("{user}", sender, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(trigger) && fullMessage.StartsWith(trigger, StringComparison.OrdinalIgnoreCase))
            {
                var cleanInput = fullMessage.Substring(trigger.Length).Trim();
                var parts = cleanInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    res = res.Replace($"{{{i + 1}}}", parts[i]);
                    if (i == 0) res = res.Replace("{target}", parts[i]);
                }
            }
            res = res.Replace("{target}", "someone", StringComparison.OrdinalIgnoreCase);

            res = Regex.Replace(res, @"\{random:(\d+)-(\d+)\}", m =>
                _rng.Next(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value) + 1).ToString());

            res = Regex.Replace(res, @"\[choose:(.*?)\]", m => {
                var options = m.Groups[1].Value.Split('|');
                return options[_rng.Next(options.Length)].Trim();
            }, RegexOptions.Singleline);

            return res;
        }

        private void AddToHistory(string user, string message)
        {
            MainWindow.Instance?.DispatcherQueue.TryEnqueue(() =>
            {
                History.Insert(0, new TtsEntry(user, message, DateTime.Now.ToString("HH:mm:ss")));
                if (History.Count > 100) History.RemoveAt(100);
            });
        }

        public async Task<List<RedeemItem>> GetCustomRewards()
        {
            if (string.IsNullOrEmpty(Config.Token)) return null;
            try
            {
                _http.DefaultRequestHeaders.Clear();
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Config.Token);
                _http.DefaultRequestHeaders.Add("Client-Id", ClientId);

                var url = $"https://api.twitch.tv/helix/channel_points/custom_rewards?broadcaster_id={Config.BroadcasterId}";
                var resp = await _http.GetFromJsonAsync<JsonElement>(url);
                var list = new List<RedeemItem>();

                foreach (var item in resp.GetProperty("data").EnumerateArray())
                {
                    list.Add(new RedeemItem
                    {
                        Id = item.GetProperty("id").GetString(),
                        Name = item.GetProperty("title").GetString(),
                        IsEnabled = true,
                        FixedText = "{user} redeemed {target}"
                    });
                }
                return list;
            }
            catch { return null; }
        }

        public async Task PerformAuth(bool isBot)
        {
            string scope = isBot ? "user:write:chat" : "user:read:chat+channel:read:redemptions+user:write:chat";
            string authUrl = $"https://id.twitch.tv/oauth2/authorize?client_id={ClientId}&redirect_uri={RedirectUri}&response_type=token&scope={scope}&force_verify=true";

            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

            using var listener = new HttpListener();
            listener.Prefixes.Add(RedirectUri);
            listener.Start();

            var context = await listener.GetContextAsync();
            string respBody = "<html><body style='font-family:sans-serif;text-align:center;padding-top:50px;'><h1>Linked!</h1><p>You can close this tab.</p><script>fetch('/token?access_token=' + new URLSearchParams(window.location.hash.substring(1)).get('access_token'))</script></body></html>";
            byte[] buf = System.Text.Encoding.UTF8.GetBytes(respBody);
            context.Response.OutputStream.Write(buf, 0, buf.Length);
            context.Response.Close();

            var tokenContext = await listener.GetContextAsync();
            string token = tokenContext.Request.QueryString["access_token"];
            tokenContext.Response.Close();
            listener.Stop();

            if (!string.IsNullOrEmpty(token))
            {
                await FetchTwitchUser(token, isBot);
                Config.Save();
            }
        }

        private async Task FetchTwitchUser(string token, bool isBot)
        {
            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _http.DefaultRequestHeaders.Add("Client-Id", ClientId);

            var userJson = await _http.GetStringAsync("https://api.twitch.tv/helix/users");
            using var doc = JsonDocument.Parse(userJson);
            var userData = doc.RootElement.GetProperty("data")[0];

            if (isBot)
            {
                Config.BotToken = token;
                Config.BotUserId = userData.GetProperty("id").GetString();
                Config.IsBotConnected = true;
                LogUI("🤖 Bot Connected.");
            }
            else
            {
                Config.Token = token;
                Config.BroadcasterId = userData.GetProperty("id").GetString();
                Config.UserName = userData.GetProperty("display_name").GetString();
                LogUI($"💜 Broadcaster Linked: {Config.UserName}");
            }
        }

        #endregion

        private void LogUI(string msg) => MainWindow.Instance?.Log(msg);
    }
}
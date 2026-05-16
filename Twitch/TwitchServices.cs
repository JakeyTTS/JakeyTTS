using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
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
using JakeyTTS.Melodies;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using KokoroSharp.Utilities;
using NAudio.Wave;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;

namespace JakeyTTS.Twitch
{
    public class TwitchService
    {
        private static TwitchService? _instance;
        public static TwitchService Instance => _instance ??= new TwitchService();

        public event EventHandler? ConnectionStateChanged;
        public event EventHandler? TtsEngineReady;

        private const string ClientId = "vf3ugrnhvufgcdc7veyajsah8iw75m";
        private const string RedirectUri = "http://localhost:8888/";
        private static readonly Random _rng = new Random();

        public AppConfig Config { get; set; }
        public EventSubWebsocketClient? Client { get; private set; }
        public KokoroWavSynthesizer? Synthesizer { get; private set; }
        public bool IsConnected => Client != null;
        public ObservableCollection<TtsEntry> History { get; } = new();

        private CancellationTokenSource? _cts;
        private readonly HttpClient _http = new HttpClient();
        private readonly SemaphoreSlim _ttsSemaphore = new SemaphoreSlim(1, 1);

        private TwitchService()
        {
            Config = AppConfig.Load();
            SetupAndLoadTTS();
            MelodyService.Instance.Initialize();

            PluginServer.Instance.Start();
        }

        private void SetupAndLoadTTS()
        {
            Task.Run(() => {
                try
                {
                    string appDataAssets = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JakeyTTS", "assets");
                    string modelPath = Path.Combine(appDataAssets, "kokoro-v1.0.onnx");
                    if (!File.Exists(modelPath)) modelPath = Path.Combine(AppContext.BaseDirectory, "kokoro-v1.0.onnx");

                    if (File.Exists(modelPath))
                    {
                        string voicesPath = Path.Combine(Path.GetDirectoryName(modelPath), "voices");
                        if (Directory.Exists(voicesPath))
                        {
                            KokoroVoiceManager.LoadVoicesFromPath(voicesPath);
                            Synthesizer = new KokoroWavSynthesizer(modelPath);
                            LogUI($"🎙 Kokoro Ready. {KokoroVoiceManager.Voices.Count} voices loaded.");
                            MainWindow.Instance?.DispatcherQueue.TryEnqueue(() => TtsEngineReady?.Invoke(this, EventArgs.Empty));
                        }
                    }
                }
                catch (Exception ex) { LogUI($"❌ TTS Init Error: {ex.Message}"); }
            });
        }

        #region Audio Device Management
        public List<string> GetAudioDevices()
        {
            var devices = new List<string> { "Default System Device", "None" };
            for (int i = 0; i < WaveOut.DeviceCount; i++) devices.Add(WaveOut.GetCapabilities(i).ProductName);
            return devices;
        }

        private List<int> GetActiveDeviceNumbers()
        {
            var names = new List<string> { Config.SelectedAudioDevice, Config.SelectedAudioDevice2, Config.SelectedAudioDevice3 };
            var ids = new List<int>();
            foreach (var name in names.Where(n => n != "None"))
            {
                if (name == "Default System Device") ids.Add(-1);
                else
                {
                    for (int i = 0; i < WaveOut.DeviceCount; i++)
                        if (WaveOut.GetCapabilities(i).ProductName == name) { ids.Add(i); break; }
                }
            }
            return ids.Distinct().ToList();
        }
        #endregion

        #region Audio Processing Effects (Technical Documentation)

        private byte[] ReverseAudio(byte[] data)
        {
            if (data == null || data.Length < 2) return data;
            int sampleCount = data.Length / 2;
            byte[] reversed = new byte[data.Length];
            for (int i = 0; i < sampleCount; i++)
            {
                int srcIdx = i * 2;
                int destIdx = (sampleCount - 1 - i) * 2;
                reversed[destIdx] = data[srcIdx];
                reversed[destIdx + 1] = data[srcIdx + 1];
            }
            return reversed;
        }

        private byte[] ApplyRobotEffect(byte[] data)
        {
            if (data == null || data.Length < 2) return data;
            int sampleCount = data.Length / 2;
            byte[] processed = new byte[data.Length];
            double frequency = 50.0;
            double sampleRate = 24000.0;
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(data, i * 2);
                double modulation = Math.Sin(2.0 * Math.PI * frequency * (i / sampleRate));
                short robotSample = (short)(sample * modulation);
                byte[] bytes = BitConverter.GetBytes(robotSample);
                processed[i * 2] = bytes[0]; processed[i * 2 + 1] = bytes[1];
            }
            return processed;
        }

        private byte[] ApplyEchoEffect(byte[] data, int delayMs)
        {
            if (data == null || data.Length < 2 || delayMs <= 0) return data;
            int sampleRate = 24000;
            int delaySamples = (delayMs * sampleRate) / 1000;
            float decay = 0.45f;
            int extraBuffer = delaySamples * 2;
            byte[] processed = new byte[data.Length + extraBuffer];
            int originalSampleCount = data.Length / 2;
            for (int i = 0; i < (processed.Length / 2); i++)
            {
                short original = (i < originalSampleCount) ? BitConverter.ToInt16(data, i * 2) : (short)0;
                short echo = (i >= delaySamples && (i - delaySamples) < originalSampleCount)
                    ? (short)(BitConverter.ToInt16(data, (i - delaySamples) * 2) * decay) : (short)0;
                short mixed = (short)Math.Clamp(original + echo, short.MinValue, short.MaxValue);
                byte[] bytes = BitConverter.GetBytes(mixed);
                processed[i * 2] = bytes[0]; processed[i * 2 + 1] = bytes[1];
            }
            return processed;
        }

        private async Task PlayWavData(byte[] data, float volume, float pitchMultiplier, Melody? activeMelody)
        {
            var deviceIds = GetActiveDeviceNumbers();
            var players = new List<WaveOutEvent>();
            var format = new WaveFormat(24000, 16, 1);

            foreach (int id in deviceIds)
            {
                try
                {
                    var waveOut = new WaveOutEvent { DeviceNumber = id };
                    IWaveProvider provider = activeMelody != null
                        ? new MelodyWaveProvider(data, format, activeMelody)
                        : new RawSourceWaveStream(new MemoryStream(data), new WaveFormat((int)(24000 * pitchMultiplier), 16, 1));

                    waveOut.Init(provider);
                    waveOut.Volume = volume;
                    players.Add(waveOut);
                    waveOut.Play();
                }
                catch { }
            }

            while (players.Any(p => p.PlaybackState == PlaybackState.Playing) && !_cts!.IsCancellationRequested)
                await Task.Delay(50);

            foreach (var p in players) { p.Stop(); p.Dispose(); }
        }

        public async Task PlaySoundEffect(string tagName)
        {
            var effect = Config.SoundEffects?.FirstOrDefault(e => e.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase) && e.IsEnabled);
            if (effect == null || !File.Exists(effect.FullPath)) return;
            try
            {
                using var audioFile = new AudioFileReader(effect.FullPath);
                var deviceIds = GetActiveDeviceNumbers();
                var players = new List<WaveOutEvent>();
                foreach (int id in deviceIds)
                {
                    var waveOut = new WaveOutEvent { DeviceNumber = id };
                    waveOut.Init(audioFile);
                    waveOut.Volume = Config.GlobalVolume;
                    players.Add(waveOut);
                    waveOut.Play();
                }
                while (players.Any(p => p.PlaybackState == PlaybackState.Playing) && !(_cts?.IsCancellationRequested ?? false))
                    await Task.Delay(50);
                foreach (var p in players) { p.Stop(); p.Dispose(); }
            }
            catch { }
        }

        private KokoroVoice? ResolveVoice(string name)
        {
            var mixCfg = Config.MixedVoices?.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && m.IsEnabled);
            if (mixCfg != null && mixCfg.Components.Any())
            {
                var comps = mixCfg.Components
                    .Select(c => (voice: KokoroVoiceManager.Voices.FirstOrDefault(v => v.Name == c.VoiceName), weight: c.Weight))
                    .Where(x => x.voice != null).Select(x => (x.voice!, x.weight)).ToArray();
                return comps.Any() ? KokoroVoiceManager.Mix(comps) : null;
            }
            return KokoroVoiceManager.Voices.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        #endregion

        #region TTS Engine & Tag Handler

        public async Task<byte[]?> SynthesizeSilentAsync(string text, string voiceName, float speed = 1.0f)
        {
            if (Synthesizer == null || string.IsNullOrWhiteSpace(text)) return null;

            await _ttsSemaphore.WaitAsync();
            try
            {
                var voice = ResolveVoice(voiceName) ?? KokoroVoiceManager.Voices.FirstOrDefault();
                if (voice == null) return null;

                return Synthesizer.Synthesize(text, voice, new KokoroTTSPipelineConfig { Speed = speed });
            }
            catch (Exception ex)
            {
                LogUI($"❌ Silent TTS Error: {ex.Message}");
                return null;
            }
            finally
            {
                _ttsSemaphore.Release();
            }
        }

        public async Task ProcessAndSpeak(string input, string scope = "chat")
        {
            if (Synthesizer == null || string.IsNullOrWhiteSpace(input)) return;

            var allVoices = KokoroVoiceManager.Voices;
            if (allVoices == null || !allVoices.Any()) return;

            _cts = new CancellationTokenSource();
            await _ttsSemaphore.WaitAsync();

            try
            {
                var defaultVoiceName = Config.DefaultVoice;
                var baseVoice = allVoices.FirstOrDefault(v => v.Name.Equals(defaultVoiceName, StringComparison.OrdinalIgnoreCase)) ?? allVoices.First();

                KokoroVoice currentActiveVoice = baseVoice;
                float currentSpeed = 1.0f;
                float currentVolume = Config.GlobalVolume;
                float currentPitch = 1.0f;
                bool currentReverse = false;
                bool currentRobot = false;
                int currentEcho = 0;
                Melody? currentMelody = null;

                var matches = Regex.Matches(input, @"\[(?<tag>\w+)(?:(?<sep>[:+])(?<val>[\w\.-]+))?\]|(?<text>[^\[]+)");

                foreach (Match m in matches)
                {
                    if (_cts.Token.IsCancellationRequested) break;
                    if (m.Groups["tag"].Success)
                    {
                        string tagName = m.Groups["tag"].Value.ToLower();
                        string val = m.Groups["val"].Value;

                        var sfx = Config.SoundEffects.FirstOrDefault(e => e.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase));
                        if (sfx != null && sfx.IsEnabled) { await PlaySoundEffect(tagName); continue; }

                        if (tagName == "mix" || tagName == "voice")
                        {
                            var newVoice = ResolveVoice(val);
                            if (newVoice != null) currentActiveVoice = newVoice;
                        }
                        else
                        {
                            HandleTag(m, ref currentActiveVoice, baseVoice, ref currentSpeed, ref currentVolume, ref currentReverse, ref currentPitch, ref currentRobot, ref currentEcho, ref currentMelody);
                        }
                    }
                    else
                    {
                        string segment = m.Groups["text"].Value.Trim();
                        if (string.IsNullOrEmpty(segment)) continue;

                        byte[] wavData = Synthesizer.Synthesize(segment, currentActiveVoice, new KokoroTTSPipelineConfig { Speed = currentSpeed });
                        if (currentRobot) wavData = ApplyRobotEffect(wavData);
                        if (currentEcho > 0) wavData = ApplyEchoEffect(wavData, currentEcho);
                        if (currentReverse) wavData = ReverseAudio(wavData);

                        _ = PluginServer.Instance.BroadcastEventAsync(scope, segment, wavData);

                        await PlayWavData(wavData, currentVolume, currentPitch, currentMelody);
                    }
                }
            }
            catch (Exception ex) { LogUI($"❌ Audio Error: {ex.Message}"); }
            finally { _ttsSemaphore.Release(); }
        }

        private void HandleTag(Match m, ref KokoroVoice activeVoice, KokoroVoice baseVoice, ref float speed, ref float vol, ref bool reverse, ref float pitch, ref bool robot, ref int echo, ref Melody? melody)
        {
            string tagName = m.Groups["tag"].Value.ToLower();
            string rawVal = m.Groups["val"].Value;
            float.TryParse(rawVal, NumberStyles.Float, CultureInfo.InvariantCulture, out float numVal);

            switch (tagName)
            {
                case "normal":
                case "reset":
                    activeVoice = baseVoice;
                    speed = 1.0f; vol = Config.GlobalVolume; reverse = false; pitch = 1.0f; robot = false; echo = 0; melody = null;
                    break;
                case "whisper": vol = 0.2f; speed = 0.8f; break;
                case "volume": vol = Math.Clamp((numVal > 1.1f) ? (numVal / 1000f) : numVal, 0f, 1f); break;
                case "speed": speed = (numVal > 5) ? (numVal / 1000f) : numVal; if (speed <= 0.1f) speed = 1.0f; break;
                case "pause":
                    int ms = (m.Groups["sep"].Value == "+") ? (int)(numVal * 1000) : (int)numVal;
                    if (ms > 0) Task.Delay(ms).Wait(); break;
                case "reverse": reverse = !reverse; break;
                case "high": pitch = (numVal > 0) ? numVal : 1.5f; break;
                case "deep": pitch = (numVal > 0) ? numVal : 0.7f; break;
                case "pitch": pitch = (numVal > 0) ? numVal : 1.0f; break;
                case "robot": robot = !robot; break;
                case "echo": echo = (numVal > 0) ? (int)numVal : 150; break;
                case "melody":
                    melody = string.IsNullOrEmpty(rawVal) ? null : MelodyService.Instance.Melodies.FirstOrDefault(x => x.Name.Equals(rawVal, StringComparison.OrdinalIgnoreCase) && x.IsEnabled);
                    break;
            }
        }
        #endregion

        #region User Actions Logic

        #region User Actions Logic

        private async Task HandleCheer(object? s, ChannelCheerArgs e)
        {
            var ev = e.Payload.Event;
            if (Config.UserActions?.BitActions == null) return;

            var action = Config.UserActions.BitActions
                .Where(a => a.IsEnabled && ev.Bits >= a.Threshold)
                .OrderByDescending(a => a.Threshold)
                .FirstOrDefault();

            if (action != null)
            {
                string res = action.Response
                    .Replace("{user}", ev.UserName, StringComparison.OrdinalIgnoreCase)
                    .Replace("{bits}", ev.Bits.ToString(), StringComparison.OrdinalIgnoreCase);

                AddToHistory(ev.UserName, $"{ev.Bits} bits", "Bits");

                // Hablar la alerta inicial con las propiedades estéticas del streamer
                await ProcessAndSpeak(res, "bits");

                // Si se debe leer el mensaje del usuario, limpiamos sus etiquetas y aplicamos el reset
                if (action.ShouldPlayUserMessage && !string.IsNullOrWhiteSpace(ev.Message))
                {
                    // SANITIZACIÓN: Elimina cualquier corchete [...] que haya escrito el usuario
                    string cleanUserMessage = Regex.Replace(ev.Message, @"\[.*?\]", "").Trim();

                    if (!string.IsNullOrWhiteSpace(cleanUserMessage))
                    {
                        // Forzamos el reinicio de audio y la pausa intermedia de 500ms
                        await ProcessAndSpeak($"[reset][pause:500] {cleanUserMessage}", "bits");
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

            var streakAction = Config.UserActions.StreakActions?
                .Where(a => a.IsEnabled && streakMonths >= a.Threshold)
                .OrderByDescending(a => a.Threshold)
                .FirstOrDefault();

            var subAction = Config.UserActions.SubActions?
                .Where(a => a.IsEnabled && cumulativeMonths >= a.Threshold)
                .OrderByDescending(a => a.Threshold)
                .FirstOrDefault();

            string response = "{user} subscribed for {months} months!";
            bool shouldPlayText = true;

            if (streakAction != null)
            {
                response = streakAction.Response;
                shouldPlayText = streakAction.ShouldPlayUserMessage;
            }
            else if (subAction != null)
            {
                response = subAction.Response;
                shouldPlayText = subAction.ShouldPlayUserMessage;
            }

            string finalMsg = response
                .Replace("{user}", ev.UserName, StringComparison.OrdinalIgnoreCase)
                .Replace("{months}", cumulativeMonths.ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace("{streak}", streakMonths.ToString(), StringComparison.OrdinalIgnoreCase);

            AddToHistory(ev.UserName, "Subscription", "Sub");

            await ProcessAndSpeak(finalMsg, "subs");

            string userWrittenText = ev.Message?.Text;
            if (shouldPlayText && !string.IsNullOrWhiteSpace(userWrittenText))
            {
                string cleanUserMessage = Regex.Replace(userWrittenText, @"\[.*?\]", "").Trim();

                if (!string.IsNullOrWhiteSpace(cleanUserMessage))
                {
                    await ProcessAndSpeak($"[reset][pause:500] {cleanUserMessage}", "subs");
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

                if (goalType.Contains("sub"))
                    responseTemplate = Config.UserActions.SubGoalReachedResponse;
                else if (goalType.Contains("follow"))
                    responseTemplate = Config.UserActions.FollowerGoalReachedResponse;
                else if (goalType.Contains("bit"))
                    responseTemplate = Config.UserActions.BitsGoalReachedResponse;
                else if (goalType.Contains("point"))
                    responseTemplate = Config.UserActions.PointsGoalReachedResponse;
                else
                    responseTemplate = "Goal {goal_title} completed!";

                if (!string.IsNullOrEmpty(responseTemplate))
                {
                    string msg = responseTemplate.Replace("{goal_title}", ev.Description, StringComparison.OrdinalIgnoreCase);
                    await ProcessAndSpeak(msg, "goals");
                }
            }
        }
        #endregion

        #region Twitch Connectivity (Auth Logic Restored)

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
                StopCurrentTTS();
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
                if (cmd.ShouldSpeak) await ProcessAndSpeak(res, "commands");
            }
            else if (Config.ReadChatEnabled)
            {
                if (ev.ChatterUserId == Config.BroadcasterId && !Config.TestModeActive) return;
                AddToHistory(ev.ChatterUserName, msg, "Chat");
                await ProcessAndSpeak(msg, "chat");
            }
        }

        private async Task HandleRewardRedemption(object? sender, ChannelPointsCustomRewardRedemptionArgs e)
        {
            var ev = e.Payload.Event;
            var redeemConfig = Config.Redeems?.FirstOrDefault(r => r.Id == ev.Reward.Id);
            if (redeemConfig != null && redeemConfig.IsEnabled)
            {
                string msg = ev.UserInput ?? "";
                if (!string.IsNullOrWhiteSpace(msg)) { AddToHistory(ev.UserName, msg, "Reward"); await ProcessAndSpeak(msg, "redeems"); }
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

        public void StopCurrentTTS() => _cts?.Cancel();
        public void LogUI(string m) => MainWindow.Instance?.Log(m);
        private void AddToHistory(string u, string m, string source) =>
            MainWindow.Instance?.DispatcherQueue?.TryEnqueue(() => {
                History.Insert(0, new TtsEntry(u, m, DateTime.Now.ToString("HH:mm:ss"), source));
                if (History.Count > 100) History.RemoveAt(100);
            });
        #endregion

        private class MelodyWaveProvider : IWaveProvider
        {
            private readonly byte[] _sourceData;
            private readonly WaveFormat _format;
            private readonly Melody _melody;
            private double _sourcePosition = 0;
            public MelodyWaveProvider(byte[] data, WaveFormat format, Melody melody) { _sourceData = data; _format = format; _melody = melody; }
            public WaveFormat WaveFormat => _format;
            public int Read(byte[] buffer, int offset, int count)
            {
                int sampleCount = count / 2; int bytesRead = 0; int sourceSamples = _sourceData.Length / 2;
                for (int i = 0; i < sampleCount; i++)
                {
                    if (_sourcePosition >= sourceSamples - 2) break;
                    float progress = (float)(_sourcePosition / sourceSamples);
                    float pitchMultiplier = MelodyService.Instance.GetPitchAt(_melody, progress);
                    int index = (int)_sourcePosition; float frac = (float)(_sourcePosition - index);
                    short s1 = BitConverter.ToInt16(_sourceData, index * 2);
                    short s2 = BitConverter.ToInt16(_sourceData, (index + 1) * 2);
                    short sample = (short)(s1 + frac * (s2 - s1));
                    byte[] bytes = BitConverter.GetBytes(sample);
                    buffer[offset + bytesRead] = bytes[0]; buffer[offset + bytesRead + 1] = bytes[1];
                    _sourcePosition += pitchMultiplier; bytesRead += 2;
                }
                return bytesRead;
            }
        }
    }
        #endregion
}
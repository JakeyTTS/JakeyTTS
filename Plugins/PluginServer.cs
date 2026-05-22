using JakeyTTS.Core;
using JakeyTTS.Views;
using JakeyTTS.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JakeyTTS
{
    public class PluginServer
    {
        private static PluginServer? _instance;
        public static PluginServer Instance => _instance ??= new PluginServer();

        private readonly HttpListener _listener = new HttpListener();
        private readonly ConcurrentDictionary<string, WebSocket> _activeClients = new();
        private readonly ConcurrentDictionary<string, string> _pluginUiManifests = new();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private readonly ConcurrentDictionary<string, WebSocket> _audioTagHandlers = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pendingAudioRoutings = new();

        private readonly ConcurrentDictionary<string, Process> _localPluginProcesses = new();

        public ConcurrentDictionary<string, string> GlobalVariables { get; } = new ConcurrentDictionary<string, string>();

        public void Start(int port = 8889)
        {
            // STEP 1: Pre-boot environment flush clears out running zombie extension tasks 
            ForceKillRoguePluginProcesses();

            try
            {
                if (_listener.IsListening)
                {
                    MainWindow.Instance?.Log($"ℹ️ WebSocket Server is already active on port {port}.");
                    return;
                }

                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
                MainWindow.Instance?.Log($"🔌 Plugin WebSocket Server started on ws://localhost:{port}/");

                // STEP 2: Boot configured local extension binaries natively
                LaunchLocalPlugins();

                Task.Run(AcceptConnectionsAsync);
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.Log($"❌ Plugin Server Bind Error: {ex.Message}");
                LaunchLocalPlugins();
            }
        }

        public void Stop()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            KillLocalPlugins();
        }

        public void LaunchLocalPlugins()
        {
            var plugins = TwitchService.Instance.Config.Plugins;
            if (plugins == null) return;

            foreach (var plugin in plugins)
            {
                if (plugin.IsEnabled && !string.IsNullOrWhiteSpace(plugin.ExecutablePath))
                {
                    LaunchPluginProcess(plugin);
                }
            }
        }

        public bool LaunchPluginProcess(PluginItem plugin)
        {
            if (_localPluginProcesses.ContainsKey(plugin.Id)) return true;

            if (string.IsNullOrWhiteSpace(plugin.ExecutablePath) || !File.Exists(plugin.ExecutablePath))
            {
                MainWindow.Instance?.Log($"⚠️ Cannot launch plugin '{plugin.Name}': Executable path is blank or file does not exist.");
                return false;
            }

            try
            {
                string absolutePath = Path.GetFullPath(plugin.ExecutablePath);
                string? workingDir = Path.GetDirectoryName(absolutePath);

                if (string.IsNullOrEmpty(workingDir))
                {
                    workingDir = AppContext.BaseDirectory;
                }

                // FIXED: Hardcoded to FALSE and NORMAL to bypass stale database properties.
                // This guarantees the extension process launches with full interactive UI views visible on top.
                var startInfo = new ProcessStartInfo
                {
                    FileName = absolutePath,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                Process? proc = Process.Start(startInfo);
                if (proc != null)
                {
                    _localPluginProcesses[plugin.Id] = proc;
                    MainWindow.Instance?.Log($"🚀 Native process spawned for: {plugin.Name} (Invisible: False)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.Log($"❌ System Process exception for [{plugin.Name}]: {ex.Message}");
            }
            return false;
        }

        public void KillPluginProcess(string pluginId)
        {
            if (_localPluginProcesses.TryRemove(pluginId, out var proc))
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill();
                        proc.WaitForExit(1000);
                        proc.Dispose();
                    }
                    MainWindow.Instance?.Log($"🛑 Terminated process for plugin ID: {pluginId}");
                }
                catch { }
            }
        }

        public void KillLocalPlugins()
        {
            var activeIds = _localPluginProcesses.Keys.ToList();
            foreach (var id in activeIds) KillPluginProcess(id);
        }

        private void ForceKillRoguePluginProcesses()
        {
            try
            {
                string[] targetPlugins = { "DiapStash_Plugin", "bridge" };

                foreach (var name in targetPlugins)
                {
                    var processes = Process.GetProcessesByName(name);
                    if (processes.Length > 0)
                    {
                        MainWindow.Instance?.Log($"🧯 Cleared {processes.Length} ghost background thread resource(s) for task: '{name}'.");
                        foreach (var proc in processes)
                        {
                            try
                            {
                                proc.Kill();
                                proc.WaitForExit(1000);
                                proc.Dispose();
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private async Task AcceptConnectionsAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        var wsContext = await context.AcceptWebSocketAsync(null);
                        _ = HandleClientAsync(wsContext.WebSocket);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
                catch (HttpListenerException) { /* Ignored on shutdown */ }
                catch (Exception) { /* Maintain connection loop stability */ }
            }
        }

        private async Task HandleClientAsync(WebSocket webSocket)
        {
            var chunkBuffer = new byte[1024 * 16];
            string pluginId = string.Empty;

            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(chunkBuffer), CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                            return;
                        }

                        ms.Write(chunkBuffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    string message = Encoding.UTF8.GetString(ms.ToArray());
                    if (string.IsNullOrWhiteSpace(message)) continue;

                    using var doc = JsonDocument.Parse(message);
                    var root = doc.RootElement;
                    string type = root.GetProperty("type").GetString() ?? "";

                    if (type == "register")
                    {
                        var payload = root.GetProperty("payload");
                        pluginId = payload.GetProperty("id").GetString()!;
                        _activeClients[pluginId] = webSocket;
                        HandleRegistration(payload);
                    }
                    // TTS execution routines...
                    else if (type == "tts_request" || type == "speak_request")
                    {
                        if (IsPluginEnabled(pluginId))
                        {
                            await HandleTtsRequest(pluginId, root, webSocket);
                        }
                        else
                        {
                            await SendJsonAsync(webSocket, new { type = "error", message = "Plugin access not approved or is currently disabled." });
                        }
                    }
                    else if (type == "audio_processed")
                    {
                        string reqId = root.GetProperty("request_id").GetString()!;
                        string base64Audio = root.GetProperty("payload").GetProperty("pcm_base64").GetString()!;

                        if (_pendingAudioRoutings.TryRemove(reqId, out var tcs))
                        {
                            tcs.SetResult(Convert.FromBase64String(base64Audio));
                        }
                    }
                    else if (IsPluginEnabled(pluginId))
                    {
                        await ProcessDynamicExtensionPacket(pluginId, type, root, webSocket);
                    }
                }
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.Log($"🔌 WebSocket Client Disconnected: {ex.Message}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(pluginId))
                {
                    _activeClients.TryRemove(pluginId, out _);

                    // FIXED: Thread-safe iteration selection loops map data mapping without triggering KeyNotFoundException crashes
                    var keysToRemove = _audioTagHandlers
                        .Where(kvp => kvp.Value == webSocket)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in keysToRemove)
                    {
                        _audioTagHandlers.TryRemove(key, out _);
                    }
                }

                if (webSocket != null)
                {
                    if (webSocket.State != WebSocketState.Closed && webSocket.State != WebSocketState.Aborted)
                    {
                        try { webSocket.Dispose(); } catch { }
                    }
                }
            }
        }

        private async Task ProcessDynamicExtensionPacket(string pluginId, string type, JsonElement root, WebSocket ws)
        {
            var payload = root.TryGetProperty("payload", out var p) ? p : default;

            switch (type)
            {
                case "set_global_variable":
                    var varName = payload.GetProperty("variable_name").GetString()!;
                    var varValue = payload.GetProperty("variable_value").GetString()!;
                    GlobalVariables[varName] = varValue;
                    break;

                case "inject_command":
                    var cmdTrigger = payload.GetProperty("trigger").GetString()!;
                    var cmdResponse = payload.GetProperty("response").GetString()!;
                    MainWindow.Instance?.DispatcherQueue.TryEnqueue(() => {
                        if (!TwitchService.Instance.Config.Commands.Any(c => c.Trigger == cmdTrigger))
                        {
                            TwitchService.Instance.Config.Commands.Add(new CommandItem
                            {
                                Trigger = cmdTrigger,
                                Response = cmdResponse,
                                IsEnabled = true,
                                ShouldSpeak = true
                            });
                            TwitchService.Instance.Config.Save();
                        }
                    });
                    break;

                case "inject_sfx":
                    var sfxTag = payload.GetProperty("tag_name").GetString()!;
                    var sfxFile = payload.GetProperty("file_name").GetString()!;
                    MainWindow.Instance?.DispatcherQueue.TryEnqueue(() => {
                        if (!TwitchService.Instance.Config.SoundEffects.Any(s => s.TagName == sfxTag))
                        {
                            TwitchService.Instance.Config.SoundEffects.Add(new SoundEffectItem
                            {
                                TagName = sfxTag,
                                FileName = sfxFile,
                                IsEnabled = true
                            });
                            TwitchService.Instance.Config.Save();
                        }
                    });
                    break;

                case "inject_tag":
                    var customTag = payload.GetProperty("tag_name").GetString()!.ToLower();
                    _audioTagHandlers[customTag] = ws;

                    TtsEngine.Instance.CustomTags[customTag] = (val, context) => {
                        context["ActiveDspTag"] = customTag;
                        _ = SendJsonAsync(ws, new { type = "tag_triggered", tag = customTag, value = val });
                    };

                    TtsEngine.Instance.CustomTags[customTag + "reset"] = (val, context) => {
                        context["ActiveDspTag"] = "none";
                        _ = SendJsonAsync(ws, new { type = "tag_triggered", tag = customTag + "reset", value = val });
                    };

                    if (TtsEngine.Instance.AudioModifiers.Count == 0)
                    {
                        TtsEngine.Instance.AudioModifiers.Add((rawPcm, context) => {
                            if (context.TryGetValue("ActiveDspTag", out var tagObj) && tagObj is string activeTag && _audioTagHandlers.TryGetValue(activeTag, out var handlerWs))
                            {
                                try
                                {
                                    string routingId = Guid.NewGuid().ToString();
                                    var tcs = new TaskCompletionSource<byte[]>();
                                    _pendingAudioRoutings[routingId] = tcs;

                                    _ = SendJsonAsync(handlerWs, new
                                    {
                                        type = "process_audio",
                                        request_id = routingId,
                                        payload = new { pcm_base64 = Convert.ToBase64String(rawPcm) }
                                    });

                                    if (Task.WaitAll(new Task[] { tcs.Task }, 1500))
                                    {
                                        return tcs.Task.Result;
                                    }
                                    MainWindow.Instance?.Log($"⚠️ DSP Middleware timeout for tag [{activeTag}]. Bypassing filter.");
                                }
                                catch { }
                            }
                            return rawPcm;
                        });
                    }
                    MainWindow.Instance?.Log($"🛡 External decoupled DSP Routing registered for tag: [{customTag}]");
                    break;

                case "inject_ui_page":
                    var webUrl = payload.GetProperty("embed_url").GetString()!;
                    _pluginUiManifests[pluginId] = webUrl;

                    MainWindow.Instance?.DispatcherQueue.TryEnqueue(() => {
                        MainWindow.Instance?.NotifyDynamicUiRegistered(pluginId, PluginNameFromId(pluginId), webUrl);
                    });
                    break;
            }
            await Task.CompletedTask;
        }

        private string PluginNameFromId(string id)
        {
            var p = TwitchService.Instance.Config.Plugins?.FirstOrDefault(x => x.Id == id);
            return p != null ? p.Name : "External Plugin";
        }

        private async Task HandleTtsRequest(string pluginId, JsonElement root, WebSocket ws)
        {
            string reqId = root.TryGetProperty("request_id", out var idProp) ? idProp.GetString()! : Guid.NewGuid().ToString();
            var payload = root.GetProperty("payload");
            string text = payload.GetProperty("text").GetString()!;

            string voice = TwitchService.Instance.Config.DefaultVoice;
            byte[]? wavData = await TtsEngine.Instance.SynthesizeSilentAsync(text, voice);

            if (wavData != null)
            {
                await SendJsonAsync(ws, new
                {
                    type = "tts_response",
                    request_id = reqId,
                    payload = new { audio_base64 = Convert.ToBase64String(wavData) }
                });
            }
        }

        private void HandleRegistration(JsonElement payload)
        {
            string id = payload.GetProperty("id").GetString()!;
            string name = payload.GetProperty("name").GetString()!;
            string version = payload.GetProperty("version").GetString() ?? "1.0";
            string protocol = payload.GetProperty("protocol_version").GetString() ?? "1.0";
            string icon = payload.TryGetProperty("icon", out var iconProp) ? iconProp.GetString() ?? "" : "";

            string path = payload.TryGetProperty("executable_path", out var pathProp) ? pathProp.GetString() ?? "" : "";
            bool invisible = payload.TryGetProperty("launch_invisible", out var invProp) && invProp.GetBoolean();

            var subs = new List<string>();
            if (payload.TryGetProperty("subscriptions", out var subsProp))
            {
                foreach (var sub in subsProp.EnumerateArray())
                    subs.Add(sub.GetString()!);
            }

            var triggers = new List<string>();
            if (payload.TryGetProperty("triggers", out var triggersProp))
            {
                foreach (var trig in triggersProp.EnumerateArray())
                    triggers.Add(trig.GetString()!);
            }

            var config = TwitchService.Instance.Config;
            var existing = config.Plugins?.FirstOrDefault(p => p.Id == id);

            if (existing == null)
            {
                MainWindow.Instance?.DispatcherQueue.TryEnqueue(() => {
                    if (config.Plugins == null) config.Plugins = new();

                    config.Plugins.Add(new PluginItem
                    {
                        Id = id,
                        Name = name,
                        Version = version,
                        ProtocolVersion = protocol,
                        IconBase64 = icon,
                        ExecutablePath = path,
                        LaunchInvisible = invisible,
                        Subscriptions = subs,
                        Triggers = triggers,
                        IsEnabled = false
                    });
                    config.Save();
                });
                MainWindow.Instance?.Log($"🔔 New Plugin requested access: {name}");
            }
            else
            {
                MainWindow.Instance?.DispatcherQueue.TryEnqueue(() => {
                    existing.Name = name;
                    existing.Version = version;
                    existing.ProtocolVersion = protocol;
                    existing.IconBase64 = icon;
                    if (!string.IsNullOrWhiteSpace(path)) existing.ExecutablePath = path;
                    existing.LaunchInvisible = invisible;
                    existing.Subscriptions = subs;
                    existing.Triggers = triggers;
                    config.Save();
                });
            }

            bool isApproved = existing?.IsEnabled ?? false;
            if (_activeClients.ContainsKey(id))
            {
                _ = SendJsonAsync(_activeClients[id], new { type = "auth_status", approved = isApproved });
            }
        }

        private bool IsPluginEnabled(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return TwitchService.Instance.Config.Plugins?.Any(p => p.Id == id && p.IsEnabled) ?? false;
        }

        public async Task BroadcastEventAsync(string scope, string text, byte[] wavData)
        {
            if (wavData == null || wavData.Length == 0) return;

            string base64Audio = Convert.ToBase64String(wavData);

            var message = new
            {
                type = "event_broadcast",
                scope = scope,
                payload = new
                {
                    text = text,
                    audio_base64 = base64Audio
                }
            };

            foreach (var client in _activeClients)
            {
                var pluginCfg = TwitchService.Instance.Config.Plugins?.FirstOrDefault(p => p.Id == client.Key);

                if (pluginCfg != null && pluginCfg.IsEnabled && pluginCfg.Subscriptions.Contains(scope))
                {
                    if (client.Value.State == WebSocketState.Open)
                    {
                        await SendJsonAsync(client.Value, message);
                    }
                }
            }
        }

        public void NotifyVariableRead(string variableName)
        {
            var msg = new { type = "variable_triggered", variable = variableName };

            foreach (var client in _activeClients)
            {
                if (client.Value.State == WebSocketState.Open)
                {
                    _ = SendJsonAsync(client.Value, msg);
                }
            }
        }

        public void NotifyTriggerEvent(string source, string name, string param, string user, string message, string targetPluginId)
        {
            if (string.IsNullOrEmpty(targetPluginId) || targetPluginId.Equals("None", StringComparison.OrdinalIgnoreCase))
                return;

            string scope = source == "command" ? "commands" : "redeems";
            bool isAll = targetPluginId.Equals("All", StringComparison.OrdinalIgnoreCase);

            foreach (var client in _activeClients)
            {
                var pluginCfg = TwitchService.Instance.Config.Plugins?.FirstOrDefault(p => p.Id == client.Key);
                if (pluginCfg == null || !pluginCfg.IsEnabled || !pluginCfg.Subscriptions.Contains(scope))
                    continue;

                bool isTarget = isAll || client.Key.Equals(targetPluginId, StringComparison.OrdinalIgnoreCase) || 
                                (pluginCfg.Triggers != null && pluginCfg.Triggers.Contains(targetPluginId));

                if (isTarget)
                {
                    if (client.Value.State == WebSocketState.Open)
                    {
                        var msg = new
                        {
                            type = "trigger_event",
                            payload = new
                            {
                                source = source,
                                name = name,
                                trigger = targetPluginId,
                                variable = targetPluginId,
                                param = param,
                                user = user,
                                message = message
                            }
                        };
                        _ = SendJsonAsync(client.Value, msg);
                    }
                }
            }
        }

        private async Task SendJsonAsync(WebSocket ws, object data)
        {
            if (ws == null || ws.State != WebSocketState.Open) return;
            try
            {
                string json = JsonSerializer.Serialize(data);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JakeyTTS;

namespace JakeyTTS
{
    public class PluginServer
    {
        private static PluginServer? _instance;
        public static PluginServer Instance => _instance ??= new PluginServer();

        private readonly HttpListener _listener = new HttpListener();
        private readonly ConcurrentDictionary<string, WebSocket> _activeClients = new();
        private readonly CancellationTokenSource _cts = new();

        public void Start(int port = 8889)
        {
            try
            {
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
                MainWindow.Instance?.Log($"🔌 Plugin WebSocket Server started on ws://localhost:{port}/");

                Task.Run(AcceptConnectionsAsync);
            }
            catch (Exception ex)
            {
                MainWindow.Instance?.Log($"❌ Plugin Server Error: {ex.Message}");
            }
        }

        public void Stop()
        {
            _cts.Cancel();
            _listener.Stop();
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
            }
        }

        private async Task HandleClientAsync(WebSocket webSocket)
        {
            var buffer = new byte[1024 * 64]; // 64KB buffer for base64 icons
            string pluginId = string.Empty;

            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    using var doc = JsonDocument.Parse(message);
                    var root = doc.RootElement;
                    string type = root.GetProperty("type").GetString() ?? "";

                    // 1. REGISTRATION HANDSHAKE
                    if (type == "register")
                    {
                        var payload = root.GetProperty("payload");
                        pluginId = payload.GetProperty("id").GetString()!;

                        _activeClients[pluginId] = webSocket;
                        HandleRegistration(payload);
                    }
                    // 2. TTS REQUEST (External program requesting custom audio)
                    else if (type == "tts_request")
                    {
                        if (IsPluginEnabled(pluginId))
                        {
                            await HandleTtsRequest(pluginId, root, webSocket);
                        }
                        else
                        {
                            await SendJsonAsync(webSocket, new { type = "error", message = "Plugin is disabled pending user approval." });
                        }
                    }
                }
            }
            catch (Exception) { /* Client disconnected forcefully */ }
            finally
            {
                if (!string.IsNullOrEmpty(pluginId)) _activeClients.TryRemove(pluginId, out _);
                if (webSocket.State != WebSocketState.Closed)
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
        }

        private void HandleRegistration(JsonElement payload)
        {
            string id = payload.GetProperty("id").GetString()!;
            string name = payload.GetProperty("name").GetString()!;
            string version = payload.GetProperty("version").GetString() ?? "1.0";
            string protocol = payload.GetProperty("protocol_version").GetString() ?? "1.0";
            string icon = payload.TryGetProperty("icon", out var iconProp) ? iconProp.GetString() ?? "" : "";

            var subs = new List<string>();
            if (payload.TryGetProperty("subscriptions", out var subsProp))
            {
                foreach (var sub in subsProp.EnumerateArray())
                    subs.Add(sub.GetString()!);
            }

            var config = TwitchService.Instance.Config;
            var existing = config.Plugins?.FirstOrDefault(p => p.Id == id);

            if (existing == null)
            {
                // New Plugin - Add to list, default to Disabled
                MainWindow.Instance?.DispatcherQueue.TryEnqueue(() => {
                    if (config.Plugins == null) config.Plugins = new();

                    config.Plugins.Add(new PluginItem
                    {
                        Id = id,
                        Name = name,
                        Version = version,
                        ProtocolVersion = protocol,
                        IconBase64 = icon,
                        Subscriptions = subs,
                        IsEnabled = false // Security: User must manually enable it in UI
                    });
                    config.Save();
                });
                MainWindow.Instance?.Log($"🔔 New Plugin requested access: {name}");
            }
            else
            {
                // Update existing metadata in case the plugin updated its version or icon
                MainWindow.Instance?.DispatcherQueue.TryEnqueue(() => {
                    existing.Name = name;
                    existing.Version = version;
                    existing.ProtocolVersion = protocol;
                    existing.IconBase64 = icon;
                    existing.Subscriptions = subs;
                    config.Save();
                });
            }

            // Send current status back to plugin immediately
            bool isApproved = existing?.IsEnabled ?? false;
            _ = SendJsonAsync(_activeClients[id], new { type = "auth_status", approved = isApproved });
        }

        private bool IsPluginEnabled(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return TwitchService.Instance.Config.Plugins?.Any(p => p.Id == id && p.IsEnabled) ?? false;
        }

        private async Task HandleTtsRequest(string pluginId, JsonElement root, WebSocket ws)
        {
            string reqId = root.GetProperty("request_id").GetString()!;
            var payload = root.GetProperty("payload");
            string text = payload.GetProperty("text").GetString()!;
            string voice = payload.GetProperty("voice").GetString()!;
            float speed = payload.TryGetProperty("speed", out var s) ? (float)s.GetDouble() : 1.0f;

            // Generate Audio bytes silently (bypassing speakers)
            byte[]? wavData = await TwitchService.Instance.SynthesizeSilentAsync(text, voice, speed);

            if (wavData != null)
            {
                string base64Audio = Convert.ToBase64String(wavData);
                await SendJsonAsync(ws, new
                {
                    type = "tts_response",
                    request_id = reqId,
                    payload = new { audio_base64 = base64Audio }
                });
            }
        }

        /// <summary>
        /// Broadcasts generated TTS audio to all connected and approved plugins that are subscribed to the specific scope.
        /// </summary>
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

                // Only send if plugin is approved AND subscribed to this scope (e.g., "bits", "subs")
                if (pluginCfg != null && pluginCfg.IsEnabled && pluginCfg.Subscriptions.Contains(scope))
                {
                    if (client.Value.State == WebSocketState.Open)
                    {
                        await SendJsonAsync(client.Value, message);
                    }
                }
            }
        }

        private async Task SendJsonAsync(WebSocket ws, object data)
        {
            string json = JsonSerializer.Serialize(data);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}
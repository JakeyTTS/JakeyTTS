using JakeyTTS.Core;
using JakeyTTS.Views;
using JakeyTTS.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JakeyTTS.Melodies;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using KokoroSharp.Utilities;
using NAudio.Wave;

namespace JakeyTTS
{
    public class TtsEngine
    {
        private static TtsEngine? _instance;
        public static TtsEngine Instance => _instance ??= new TtsEngine();

        public event EventHandler? TtsEngineReady;

        public KokoroWavSynthesizer? Synthesizer { get; private set; }
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _ttsSemaphore = new SemaphoreSlim(1, 1);
        private AppConfig Config => TwitchService.Instance.Config;

        public Dictionary<string, Action<string, Dictionary<string, object>>> CustomTags { get; } = new Dictionary<string, Action<string, Dictionary<string, object>>>();
        public List<Func<byte[], Dictionary<string, object>, byte[]>> AudioModifiers { get; } = new List<Func<byte[], Dictionary<string, object>, byte[]>>();

        private TtsEngine()
        {
            SetupAndLoadTTS();
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
                            TwitchService.Instance.LogUI($"🎙 Kokoro Ready. {KokoroVoiceManager.Voices.Count} voices loaded.");
                            MainWindow.Instance?.DispatcherQueue.TryEnqueue(() => TtsEngineReady?.Invoke(this, EventArgs.Empty));
                        }
                    }
                }
                catch (Exception ex) { TwitchService.Instance.LogUI($"❌ TTS Init Error: {ex.Message}"); }
            });
        }

        public void Stop() => _cts?.Cancel();

        /**
         * Global variable syntax: {variableName}
         * If variableName ends with "_show", it will trigger a read notification but return empty string
         * Otherwise, it will attempt to replace with the current value from PluginServer's GlobalVariables dictionary
         */
        private string PreProcessGlobalVariables(string rawInput)
        {
            if (string.IsNullOrWhiteSpace(rawInput)) return rawInput;

            return Regex.Replace(rawInput, @"\{(?<varName>[a-zA-Z0-9_\-]+)\}", m =>
            {
                string targetKey = m.Groups["varName"].Value;

                if (targetKey.EndsWith("_show", StringComparison.OrdinalIgnoreCase))
                {
                    PluginServer.Instance.NotifyVariableRead(targetKey);
                    return string.Empty;
                }

                if (PluginServer.Instance.GlobalVariables.TryGetValue(targetKey, out var value))
                {
                    return value;
                }
                return string.Empty;
            });
        }

        #region Device and Resolution Helpers
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

        private KokoroVoice? ResolveVoice(string name)
        {
            // FIXED: Voice resolution logic now checks for mixed voice configurations first, allowing users to define custom blends of voices under a single name.
            // If a mix configuration is found and valid, it will create and return the mixed voice. If not, it falls back to searching for a standard voice match by name.
            var allVoices = KokoroVoiceManager.Voices;
            if (allVoices == null || !allVoices.Any()) return null;

            var mixCfg = Config.MixedVoices?.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && m.IsEnabled);
            if (mixCfg != null && mixCfg.Components.Any())
            {
                var validComps = mixCfg.Components
                    .Select(c => (voice: allVoices.FirstOrDefault(v => v.Name == c.VoiceName), weight: c.Weight))
                    .Where(x => x.voice != null)
                    .ToList();

                if (!validComps.Any()) return null;

                float currentSum = validComps.Sum(x => x.weight);
                var normalizedComps = new List<(KokoroVoice voice, float weight)>();

                for (int i = 0; i < validComps.Count; i++)
                {
                    if (i == validComps.Count - 1)
                    {
                        float remainingWeight = 1.0f - normalizedComps.Sum(x => x.weight);
                        normalizedComps.Add((validComps[i].voice!, remainingWeight));
                    }
                    else
                    {
                        float proportionalWeight = validComps[i].weight / currentSum;
                        normalizedComps.Add((validComps[i].voice!, proportionalWeight));
                    }
                }

                return KokoroVoiceManager.Mix(normalizedComps.ToArray());
            }
            return allVoices.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        #endregion

        #region Synthesis Methods
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
                    waveOut.Init(audioFile); waveOut.Volume = Config.GlobalVolume;
                    players.Add(waveOut); waveOut.Play();
                }
                while (players.Any(p => p.PlaybackState == PlaybackState.Playing) && !(_cts?.IsCancellationRequested ?? false)) await Task.Delay(50);
                foreach (var p in players) { p.Stop(); p.Dispose(); }
            }
            catch { }
        }

        private string PreProcessDictionary(string text)
        {
            if (Config.PronunciationDictionary == null || !Config.PronunciationDictionary.Any(p => p.IsEnabled)) return text;
            foreach (var p in Config.PronunciationDictionary.Where(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.Word)))
            {
                if (p.IsRegex)
                {
                    try { text = Regex.Replace(text, p.Word, p.Replacement ?? ""); } catch { }
                }
                else
                {
                    text = Regex.Replace(text, $@"\b{Regex.Escape(p.Word)}\b", p.Replacement ?? "", RegexOptions.IgnoreCase);
                }
            }
            return text;
        }

        public async Task<byte[]?> SynthesizeSilentAsync(string text, string voiceName, float speed = 1.0f)
        {
            if (Synthesizer == null || string.IsNullOrWhiteSpace(text)) return null;

            text = PreProcessGlobalVariables(text);
            text = PreProcessDictionary(text);

            var allVoices = KokoroVoiceManager.Voices;
            if (allVoices == null || !allVoices.Any()) return null;

            text = text.Replace("\"\"", " ").Trim();
            await _ttsSemaphore.WaitAsync();

            try
            {
                var defaultVoiceName = Config.DefaultVoice;
                var baseVoice = ResolveVoice(defaultVoiceName) ?? allVoices.First();

                KokoroVoice currentActiveVoice = ResolveVoice(voiceName) ?? baseVoice;
                float currentSpeed = speed;
                float currentVolume = Config.GlobalVolume;
                float currentPitch = 1.0f;
                bool currentReverse = false;
                bool currentRobot = false;
                int currentEcho = 0;
                Melody? currentMelody = null;

                var stateContext = new Dictionary<string, object>();
                using var accumulatedStream = new MemoryStream();
                var matches = Regex.Matches(text, @"\[(?<tag>\w+)(?:(?<sep>[:+])(?<val>[\w\.-]+))?\]|(?<text>[^\[]+)");

                foreach (Match m in matches)
                {
                    if (m.Groups["tag"].Success)
                    {
                        string tagName = m.Groups["tag"].Value.ToLower();
                        var sfx = Config.SoundEffects.FirstOrDefault(e => e.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase));
                        if (sfx != null) continue;

                        if (tagName == "mix" || tagName == "voice")
                        {
                            var newVoice = ResolveVoice(m.Groups["val"].Value);
                            if (newVoice != null) currentActiveVoice = newVoice;
                        }
                        else if (CustomTags.TryGetValue(tagName, out var pluginHandler))
                        {
                            pluginHandler(m.Groups["val"].Value, stateContext);
                        }
                        else
                        {
                            HandleTag(m, ref currentActiveVoice, baseVoice, ref currentSpeed, ref currentVolume, ref currentReverse, ref currentPitch, ref currentRobot, ref currentEcho, ref currentMelody);
                        }
                    }
                    else
                    {
                        string segment = m.Groups["text"].Value.Replace("\"", "").Trim();
                        if (string.IsNullOrWhiteSpace(segment) || segment.Length == 0) continue;

                        byte[] chunkData = Synthesizer.Synthesize(segment, currentActiveVoice, new KokoroTTSPipelineConfig { Speed = currentSpeed });
                        if (chunkData == null || chunkData.Length <= 44) continue;

                        int pcmDataLength = chunkData.Length - 44;
                        byte[] rawPcmData = new byte[pcmDataLength];
                        Buffer.BlockCopy(chunkData, 44, rawPcmData, 0, pcmDataLength);

                        if (currentRobot) rawPcmData = ApplyRobotEffect(rawPcmData);
                        if (currentEcho > 0) rawPcmData = ApplyEchoEffect(rawPcmData, currentEcho);
                        if (currentReverse) rawPcmData = ReverseAudio(rawPcmData);

                        foreach (var modifier in AudioModifiers)
                        {
                            rawPcmData = modifier(rawPcmData, stateContext);
                        }

                        if (Math.Abs(currentPitch - 1.0f) > 0.01f)
                        {
                            rawPcmData = ResamplePcmSimple(rawPcmData, currentPitch);
                        }

                        await accumulatedStream.WriteAsync(rawPcmData, 0, rawPcmData.Length);
                    }
                }

                if (accumulatedStream.Length == 0) return null;

                byte[] finalPcmPayload = accumulatedStream.ToArray();
                byte[] fullWavFileBuffer = new byte[44 + finalPcmPayload.Length];

                Buffer.BlockCopy(CreateWavHeader(finalPcmPayload.Length), 0, fullWavFileBuffer, 0, 44);
                Buffer.BlockCopy(finalPcmPayload, 0, fullWavFileBuffer, 44, finalPcmPayload.Length);

                return fullWavFileBuffer;
            }
            catch (Exception ex)
            {
                TwitchService.Instance.LogUI($"❌ Silent TTS Tag Processing Error: {ex.Message}");
                return null;
            }
            finally { _ttsSemaphore.Release(); }
        }

        public async Task ProcessAndSpeak(string input, string scope = "chat")
        {
            if (Synthesizer == null || string.IsNullOrWhiteSpace(input)) return;

            input = PreProcessGlobalVariables(input);
            input = PreProcessDictionary(input);

            var allVoices = KokoroVoiceManager.Voices;
            if (allVoices == null || !allVoices.Any()) return;

            if (input.Contains("🎙 JakeyTTS:")) input = input.Replace("🎙 JakeyTTS:", "");
            input = input.Replace("\"\"", " ").Trim();

            _cts = new CancellationTokenSource();
            await _ttsSemaphore.WaitAsync();

            try
            {
                var defaultVoiceName = Config.DefaultVoice;
                var baseVoice = ResolveVoice(defaultVoiceName) ?? allVoices.First();

                KokoroVoice currentActiveVoice = baseVoice;
                float currentSpeed = 1.0f;
                float currentVolume = Config.GlobalVolume;
                float currentPitch = 1.0f;
                bool currentReverse = false;
                bool currentRobot = false;
                int currentEcho = 0;
                Melody? currentMelody = null;

                var stateContext = new Dictionary<string, object>();
                var matches = Regex.Matches(input, @"\[(?<tag>\w+)(?:(?<sep>[:+])(?<val>[\w\.-]+))?\]|(?<text>[^\[]+)");

                foreach (Match m in matches)
                {
                    if (_cts.Token.IsCancellationRequested) break;
                    if (m.Groups["tag"].Success)
                    {
                        string tagName = m.Groups["tag"].Value.ToLower();
                        var sfx = Config.SoundEffects.FirstOrDefault(e => e.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase));

                        if (sfx != null && sfx.IsEnabled) { await PlaySoundEffect(tagName); continue; }

                        if (tagName == "mix" || tagName == "voice")
                        {
                            var newVoice = ResolveVoice(m.Groups["val"].Value);
                            if (newVoice != null) currentActiveVoice = newVoice;
                        }
                        else if (CustomTags.TryGetValue(tagName, out var pluginHandler))
                        {
                            pluginHandler(m.Groups["val"].Value, stateContext);
                        }
                        else
                        {
                            HandleTag(m, ref currentActiveVoice, baseVoice, ref currentSpeed, ref currentVolume, ref currentReverse, ref currentPitch, ref currentRobot, ref currentEcho, ref currentMelody);
                        }
                    }
                    else
                    {
                        string segment = m.Groups["text"].Value.Replace("\"", "").Trim();

                        if (string.IsNullOrWhiteSpace(segment) || segment.Length == 0) continue;
                        if (Synthesizer == null || currentActiveVoice == null) continue;

                        byte[] wavData = Synthesizer.Synthesize(segment, currentActiveVoice, new KokoroTTSPipelineConfig { Speed = currentSpeed });
                        if (wavData == null || wavData.Length <= 44) continue;

                        if (currentRobot) wavData = ApplyRobotEffect(wavData);
                        if (currentEcho > 0) wavData = ApplyEchoEffect(wavData, currentEcho);
                        if (currentReverse) wavData = ReverseAudio(wavData);

                        if (AudioModifiers.Any())
                        {
                            int rawPcmLength = wavData.Length - 44;
                            byte[] rawPcm = new byte[rawPcmLength];
                            Buffer.BlockCopy(wavData, 44, rawPcm, 0, rawPcmLength);

                            foreach (var modifier in AudioModifiers) rawPcm = modifier(rawPcm, stateContext);

                            byte[] directWav = new byte[44 + rawPcm.Length];
                            Buffer.BlockCopy(CreateWavHeader(rawPcm.Length), 0, directWav, 0, 44);
                            Buffer.BlockCopy(rawPcm, 0, directWav, 44, rawPcm.Length);
                            wavData = directWav;
                        }

                        _ = PluginServer.Instance.BroadcastEventAsync(scope, segment, wavData);
                        await PlayWavData(wavData, currentVolume, currentPitch, currentMelody);
                    }
                }
            }
            catch (Exception ex) { TwitchService.Instance.LogUI($"❌ Audio Error: {ex.Message}"); }
            finally { _ttsSemaphore.Release(); }
        }
        #endregion

        #region Audio FX Pipeline
        private void HandleTag(Match m, ref KokoroVoice activeVoice, KokoroVoice baseVoice, ref float speed, ref float vol, ref bool reverse, ref float pitch, ref bool robot, ref int echo, ref Melody? melody)
        {
            string tagName = m.Groups["tag"].Value.ToLower();
            string rawVal = m.Groups["val"].Value;

            rawVal = rawVal.Replace("\"", "").Trim();
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
                case "speed":
                    if (m.Groups["sep"].Value == "+") speed = 1.0f + numVal;
                    else speed = (numVal > 5) ? (numVal / 1000f) : numVal;
                    if (speed <= 0.1f) speed = 1.0f;
                    break;
                case "pause":
                    int ms = (m.Groups["sep"].Value == "+") ? (int)(numVal * 1000) : (int)numVal;
                    if (ms > 0) Task.Delay(ms).Wait(); break;
                case "reverse": reverse = !reverse; break;
                case "high": pitch = (numVal > 0) ? numVal : 1.5f; break;
                case "deep": pitch = (numVal > 0) ? numVal : 0.7f; break;
                case "pitch": pitch = (numVal > 0) ? numVal : 1.0f; break;
                case "robot": robot = !robot; break;
                case "data":
                case "echo": echo = (numVal > 0) ? (int)numVal : 150; break;
                case "melody":
                    melody = string.IsNullOrEmpty(rawVal) ? null : MelodyService.Instance.Melodies.FirstOrDefault(x => x.Name.Equals(rawVal, StringComparison.OrdinalIgnoreCase) && x.IsEnabled);
                    break;
            }
        }

        /*  
         * REVERSE EFFECT (Temporal Phase Inversion)  
         * -----------------------------------------  
         * Theory: 16-bit Mono PCM audio consists of a sequence of "samples."   
         * Each sample occupies 2 bytes (Little-Endian format).  
         *   
         * Technical Note: You cannot simply reverse the raw byte array (byte[]). Doing so   
         * would flip the internal byte order of each 16-bit sample, destroying the amplitude   
         * data and resulting in digital noise (static).  
         *   
         * Implementation: We iterate through the data in 2-byte blocks. We move block 'i'   
         * to position (Total - 1 - i), ensuring each individual sample's integrity is preserved.  
         */
        private byte[] ReverseAudio(byte[] data)
        {
            if (data == null || data.Length < 2) return data;
            int sampleCount = data.Length / 2;
            byte[] reversed = new byte[data.Length];
            for (int i = 0; i < sampleCount; i++)
            {
                int srcIdx = i * 2; int destIdx = (sampleCount - 1 - i) * 2;
                reversed[destIdx] = data[srcIdx]; reversed[destIdx + 1] = data[srcIdx + 1];
            }
            return reversed;
        }

        /*  
         * ROBOT EFFECT (Ring Modulation)  
         * ------------------------------  
         * Theory: Multiply the audio signal (carrier) by a low-frequency sine wave   
         * (modulator, typically between 30Hz and 60Hz).  
         * https://en.wikipedia.org/wiki/Ring_modulation  
         * Technical Note: In the frequency domain, this creates "sidebands" (the sum and   
         * difference of the carrier and modulator frequencies). This results in a non-harmonic,   
         * metallic timbre that "dehumanizes" the voice by shifting its natural formants.  
         *   
         * Implementation: Bytes are converted to 16-bit integers (shorts), multiplied by   
         * a Sin() value relative to the sample's timestamp, and converted back to bytes.  
         */
        private byte[] ApplyRobotEffect(byte[] data)
        {
            if (data == null || data.Length < 2) return data;
            int sampleCount = data.Length / 2;
            byte[] processed = new byte[data.Length];
            double frequency = 50.0; double sampleRate = 24000.0;
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

        /*  
         * ECHO EFFECT (Feedback Delay Line)  
         * ---------------------------------  
         * Theory: Echo is produced by adding a version of the signal that occurred in the   
         * past (delay) to the current signal, attenuated by a gain factor (decay).  
         * https://music.arts.lucid-bardeen/delay-effect-feedback  
         *   
         * Technical Note:   
         * 1. Buffer Expansion: The resulting audio must be longer than the original   
         *    to accommodate the "echo tail" after the speech ends.  
         * 2. Mixing: We sum the original sample with the delayed sample.  
         * 3. Clamping: Summing two signals can exceed 16-bit limits (-32768 to 32767).   
         *    We use Math.Clamp to prevent digital clipping (distortion).  
         */
        private byte[] ApplyEchoEffect(byte[] data, int delayMs)
        {
            if (data == null || data.Length < 2 || delayMs <= 0) return data;
            int sampleRate = 24000; int delaySamples = (delayMs * sampleRate) / 1000;
            float decay = 0.45f; int extraBuffer = delaySamples * 2;
            byte[] processed = new byte[data.Length + extraBuffer];
            int originalSampleCount = data.Length / 2;
            for (int i = 0; i < (processed.Length / 2); i++)
            {
                short original = (i < originalSampleCount) ? BitConverter.ToInt16(data, i * 2) : (short)0;
                short echo = (i >= delaySamples && (i - delaySamples) < originalSampleCount) ? (short)(BitConverter.ToInt16(data, (i - delaySamples) * 2) * decay) : (short)0;
                short mixed = (short)Math.Clamp(original + echo, short.MinValue, short.MaxValue);
                byte[] bytes = BitConverter.GetBytes(mixed);
                processed[i * 2] = bytes[0]; processed[i * 2 + 1] = bytes[1];
            }
            return processed;
        }

        /*
         * PITCH SHIFT (Resampling with Linear Interpolation)  
         * --------------------------------------------------  
         * Theory: To change the pitch without affecting duration, we can resample the audio data.  
         * A pitch multiplier > 1.0 raises the pitch (fewer samples), while < 1.0 lowers it (more samples).  
         * https://en.wikipedia.org/wiki/Pitch_shifting#Resampling  
         *   
         * Technical Note:   
         * 1. Output Length: The output sample count is input sample count divided by the pitch multiplier.  
         * 2. Linear Interpolation: For non-integer source positions, we interpolate between the two nearest samples to create a smoother result.  
         */
        private byte[] ResamplePcmSimple(byte[] data, float pitchMultiplier)
        {
            if (data == null || data.Length < 2 || Math.Abs(pitchMultiplier - 1.0f) < 0.01f) return data;
            int inputSamples = data.Length / 2;
            int outputSamples = (int)(inputSamples / pitchMultiplier);
            byte[] outputBytes = new byte[outputSamples * 2];

            for (int i = 0; i < outputSamples; i++)
            {
                float srcSamplePos = i * pitchMultiplier;
                int index = (int)srcSamplePos;
                float frac = srcSamplePos - index;

                if (index >= inputSamples - 1) break;

                short s1 = BitConverter.ToInt16(data, index * 2);
                short s2 = BitConverter.ToInt16(data, (index + 1) * 2);
                short interpolated = (short)(s1 + frac * (s2 - s1));

                byte[] sampleBytes = BitConverter.GetBytes(interpolated);
                outputBytes[i * 2] = sampleBytes[0];
                outputBytes[i * 2 + 1] = sampleBytes[1];
            }
            return outputBytes;
        }

        /*
         * WAV HEADER CREATION  
         * -------------------  
         * Theory: A WAV file consists of a 44-byte header followed by raw PCM data.  
         * The header contains metadata about the audio format, sample rate, bit depth, and data length.  
         * https://en.wikipedia.org/wiki/WAV#File_structure  
         *   
         * Technical Note: We construct the header manually to prepend it to our synthesized PCM data, allowing it to be played as a standard WAV file.  
         */
        private byte[] CreateWavHeader(int pcmDataLength)
        {
            byte[] header = new byte[44];
            int totalDataLen = pcmDataLength + 36;

            header[0] = (byte)'R'; header[1] = (byte)'I'; header[2] = (byte)'F'; header[3] = (byte)'F';
            Buffer.BlockCopy(BitConverter.GetBytes(totalDataLen), 0, header, 4, 4);
            header[8] = (byte)'W'; header[9] = (byte)'A'; header[10] = (byte)'V'; header[11] = (byte)'E';

            header[12] = (byte)'f'; header[13] = (byte)'m'; header[14] = (byte)'t'; header[15] = (byte)' ';
            Buffer.BlockCopy(BitConverter.GetBytes(16), 0, header, 16, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((short)1), 0, header, 20, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((short)1), 0, header, 22, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(24000), 0, header, 24, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(24000 * 2), 0, header, 28, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((short)2), 0, header, 32, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((short)16), 0, header, 34, 2);

            header[36] = (byte)'d'; header[37] = (byte)'a'; header[38] = (byte)'t'; header[39] = (byte)'a';
            Buffer.BlockCopy(BitConverter.GetBytes(pcmDataLength), 0, header, 40, 4);

            return header;
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
            while (players.Any(p => p.PlaybackState == PlaybackState.Playing) && !_cts!.IsCancellationRequested) await Task.Delay(50);
            foreach (var p in players) { p.Stop(); p.Dispose(); }
        }
        #endregion

        /*  
         * MELODY EFFECT (Dynamic Pitch Modulation)  
         * -----------------------------------------  
         * Theory: A melody effect modulates the pitch of the audio over time according to a predefined curve or pattern.  
         * This can create musical intonations, vibrato, or other expressive effects.  
         *   
         * Technical Note: We implement this by creating a custom IWaveProvider that reads from the original PCM data 
         * and applies a dynamic pitch multiplier based on the current playback position and the specified Melody's curve.  
         */
        private class MelodyWaveProvider : IWaveProvider
        {
            private readonly byte[] _sourceData; private readonly WaveFormat _format; private readonly Melody _melody; private double _sourcePosition = 0;
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
                    short s1 = BitConverter.ToInt16(_sourceData, index * 2); short s2 = BitConverter.ToInt16(_sourceData, (index + 1) * 2);
                    short sample = (short)(s1 + frac * (s2 - s1)); byte[] bytes = BitConverter.GetBytes(sample);
                    buffer[offset + bytesRead] = bytes[0]; buffer[offset + bytesRead + 1] = bytes[1];
                    _sourcePosition += pitchMultiplier; bytesRead += 2;
                }
                return bytesRead;
            }
        }
    }
}

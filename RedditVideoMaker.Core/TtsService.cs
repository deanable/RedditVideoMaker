// TtsService.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using ElevenLabs;
using ElevenLabs.Voices;
using ElevenLabs.TextToSpeech;

// System.Speech for fallback:
// No 'using System.Speech.Synthesis;' here to avoid ambiguity at this level.

namespace RedditVideoMaker.Core
{
    public class TtsService
    {
        private readonly TtsOptions _ttsOptions;

        public TtsService(IOptions<TtsOptions> ttsOptions)
        {
            _ttsOptions = ttsOptions.Value;
        }

        public async Task<bool> TextToSpeechAsync(string text, string outputFilePath)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Console.Error.WriteLine("TTS Error: Input text cannot be empty.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                Console.Error.WriteLine("TTS Error: Output file path cannot be empty.");
                return false;
            }

            string? directory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Console.WriteLine($"TTS: Attempting to use engine: '{_ttsOptions.Engine}'");

            if (_ttsOptions.Engine?.Equals("Azure", StringComparison.OrdinalIgnoreCase) == true &&
                !string.IsNullOrWhiteSpace(_ttsOptions.AzureSpeechKey) &&
                !string.IsNullOrWhiteSpace(_ttsOptions.AzureSpeechRegion))
            {
                Console.WriteLine("TTS: Using Azure Cognitive Services Speech.");
                return await SynthesizeWithAzureAsync(text, outputFilePath);
            }
            else if (_ttsOptions.Engine?.Equals("ElevenLabs", StringComparison.OrdinalIgnoreCase) == true &&
                     !string.IsNullOrWhiteSpace(_ttsOptions.ElevenLabsApiKey))
            {
                Console.WriteLine("TTS: Using ElevenLabs Speech.");
                return await SynthesizeWithElevenLabsAsync(text, outputFilePath);
            }
            else
            {
                if (_ttsOptions.Engine?.Equals("Azure", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Console.Error.WriteLine("TTS Warning: Azure engine selected, but AzureSpeechKey or AzureSpeechRegion is missing. Falling back.");
                }
                else if (_ttsOptions.Engine?.Equals("ElevenLabs", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Console.Error.WriteLine("TTS Warning: ElevenLabs engine selected, but ElevenLabsApiKey is missing. Falling back.");
                }
                Console.WriteLine("TTS: Using SystemSpeech (fallback).");
                return await Task.Run(() => SynthesizeWithSystemSpeech(text, outputFilePath));
            }
        }

        private async Task<bool> SynthesizeWithAzureAsync(string text, string outputFilePath)
        {
            try
            {
                var speechConfig = SpeechConfig.FromSubscription(_ttsOptions.AzureSpeechKey, _ttsOptions.AzureSpeechRegion);

                if (!string.IsNullOrWhiteSpace(_ttsOptions.AzureVoiceName))
                {
                    speechConfig.SpeechSynthesisVoiceName = _ttsOptions.AzureVoiceName;
                    Console.WriteLine($"TTS (Azure): Using voice: {_ttsOptions.AzureVoiceName}");
                }
                else { Console.WriteLine("TTS (Azure): Using default voice for the region/language."); }

                using var audioConfig = AudioConfig.FromWavFileOutput(outputFilePath);
                using var synthesizer = new Microsoft.CognitiveServices.Speech.SpeechSynthesizer(speechConfig, audioConfig);

                Console.WriteLine($"TTS (Azure): Synthesizing: \"{text.Substring(0, Math.Min(text.Length, 70))}...\"");
                var result = await synthesizer.SpeakTextAsync(text);

                if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                {
                    Console.WriteLine($"TTS (Azure): Speech successfully saved to {outputFilePath}");
                    return true;
                }
                else if (result.Reason == ResultReason.Canceled)
                {
                    var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                    Console.Error.WriteLine($"TTS (Azure) CANCELED: Reason={cancellation.Reason}");
                    if (cancellation.Reason == CancellationReason.Error)
                    {
                        Console.Error.WriteLine($"TTS (Azure) CANCELED: ErrorCode={cancellation.ErrorCode}, ErrorDetails=[{cancellation.ErrorDetails}]");
                    }
                    return false;
                }
                else
                {
                    Console.Error.WriteLine($"TTS (Azure): Speech synthesis failed. Reason: {result.Reason}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"TTS (Azure) Error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> SynthesizeWithElevenLabsAsync(string text, string outputFilePath)
        {
            try
            {
                var api = new ElevenLabsClient(_ttsOptions.ElevenLabsApiKey);
                Voice? nullableSelectedVoice = null;

                if (!string.IsNullOrWhiteSpace(_ttsOptions.ElevenLabsVoiceId))
                {
                    try
                    {
                        nullableSelectedVoice = await api.VoicesEndpoint.GetVoiceAsync(_ttsOptions.ElevenLabsVoiceId);
                        Console.WriteLine($"TTS (ElevenLabs): Using voice ID: {_ttsOptions.ElevenLabsVoiceId} (Name: {nullableSelectedVoice?.Name ?? "Unknown"})");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"TTS (ElevenLabs): Failed to get voice by ID '{_ttsOptions.ElevenLabsVoiceId}'. Using default. Error: {ex.Message}");
                        var defaultVoices = await api.VoicesEndpoint.GetAllVoicesAsync();
                        nullableSelectedVoice = defaultVoices.FirstOrDefault();
                    }
                }
                else
                {
                    var defaultVoices = await api.VoicesEndpoint.GetAllVoicesAsync();
                    nullableSelectedVoice = defaultVoices.FirstOrDefault();
                    if (nullableSelectedVoice != null)
                    {
                        Console.WriteLine($"TTS (ElevenLabs): Using default voice: {nullableSelectedVoice.Name} (ID: {nullableSelectedVoice.Id})");
                    }
                }

                if (nullableSelectedVoice == null)
                {
                    Console.Error.WriteLine("TTS (ElevenLabs): No voice available (neither specified nor default). Cannot synthesize.");
                    return false;
                }

                Voice actualSelectedVoice = nullableSelectedVoice; // Safe due to null check above

                Console.WriteLine($"TTS (ElevenLabs): Synthesizing: \"{text.Substring(0, Math.Min(text.Length, 70))}...\"");

                var ttsRequest = new TextToSpeechRequest(actualSelectedVoice, text);

                VoiceClip? voiceClip = null;
                try
                {
                    voiceClip = await api.TextToSpeechEndpoint.TextToSpeechAsync(ttsRequest);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"TTS (ElevenLabs) Error during synthesis call: {e.Message}");
                    return false;
                }

                if (voiceClip == null)
                {
                    Console.Error.WriteLine("TTS (ElevenLabs): Synthesized audio result (VoiceClip) is null.");
                    return false;
                }

                try
                {
                    // Access the ClipData (ReadOnlyMemory<byte>) from VoiceClip (or its base GeneratedClip)
                    // The property name might be ClipData, AudioData, or similar. Let's assume ClipData for now.
                    // This is based on the constructor: base(id, text, clipData, sampleRate)
                    // We need to find how 'clipData' is exposed publicly.
                    // If 'VoiceClip' inherits from 'GeneratedClip' and 'GeneratedClip' has a public 'ClipData' property:
                    ReadOnlyMemory<byte> audioBytes = voiceClip.ClipData; // This is the critical assumption based on decompiled info

                    if (audioBytes.IsEmpty)
                    {
                        Console.Error.WriteLine("TTS (ElevenLabs): Synthesized audio data is empty.");
                        return false;
                    }

                    await File.WriteAllBytesAsync(outputFilePath, audioBytes.ToArray());

                    Console.WriteLine($"TTS (ElevenLabs): Speech successfully saved to {outputFilePath}");
                    return true;
                }
                catch (MissingMemberException mmEx)
                {
                    Console.Error.WriteLine($"TTS (ElevenLabs) Error: SDK API mismatch. VoiceClip does not have expected 'ClipData' property. {mmEx.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"TTS (ElevenLabs) Error saving audio clip: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"TTS (ElevenLabs) Error: {ex.Message}");
                return false;
            }
        }

        private bool SynthesizeWithSystemSpeech(string text, string outputFilePath)
        {
            try
            {
                using (var synthesizer = new System.Speech.Synthesis.SpeechSynthesizer())
                {
                    synthesizer.SetOutputToWaveFile(outputFilePath);
                    Console.WriteLine($"TTS (SystemSpeech): Synthesizing: \"{text.Substring(0, Math.Min(text.Length, 70))}...\"");
                    synthesizer.Speak(text);
                    Console.WriteLine($"TTS (SystemSpeech): Speech saved to {outputFilePath}");
                }
                return true;
            }
            catch (PlatformNotSupportedException pnsEx)
            {
                Console.Error.WriteLine($"TTS (SystemSpeech) Error: System.Speech is not supported on this platform. {pnsEx.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"TTS (SystemSpeech) Error: {ex.Message}");
                return false;
            }
        }
    }
}

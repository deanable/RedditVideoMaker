// TtsService.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

// No 'using System.Speech.Synthesis;' here at the top level to avoid ambiguity.
// We will fully qualify System.Speech.Synthesis.SpeechSynthesizer.

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
            else
            {
                if (_ttsOptions.Engine?.Equals("Azure", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Console.Error.WriteLine("TTS Warning: Azure engine selected, but AzureSpeechKey or AzureSpeechRegion is missing. Falling back to SystemSpeech.");
                }
                Console.WriteLine("TTS: Using SystemSpeech (fallback).");
                // SynthesizeWithSystemSpeech is synchronous, so wrap in Task.Run for an async signature
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
                else
                {
                    Console.WriteLine("TTS (Azure): Using default voice for the region/language.");
                }

                using var audioConfig = AudioConfig.FromWavFileOutput(outputFilePath);
                // Fully qualify the Azure SpeechSynthesizer
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
                Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");
                return false;
            }
        }

        private bool SynthesizeWithSystemSpeech(string text, string outputFilePath)
        {
            try
            {
                // Fully qualify the System.Speech.Synthesis.SpeechSynthesizer
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
                Console.Error.WriteLine($"TTS (SystemSpeech) Error: An unexpected error occurred: {ex.Message}");
                return false;
            }
        }
    }
}

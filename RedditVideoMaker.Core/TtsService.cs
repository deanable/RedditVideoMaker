// TtsService.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Google.Cloud.TextToSpeech.V1;

// No 'using System.Speech.Synthesis;' here at the top level to avoid ambiguity.

namespace RedditVideoMaker.Core
{
    public class TtsService
    {
        private readonly TtsOptions _ttsOptions;
        private readonly GeneralOptions _generalOptions; // For testing mode

        public TtsService(IOptions<TtsOptions> ttsOptions, IOptions<GeneralOptions> generalOptions)
        {
            _ttsOptions = ttsOptions.Value;
            _generalOptions = generalOptions.Value;
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

            // Check for testing mode first
            if (_generalOptions.IsInTestingModule)
            {
                Console.WriteLine("TTS: In Testing Mode - Using SystemSpeech.");
                return await Task.Run(() => SynthesizeWithSystemSpeech(text, outputFilePath));
            }

            Console.WriteLine($"TTS: Attempting to use configured engine: '{_ttsOptions.Engine}'");

            if (_ttsOptions.Engine?.Equals("Azure", StringComparison.OrdinalIgnoreCase) == true &&
                !string.IsNullOrWhiteSpace(_ttsOptions.AzureSpeechKey) &&
                !string.IsNullOrWhiteSpace(_ttsOptions.AzureSpeechRegion))
            {
                Console.WriteLine("TTS: Using Azure Cognitive Services Speech.");
                return await SynthesizeWithAzureAsync(text, outputFilePath);
            }
            else if (_ttsOptions.Engine?.Equals("GoogleCloud", StringComparison.OrdinalIgnoreCase) == true &&
                     !string.IsNullOrWhiteSpace(_ttsOptions.GoogleCloudCredentialsPath) &&
                     File.Exists(_ttsOptions.GoogleCloudCredentialsPath))
            {
                Console.WriteLine("TTS: Using Google Cloud Text-to-Speech.");
                return await SynthesizeWithGoogleCloudAsync(text, outputFilePath);
            }
            else
            {
                // Fallback logic if a specific engine was configured but credentials were bad
                if (_ttsOptions.Engine?.Equals("Azure", StringComparison.OrdinalIgnoreCase) == true)
                    Console.Error.WriteLine("TTS Warning: Azure engine selected, but credentials missing/invalid. Falling back to SystemSpeech.");
                else if (_ttsOptions.Engine?.Equals("GoogleCloud", StringComparison.OrdinalIgnoreCase) == true)
                    Console.Error.WriteLine("TTS Warning: GoogleCloud engine selected, but credentials path missing or invalid. Falling back to SystemSpeech.");
                else if (!string.IsNullOrWhiteSpace(_ttsOptions.Engine) && !_ttsOptions.Engine.Equals("SystemSpeech", StringComparison.OrdinalIgnoreCase))
                    Console.Error.WriteLine($"TTS Warning: Configured engine '{_ttsOptions.Engine}' is not recognized or fully configured. Falling back to SystemSpeech.");

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
                else
                {
                    Console.WriteLine("TTS (Azure): Using default voice for the region/language.");
                }
                // Fully qualify AudioConfig for Azure
                using var audioConfig = Microsoft.CognitiveServices.Speech.Audio.AudioConfig.FromWavFileOutput(outputFilePath);
                using var synthesizer = new Microsoft.CognitiveServices.Speech.SpeechSynthesizer(speechConfig, audioConfig);

                Console.WriteLine($"TTS (Azure): Synthesizing: \"{text.Substring(0, Math.Min(text.Length, 70))}...\"");
                var result = await synthesizer.SpeakTextAsync(text);

                if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                {
                    Console.WriteLine($"TTS (Azure): Speech saved to {outputFilePath}"); return true;
                }
                else
                {
                    var cancellationDetails = SpeechSynthesisCancellationDetails.FromResult(result);
                    Console.Error.WriteLine($"TTS (Azure) Error: {result.Reason}, Details: {cancellationDetails?.ErrorDetails}");
                    if (cancellationDetails?.Reason == CancellationReason.Error)
                    {
                        Console.Error.WriteLine($"TTS (Azure) ErrorCode: {cancellationDetails.ErrorCode}");
                    }
                    return false;
                }
            }
            catch (Exception ex) { Console.Error.WriteLine($"TTS (Azure) Error: {ex.Message}"); return false; }
        }

        private async Task<bool> SynthesizeWithGoogleCloudAsync(string text, string outputFilePath)
        {
            try
            {
                var clientBuilder = new TextToSpeechClientBuilder
                {
                    CredentialsPath = _ttsOptions.GoogleCloudCredentialsPath
                };
                TextToSpeechClient client = await clientBuilder.BuildAsync();

                var input = new SynthesisInput { Text = text };
                var voiceSelection = new VoiceSelectionParams
                {
                    LanguageCode = !string.IsNullOrWhiteSpace(_ttsOptions.GoogleCloudLanguageCode) ? _ttsOptions.GoogleCloudLanguageCode : "en-US"
                };

                if (!string.IsNullOrWhiteSpace(_ttsOptions.GoogleCloudVoiceName))
                {
                    voiceSelection.Name = _ttsOptions.GoogleCloudVoiceName;
                    Console.WriteLine($"TTS (GoogleCloud): Using voice: {_ttsOptions.GoogleCloudVoiceName} for language: {voiceSelection.LanguageCode}");
                }
                else
                {
                    Console.WriteLine($"TTS (GoogleCloud): Using default voice for language: {voiceSelection.LanguageCode}");
                }

                // Fully qualify AudioConfig for Google Cloud
                var audioConfig = new Google.Cloud.TextToSpeech.V1.AudioConfig { AudioEncoding = Google.Cloud.TextToSpeech.V1.AudioEncoding.Linear16 };

                Console.WriteLine($"TTS (GoogleCloud): Synthesizing: \"{text.Substring(0, Math.Min(text.Length, 70))}...\"");
                var response = await client.SynthesizeSpeechAsync(input, voiceSelection, audioConfig);

                await File.WriteAllBytesAsync(outputFilePath, response.AudioContent.ToByteArray());

                Console.WriteLine($"TTS (GoogleCloud): Speech saved to {outputFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"TTS (GoogleCloud) Error: {ex.Message}");
                Console.Error.WriteLine("Ensure your Google Cloud credentials JSON path is correct, the service account has 'Cloud Text-to-Speech API User' role, and billing is enabled on your GCP project.");
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
                    Console.WriteLine($"TTS (SystemSpeech): Synthesizing...");
                    synthesizer.Speak(text);
                    Console.WriteLine($"TTS (SystemSpeech): Speech saved to {outputFilePath}");
                }
                return true;
            }
            catch (PlatformNotSupportedException) { Console.Error.WriteLine("TTS (SystemSpeech) Error: Not supported on this platform."); return false; }
            catch (Exception ex) { Console.Error.WriteLine($"TTS (SystemSpeech) Error: {ex.Message}"); return false; }
        }
    }
}

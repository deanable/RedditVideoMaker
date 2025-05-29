// TtsService.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Options; // For IOptions
using Microsoft.CognitiveServices.Speech; // Azure TTS
using Microsoft.CognitiveServices.Speech.Audio; // Azure TTS AudioConfig
using Google.Cloud.TextToSpeech.V1; // Google Cloud TTS

// System.Speech.Synthesis.SpeechSynthesizer is fully qualified where used
// to avoid ambiguity and because it's platform-specific.

namespace RedditVideoMaker.Core
{
    /// <summary>
    /// Provides Text-to-Speech (TTS) services using various engines (Azure, Google Cloud, SystemSpeech).
    /// It selects the appropriate engine based on configuration and handles the synthesis process.
    /// </summary>
    public class TtsService
    {
        private readonly TtsOptions _ttsOptions;
        private readonly GeneralOptions _generalOptions; // Used for checking IsInTestingModule

        /// <summary>
        /// Initializes a new instance of the <see cref="TtsService"/> class.
        /// </summary>
        /// <param name="ttsOptions">The TTS configuration options.</param>
        /// <param name="generalOptions">The general application configuration options.</param>
        public TtsService(IOptions<TtsOptions> ttsOptions, IOptions<GeneralOptions> generalOptions)
        {
            _ttsOptions = ttsOptions.Value;
            _generalOptions = generalOptions.Value;
        }

        /// <summary>
        /// Converts the given text to speech and saves it as an audio file at the specified path.
        /// The TTS engine is determined by configuration, with a fallback mechanism.
        /// </summary>
        /// <param name="text">The text to synthesize.</param>
        /// <param name="outputFilePath">The path where the generated audio file (typically .wav) will be saved.</param>
        /// <returns>True if speech synthesis was successful and the file was saved; false otherwise.</returns>
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
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"TTS Error: Failed to create output directory '{directory}'. Exception: {ex.Message}");
                    return false;
                }
            }

            if (_generalOptions.IsInTestingModule)
            {
                Console.WriteLine("TTS: In Testing Mode - Using SystemSpeech engine.");
                return await Task.Run(() => SynthesizeWithSystemSpeech(text, outputFilePath));
            }

            Console.WriteLine($"TTS: Attempting to use configured engine: '{_ttsOptions.Engine}'");

            if (_ttsOptions.Engine?.Equals("Azure", StringComparison.OrdinalIgnoreCase) == true &&
                !string.IsNullOrWhiteSpace(_ttsOptions.AzureSpeechKey) &&
                !string.IsNullOrWhiteSpace(_ttsOptions.AzureSpeechRegion))
            {
                Console.WriteLine("TTS: Using Azure Cognitive Services Speech engine.");
                return await SynthesizeWithAzureAsync(text, outputFilePath);
            }
            else if (_ttsOptions.Engine?.Equals("GoogleCloud", StringComparison.OrdinalIgnoreCase) == true &&
                     !string.IsNullOrWhiteSpace(_ttsOptions.GoogleCloudCredentialsPath) &&
                     File.Exists(_ttsOptions.GoogleCloudCredentialsPath))
            {
                Console.WriteLine("TTS: Using Google Cloud Text-to-Speech engine.");
                return await SynthesizeWithGoogleCloudAsync(text, outputFilePath);
            }
            else
            {
                if (_ttsOptions.Engine?.Equals("Azure", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Console.Error.WriteLine("TTS Warning: Azure engine selected, but credentials (key/region) missing or invalid. Falling back to SystemSpeech.");
                }
                else if (_ttsOptions.Engine?.Equals("GoogleCloud", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Console.Error.WriteLine("TTS Warning: GoogleCloud engine selected, but credentials path missing or file not found. Falling back to SystemSpeech.");
                }
                else if (!string.IsNullOrWhiteSpace(_ttsOptions.Engine) && !_ttsOptions.Engine.Equals("SystemSpeech", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"TTS Warning: Configured engine '{_ttsOptions.Engine}' is not recognized or fully configured. Falling back to SystemSpeech.");
                }
                Console.WriteLine("TTS: Using SystemSpeech engine (default or fallback).");
                return await Task.Run(() => SynthesizeWithSystemSpeech(text, outputFilePath));
            }
        }

        /// <summary>
        /// Synthesizes speech using Azure Cognitive Services Text-to-Speech.
        /// </summary>
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
                    Console.WriteLine("TTS (Azure): Using default voice for the configured region/language.");
                }

                using var audioConfig = Microsoft.CognitiveServices.Speech.Audio.AudioConfig.FromWavFileOutput(outputFilePath);
                using var synthesizer = new Microsoft.CognitiveServices.Speech.SpeechSynthesizer(speechConfig, audioConfig);

                Console.WriteLine($"TTS (Azure): Synthesizing text (approx {Math.Min(text.Length, 70)} chars): \"{text.Substring(0, Math.Min(text.Length, 70))}...\"");
                var result = await synthesizer.SpeakTextAsync(text);

                if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                {
                    Console.WriteLine($"TTS (Azure): Speech synthesized and saved to {outputFilePath}");
                    return true;
                }
                else
                {
                    var cancellationDetails = SpeechSynthesisCancellationDetails.FromResult(result);
                    Console.Error.WriteLine($"TTS (Azure) Error: Synthesis failed. Reason: {result.Reason}");
                    if (cancellationDetails != null)
                    {
                        Console.Error.WriteLine($"TTS (Azure) Cancellation Details: {cancellationDetails.ErrorDetails}");
                        if (cancellationDetails.Reason == CancellationReason.Error)
                        {
                            Console.Error.WriteLine($"TTS (Azure) ErrorCode: {cancellationDetails.ErrorCode}");
                        }
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"TTS (Azure) Exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Synthesizes speech using Google Cloud Text-to-Speech.
        /// IMPORTANT WORKAROUND: Due to persistent compiler errors (CS8410/CS1674) related to IAsyncDisposable/IDisposable
        /// in the current project environment for TextToSpeechClient, explicit disposal (using/await using)
        /// has been temporarily removed. This is not ideal for resource management in long-running services
        /// but allows the application to proceed. This should be revisited to ensure proper
        /// client lifecycle management, likely by resolving NuGet package versioning or project compatibility issues.
        /// </summary>
        private async Task<bool> SynthesizeWithGoogleCloudAsync(string text, string outputFilePath)
        {
            try
            {
                // Create the client without a 'using' statement for now due to environment issues.
                TextToSpeechClient client = await new TextToSpeechClientBuilder
                {
                    CredentialsPath = _ttsOptions.GoogleCloudCredentialsPath
                }.BuildAsync();

                var input = new SynthesisInput { Text = text };
                var voiceSelection = new VoiceSelectionParams
                {
                    LanguageCode = !string.IsNullOrWhiteSpace(_ttsOptions.GoogleCloudLanguageCode)
                                     ? _ttsOptions.GoogleCloudLanguageCode
                                     : "en-US"
                };

                if (!string.IsNullOrWhiteSpace(_ttsOptions.GoogleCloudVoiceName))
                {
                    voiceSelection.Name = _ttsOptions.GoogleCloudVoiceName;
                    Console.WriteLine($"TTS (GoogleCloud): Using voice: '{_ttsOptions.GoogleCloudVoiceName}' for language: '{voiceSelection.LanguageCode}'.");
                }
                else
                {
                    Console.WriteLine($"TTS (GoogleCloud): Using default voice for language: '{voiceSelection.LanguageCode}'.");
                }

                var audioConfig = new Google.Cloud.TextToSpeech.V1.AudioConfig
                {
                    AudioEncoding = Google.Cloud.TextToSpeech.V1.AudioEncoding.Linear16
                };

                Console.WriteLine($"TTS (GoogleCloud): Synthesizing text (approx {Math.Min(text.Length, 70)} chars): \"{text.Substring(0, Math.Min(text.Length, 70))}...\"");
                var response = await client.SynthesizeSpeechAsync(input, voiceSelection, audioConfig);

                await File.WriteAllBytesAsync(outputFilePath, response.AudioContent.ToByteArray());
                Console.WriteLine($"TTS (GoogleCloud): Speech synthesized and saved to {outputFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"TTS (GoogleCloud) Exception: {ex.Message}");
                Console.Error.WriteLine("Ensure your Google Cloud credentials JSON path is correct, the service account has the 'Cloud Text-to-Speech API User' role, API is enabled, billing is active on your GCP project. Also, consider checking the Google.Cloud.TextToSpeech.V1 NuGet package version.");
                return false;
            }
            // NOTE: Explicit client.ShutdownAsync() or client.Dispose() has been removed here
            // as a temporary measure to bypass compiler errors CS8410/CS1674.
            // This relies on process termination or garbage collection for resource cleanup,
            // which is suboptimal but may be acceptable for a console app that exits.
            // This should be revisited.
        }

        /// <summary>
        /// Synthesizes speech using the built-in System.Speech.Synthesis.SpeechSynthesizer.
        /// This method is only functional on Windows platforms.
        /// </summary>
        private bool SynthesizeWithSystemSpeech(string text, string outputFilePath)
        {
            // Guard against using this API on non-Windows platforms using OperatingSystem.IsWindows().
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("TTS (SystemSpeech) Error: System.Speech.Synthesis is only supported on Windows. Skipping.");
                return false;
            }

            // The following code will only execute on Windows, satisfying CA1416.
            try
            {
                // Fully qualified name is used to avoid ambiguity.
                using (var synthesizer = new System.Speech.Synthesis.SpeechSynthesizer())
                {
                    synthesizer.SetOutputToWaveFile(outputFilePath);
                    Console.WriteLine($"TTS (SystemSpeech): Synthesizing text (approx {Math.Min(text.Length, 70)} chars): \"{text.Substring(0, Math.Min(text.Length, 70))}...\"");
                    synthesizer.Speak(text); // Synchronous synthesis
                    Console.WriteLine($"TTS (SystemSpeech): Speech synthesized and saved to {outputFilePath}");
                }
                return true;
            }
            catch (PlatformNotSupportedException ex) // This catch might be redundant due to the OS check, but kept as a safeguard.
            {
                Console.Error.WriteLine($"TTS (SystemSpeech) Error: PlatformNotSupportedException encountered even after OS check. {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"TTS (SystemSpeech) Exception: {ex.Message}");
                return false;
            }
        }
    }
}
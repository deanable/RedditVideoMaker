// TtsService.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
using System.Speech.Synthesis; // Requires System.Speech NuGet package
using System.Threading.Tasks;

namespace RedditVideoMaker.Core
{
    public class TtsService
    {
        public TtsService()
        {
            // Constructor can be used for initial setup if needed
        }

        /// <summary>
        /// Converts the given text to speech and saves it as a WAV file.
        /// This method runs synchronously because System.Speech.Synthesis is primarily synchronous.
        /// </summary>
        /// <param name="text">The text to convert to speech.</param>
        /// <param name="outputFilePath">The path where the WAV file will be saved.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public bool TextToSpeech(string text, string outputFilePath)
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

            try
            {
                // Ensure the output directory exists
                string? directory = Path.GetDirectoryName(outputFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Initialize a new instance of the SpeechSynthesizer.
                // This using statement ensures that the synthesizer is disposed of correctly.
                using (SpeechSynthesizer synthesizer = new SpeechSynthesizer())
                {
                    // Configure the audio output.
                    synthesizer.SetOutputToWaveFile(outputFilePath);

                    // Build a prompt and speak the text.
                    PromptBuilder builder = new PromptBuilder();
                    builder.AppendText(text);

                    Console.WriteLine($"TTS: Synthesizing speech for text: \"{text.Substring(0, Math.Min(text.Length, 50))}...\"");
                    synthesizer.Speak(builder);
                    Console.WriteLine($"TTS: Speech saved to {outputFilePath}");
                }
                return true;
            }
            catch (PlatformNotSupportedException pnsEx)
            {
                Console.Error.WriteLine($"TTS Error: System.Speech is not supported on this platform. {pnsEx.Message}");
                Console.Error.WriteLine("Note: System.Speech.Synthesis primarily works on Windows with installed SAPI voices.");
                return false;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"TTS Error: An unexpected error occurred: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Asynchronous wrapper for TextToSpeech to be easily callable with await.
        /// SpeechSynthesizer.SpeakAsync exists but SetOutputToWaveFile is synchronous.
        /// For true async file writing, more complex handling would be needed.
        /// This Task.Run is a simple way to make it awaitable without blocking the caller significantly
        /// if the TTS operation itself is lengthy, though the core Speak() to file is blocking.
        /// </summary>
        public Task<bool> TextToSpeechAsync(string text, string outputFilePath)
        {
            // Run the synchronous method on a thread pool thread
            return Task.Run(() => TextToSpeech(text, outputFilePath));
        }
    }
}

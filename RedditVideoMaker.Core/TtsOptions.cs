// TtsOptions.cs (in RedditVideoMaker.Core project)
namespace RedditVideoMaker.Core
{
    public class TtsOptions
    {
        public const string SectionName = "TtsOptions";

        /// <summary>
        /// Specifies the TTS engine to use.
        /// Possible values: "Azure", "SystemSpeech"
        /// </summary>
        public string Engine { get; set; } = "SystemSpeech"; // Default to SystemSpeech

        // Azure Options
        public string? AzureSpeechKey { get; set; }
        public string? AzureSpeechRegion { get; set; }
        public string? AzureVoiceName { get; set; } // Optional: e.g., "en-US-JennyNeural"
    }
}

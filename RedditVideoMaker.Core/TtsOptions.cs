// TtsOptions.cs (in RedditVideoMaker.Core project)
// Removed: using System.Collections.Generic; // This using statement was not needed for this file.

namespace RedditVideoMaker.Core
{
    /// <summary>
    /// Holds settings related to Text-to-Speech (TTS) services.
    /// These options determine which TTS engine is used and its specific configuration,
    /// such as API keys, regions, and voice preferences.
    /// </summary>
    public class TtsOptions
    {
        /// <summary>
        /// Defines the section name in the configuration file (e.g., appsettings.json)
        /// from which these options will be loaded.
        /// </summary>
        public const string SectionName = "TtsOptions";

        /// <summary>
        /// Gets or sets the preferred TTS engine to use.
        /// Supported values typically include "SystemSpeech", "Azure", "GoogleCloud".
        /// If an engine is specified but its required credentials (like API keys) are missing,
        /// the application may fall back to "SystemSpeech".
        /// Default is "SystemSpeech".
        /// </summary>
        public string Engine { get; set; } = "SystemSpeech";

        /// <summary>
        /// Gets or sets the API key for Azure Cognitive Services Speech.
        /// Required if <see cref="Engine"/> is set to "Azure".
        /// Default is null.
        /// </summary>
        public string? AzureSpeechKey { get; set; }

        /// <summary>
        /// Gets or sets the region for Azure Cognitive Services Speech (e.g., "eastus").
        /// Required if <see cref="Engine"/> is set to "Azure".
        /// Default is null.
        /// </summary>
        public string? AzureSpeechRegion { get; set; }

        /// <summary>
        /// Gets or sets the specific voice name to use with Azure Cognitive Services Speech
        /// (e.g., "en-US-ChristopherNeural").
        /// If null or empty, a default voice for the specified region/language may be used.
        /// Relevant if <see cref="Engine"/> is set to "Azure".
        /// Default is null.
        /// </summary>
        public string? AzureVoiceName { get; set; }

        /// <summary>
        /// Gets or sets the file path to the Google Cloud credentials JSON file.
        /// Required if <see cref="Engine"/> is set to "GoogleCloud".
        /// Default is null.
        /// </summary>
        public string? GoogleCloudCredentialsPath { get; set; }

        /// <summary>
        /// Gets or sets the specific voice name to use with Google Cloud Text-to-Speech
        /// (e.g., "en-US-Wavenet-D", "en-GB-News-K").
        /// If null or empty, a default voice for the specified <see cref="GoogleCloudLanguageCode"/> may be used.
        /// Relevant if <see cref="Engine"/> is set to "GoogleCloud".
        /// Default is null.
        /// </summary>
        public string? GoogleCloudVoiceName { get; set; }

        /// <summary>
        /// Gets or sets the language code to use with Google Cloud Text-to-Speech (e.g., "en-US", "en-GB").
        /// Relevant if <see cref="Engine"/> is set to "GoogleCloud".
        /// Default is "en-US".
        /// </summary>
        public string? GoogleCloudLanguageCode { get; set; } = "en-US";
    }
}
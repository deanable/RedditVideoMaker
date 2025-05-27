// TtsOptions.cs (in RedditVideoMaker.Core project)
using System.Collections.Generic;

namespace RedditVideoMaker.Core
{
    public class TtsOptions
    {
        public const string SectionName = "TtsOptions";
        public string Engine { get; set; } = "SystemSpeech";
        public string? AzureSpeechKey { get; set; }
        public string? AzureSpeechRegion { get; set; }
        public string? AzureVoiceName { get; set; }
        public string? GoogleCloudCredentialsPath { get; set; }
        public string? GoogleCloudVoiceName { get; set; }
        public string? GoogleCloudLanguageCode { get; set; } = "en-US";
    }
}

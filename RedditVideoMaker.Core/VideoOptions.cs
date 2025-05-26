// VideoOptions.cs (in RedditVideoMaker.Core project)
namespace RedditVideoMaker.Core
{
    public class VideoOptions
    {
        public const string SectionName = "VideoOptions";
        public string OutputResolution { get; set; } = "1080x1920";
        public string Theme { get; set; } = "dark";

        public string? BackgroundVideoPath { get; set; } // For visual background
        public int CardWidth { get; set; } = 800;
        public int CardHeight { get; set; } = 600;
        public string CardBackgroundColor { get; set; } = "DarkSlateGray";
        public string CardFontColor { get; set; } = "White";
        public string CardMetadataFontColor { get; set; } = "LightGray";
        public int NumberOfCommentsToInclude { get; set; } = 3;

        public bool EnableTransitions { get; set; } = true;
        public double TransitionDurationSeconds { get; set; } = 0.5;

        public float ContentTargetFontSize { get; set; } = 36f;
        public float ContentMinFontSize { get; set; } = 16f;
        public float ContentMaxFontSize { get; set; } = 60f;
        public float MetadataTargetFontSize { get; set; } = 24f;
        public float MetadataMinFontSize { get; set; } = 12f;
        public float MetadataMaxFontSize { get; set; } = 32f;

        public bool CleanUpIntermediateFiles { get; set; } = true;

        public string? IntroVideoPath { get; set; }
        public string? OutroVideoPath { get; set; }

        // New properties for Step 23: Background Music
        public string? BackgroundMusicFilePath { get; set; }
        public double BackgroundMusicVolume { get; set; } = 0.15; // Default to 15% volume
    }
}
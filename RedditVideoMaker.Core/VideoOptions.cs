// VideoOptions.cs (in RedditVideoMaker.Core project)
namespace RedditVideoMaker.Core
{
    public class VideoOptions
    {
        public const string SectionName = "VideoOptions";
        public string OutputResolution { get; set; } = "1920x1080";
        public string Theme { get; set; } = "dark";

        public string? BackgroundVideoPath { get; set; }
        public int CardWidth { get; set; } = 800;
        public int CardHeight { get; set; } = 600;
        public string CardBackgroundColor { get; set; } = "DarkSlateGray";
        public string CardFontColor { get; set; } = "White";
        public string CardMetadataFontColor { get; set; } = "LightGray"; // New: Font color for author/score
        public int NumberOfCommentsToInclude { get; set; } = 3;
    }
}

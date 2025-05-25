// VideoOptions.cs (in RedditVideoMaker.Core project)
namespace RedditVideoMaker.Core
{
    public class VideoOptions
    {
        public const string SectionName = "VideoOptions";
        public string OutputResolution { get; set; } = "1080x1920";
        public string Theme { get; set; } = "dark";

        public string? BackgroundVideoPath { get; set; }
        public int CardWidth { get; set; } = 800;
        public int CardHeight { get; set; } = 600;
        public string CardBackgroundColor { get; set; } = "DarkSlateGray";
        public string CardFontColor { get; set; } = "White";
        public int NumberOfCommentsToInclude { get; set; } = 3; // New: Number of comments for the video (default 3)
    }
}

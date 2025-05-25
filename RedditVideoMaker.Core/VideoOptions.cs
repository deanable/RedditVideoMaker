// VideoOptions.cs (in RedditVideoMaker.Core project)
namespace RedditVideoMaker.Core
{
    // This class will hold settings related to video output.
    public class VideoOptions
    {
        public const string SectionName = "VideoOptions"; // Convention to define section name

        // The desired output resolution for the video (e.g., "1080x1920").
        public string OutputResolution { get; set; } = "1080x1920"; // Default value

        // The theme to use for screenshots (e.g., "dark" or "light").
        public string Theme { get; set; } = "dark"; // Default value
    }
}
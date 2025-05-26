// GeneralOptions.cs (in RedditVideoMaker.Core project)
namespace RedditVideoMaker.Core
{
    public class GeneralOptions
    {
        public const string SectionName = "GeneralOptions";

        /// <summary>
        /// If true, the application runs in a testing/debug mode.
        /// This might involve using free/local TTS, skipping uploads, etc.
        /// </summary>
        public bool IsInTestingModule { get; set; } = false; // Default to false (production mode)
    }
}

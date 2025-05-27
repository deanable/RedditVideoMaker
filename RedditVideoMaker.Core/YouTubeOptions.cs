// YouTubeOptions.cs (in RedditVideoMaker.Core project)
using System.Collections.Generic;

namespace RedditVideoMaker.Core
{
    public class YouTubeOptions
    {
        public const string SectionName = "YouTubeOptions";

        public string? ClientSecretJsonPath { get; set; }
        public string DefaultVideoTitle { get; set; } = "Reddit Story Video";
        public string DefaultVideoDescription { get; set; } = "An interesting story from Reddit.";
        public List<string> DefaultVideoTags { get; set; } = new List<string> { "reddit", "story" };
        public string DefaultVideoCategoryId { get; set; } = "24";
        public string DefaultVideoPrivacyStatus { get; set; } = "private";

        // New properties for Step 28: Duplicate Upload Prevention
        /// <summary>
        /// If true, the application will check a local log to prevent re-uploading videos for the same Reddit post ID.
        /// </summary>
        public bool EnableDuplicateCheck { get; set; } = true; // Default to true

        /// <summary>
        /// Path to the file that logs IDs of successfully uploaded Reddit posts.
        /// Can be a relative path (e.g., "uploaded_posts.log") or an absolute path.
        /// </summary>
        public string UploadedPostsLogPath { get; set; } = "uploaded_post_ids.log";
    }
}

// YouTubeOptions.cs (in RedditVideoMaker.Core project)
using System.Collections.Generic; // Required for List<string>

namespace RedditVideoMaker.Core
{
    /// <summary>
    /// Holds settings related to uploading videos to YouTube.
    /// This includes API credentials, default video metadata, and duplicate upload prevention settings.
    /// </summary>
    public class YouTubeOptions
    {
        /// <summary>
        /// Defines the section name in the configuration file (e.g., appsettings.json)
        /// from which these options will be loaded.
        /// </summary>
        public const string SectionName = "YouTubeOptions";

        /// <summary>
        /// Gets or sets the file path to the Google Cloud client_secret.json file.
        /// This file contains the OAuth 2.0 credentials required to authenticate with the YouTube Data API.
        /// It is essential for video uploading.
        /// Default is null.
        /// </summary>
        public string? ClientSecretJsonPath { get; set; }

        /// <summary>
        /// Gets or sets the default title for uploaded YouTube videos.
        /// This can be overridden by more specific logic (e.g., using the Reddit post title).
        /// Default is "Reddit Story Video".
        /// </summary>
        public string DefaultVideoTitle { get; set; } = "Reddit Story Video";

        /// <summary>
        /// Gets or sets the default description for uploaded YouTube videos.
        /// This can be appended with more specific details from the Reddit post.
        /// Default is "An interesting story from Reddit.".
        /// </summary>
        public string DefaultVideoDescription { get; set; } = "An interesting story from Reddit.";

        /// <summary>
        /// Gets or sets a list of default tags for uploaded YouTube videos.
        /// Tags help with video discovery.
        /// Default is a list containing "reddit" and "story".
        /// </summary>
        public List<string> DefaultVideoTags { get; set; } = new List<string> { "reddit", "story" };

        /// <summary>
        /// Gets or sets the default YouTube video category ID.
        /// For example, "24" typically represents "Entertainment".
        /// Refer to the YouTube Data API documentation for a list of valid category IDs.
        /// Default is "24".
        /// </summary>
        public string DefaultVideoCategoryId { get; set; } = "24"; // "24" is Entertainment

        /// <summary>
        /// Gets or sets the default privacy status for uploaded YouTube videos.
        /// Valid values are "private", "unlisted", or "public".
        /// Default is "private".
        /// </summary>
        public string DefaultVideoPrivacyStatus { get; set; } = "private";

        /// <summary>
        /// Gets or sets a value indicating whether to enable duplicate upload checking.
        /// If true, the application will check a log file (specified by <see cref="UploadedPostsLogPath"/>)
        /// to see if a video for a given Reddit post ID has already been uploaded.
        /// Default is true.
        /// </summary>
        public bool EnableDuplicateCheck { get; set; } = true;

        /// <summary>
        /// Gets or sets the path to the file that logs the IDs of successfully processed and uploaded Reddit posts.
        /// This file is used by the duplicate check mechanism if <see cref="EnableDuplicateCheck"/> is true.
        /// Can be a relative path (e.g., "uploaded_post_ids.log") or an absolute path.
        /// Default is "uploaded_post_ids.log".
        /// </summary>
        public string UploadedPostsLogPath { get; set; } = "uploaded_post_ids.log";
    }
}
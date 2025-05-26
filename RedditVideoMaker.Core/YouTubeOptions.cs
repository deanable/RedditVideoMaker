// YouTubeOptions.cs (in RedditVideoMaker.Core project)
using System.Collections.Generic;

namespace RedditVideoMaker.Core
{
    public class YouTubeOptions
    {
        public const string SectionName = "YouTubeOptions";

        /// <summary>
        /// Path to the client_secret.json file downloaded from Google Cloud Console for OAuth 2.0.
        /// </summary>
        public string? ClientSecretJsonPath { get; set; }

        /// <summary>
        /// Default title for the uploaded YouTube video. Can be overridden.
        /// </summary>
        public string DefaultVideoTitle { get; set; } = "Reddit Story Video";

        /// <summary>
        /// Default description for the uploaded YouTube video.
        /// </summary>
        public string DefaultVideoDescription { get; set; } = "An interesting story from Reddit.";

        /// <summary>
        /// Default tags for the uploaded YouTube video.
        /// </summary>
        public List<string> DefaultVideoTags { get; set; } = new List<string> { "reddit", "story" };

        /// <summary>
        /// Default YouTube video category ID. 
        /// E.g., "22" for People & Blogs, "24" for Entertainment.
        /// See YouTube Data API documentation for a list of category IDs.
        /// </summary>
        public string DefaultVideoCategoryId { get; set; } = "24"; // Default to Entertainment

        /// <summary>
        /// Default privacy status for the uploaded video.
        /// Valid values: "private", "unlisted", "public".
        /// </summary>
        public string DefaultVideoPrivacyStatus { get; set; } = "private"; // Default to private
    }
}

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
    }
}

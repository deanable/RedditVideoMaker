// RedditOptions.cs (in RedditVideoMaker.Core project)
namespace RedditVideoMaker.Core
{
    // This class will hold settings related to Reddit.
    public class RedditOptions
    {
        public const string SectionName = "RedditOptions"; // Convention to define section name

        // The name of the subreddit to fetch posts from.
        public string Subreddit { get; set; } = "AskReddit"; // Default value

        // The ID of the post to fetch, or "latest" for the newest.
        public string PostId { get; set; } = "latest"; // Default value

        // Whether to allow NSFW (Not Safe For Work) content.
        public bool AllowNsfw { get; set; } = false; // Default value
    }
}

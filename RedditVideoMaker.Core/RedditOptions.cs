// RedditOptions.cs (in RedditVideoMaker.Core project)
using System.Collections.Generic; // Required for List<string>

namespace RedditVideoMaker.Core
{
    /// <summary>
    /// Holds settings related to fetching content from Reddit.
    /// These options control which subreddit, posts, and comments are selected for video generation.
    /// </summary>
    public class RedditOptions
    {
        /// <summary>
        /// Defines the section name in the configuration file (e.g., appsettings.json)
        /// from which these options will be loaded.
        /// </summary>
        public const string SectionName = "RedditOptions";

        /// <summary>
        /// Gets or sets the name of the subreddit to fetch posts from (e.g., "AskReddit").
        /// This is used if <see cref="PostUrl"/> is not specified.
        /// Default is "AskReddit".
        /// </summary>
        public string Subreddit { get; set; } = "AskReddit"; 

        /// <summary>
        /// Gets or sets the sort order or specific post identifier for fetching posts from the subreddit.
        /// Common values include "hot", "new", "top", "controversial".
        /// If <see cref="PostUrl"/> is specified, this setting is ignored.
        /// Default is "top".
        /// </summary>
        public string PostId { get; set; } = "top"; 

        /// <summary>
        /// Gets or sets a value indicating whether to allow Not Safe For Work (NSFW) posts.
        /// Default is false.
        /// </summary>
        public bool AllowNsfw { get; set; } = false; 

        /// <summary>
        /// Gets or sets a specific URL to a Reddit post.
        /// If provided, the application will attempt to fetch this specific post,
        /// and settings like <see cref="Subreddit"/> and <see cref="PostId"/> (for subreddit scanning) will be ignored.
        /// Default is null.
        /// </summary>
        public string? PostUrl { get; set; }

        /// <summary>
        /// Gets or sets the minimum number of upvotes a post must have to be considered.
        /// This filter is applied when scanning a subreddit for posts.
        /// It can be bypassed if <see cref="BypassPostFilters"/> is true or if <see cref="PostUrl"/> is used.
        /// Default is 0 (no minimum).
        /// </summary>
        public int MinPostUpvotes { get; set; } = 0; 

        /// <summary>
        /// Gets or sets the minimum number of comments a post must have to be considered.
        /// This filter is applied when scanning a subreddit for posts.
        /// It can be bypassed if <see cref="BypassPostFilters"/> is true or if <see cref="PostUrl"/> is used.
        /// Default is 0 (no minimum).
        /// </summary>
        public int MinPostCommentsCount { get; set; } = 0; 

        /// <summary>
        /// Gets or sets the start date for filtering posts (format YYYY-MM-DD).
        /// Posts created before this date will be excluded.
        /// This filter is applied when scanning a subreddit.
        /// If null or empty, no start date filter is applied.
        /// Default is null.
        /// </summary>
        public string? PostFilterStartDate { get; set; }

        /// <summary>
        /// Gets or sets the end date for filtering posts (format YYYY-MM-DD).
        /// Posts created on or after this date (effectively, up to the end of the day before this date) will be excluded.
        /// This filter is applied when scanning a subreddit.
        /// If null or empty, no end date filter is applied.
        /// Default is null.
        /// </summary>
        public string? PostFilterEndDate { get; set; }

        /// <summary>
        /// Gets or sets the minimum score a comment must have to be included in the video.
        /// Can be set to <see cref="int.MinValue"/> for no score filtering if <see cref="BypassCommentScoreFilter"/> is false.
        /// Default is <see cref="int.MinValue"/> (effectively no filter unless explicitly set higher).
        /// </summary>
        public int MinCommentScore { get; set; } = int.MinValue; 

        /// <summary>
        /// Gets or sets the number of posts to scan from the subreddit's listing (e.g., top 50 posts).
        /// This determines how many posts are initially fetched and then filtered.
        /// Default is 50.
        /// </summary>
        public int SubredditPostsToScan { get; set; } = 50; 

        /// <summary>
        /// Gets or sets a value indicating whether to bypass post filters like
        /// <see cref="MinPostUpvotes"/>, <see cref="MinPostCommentsCount"/>, date filters, and self-post/image checks.
        /// Useful if you want to process any post regardless of its stats, especially when using <see cref="PostUrl"/>.
        /// Default is false.
        /// </summary>
        public bool BypassPostFilters { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether to bypass the <see cref="MinCommentScore"/> filter.
        /// If true, comments will be included regardless of their score (but other filters like author "[deleted]" still apply).
        /// Default is false.
        /// </summary>
        public bool BypassCommentScoreFilter { get; set; } = false;

        /// <summary>
        /// Gets or sets the sort order for comments fetched for a post.
        /// Common values: "confidence", "top", "new", "controversial", "old", "qa".
        /// "top" usually yields highly-voted comments. "confidence" is best for controversial threads.
        /// Default is "top".
        /// </summary>
        public string CommentSortOrder { get; set; } = "top"; 

        /// <summary>
        /// Gets or sets a list of keywords to filter comments by.
        /// If the list is not empty, only comments containing at least one of these keywords (case-insensitive) will be included.
        /// If empty, no keyword filtering is applied to comments.
        /// Default is an empty list.
        /// </summary>
        public List<string> CommentIncludeKeywords { get; set; } = new List<string>(); 

        /// <summary>
        /// Gets or sets the number of videos/posts to attempt to process in a single run of the application.
        /// If fetching from a subreddit, the application will try to find up to this many posts that meet the criteria.
        /// Default is 1.
        /// </summary>
        public int NumberOfVideosInBatch { get; set; } = 1;
    }
}
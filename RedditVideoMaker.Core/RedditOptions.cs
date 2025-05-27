// RedditOptions.cs (in RedditVideoMaker.Core project)
using System.Collections.Generic;

namespace RedditVideoMaker.Core
{
    public class RedditOptions
    {
        public const string SectionName = "RedditOptions";
        public string Subreddit { get; set; } = "AskReddit";
        public string PostId { get; set; } = "top";
        public bool AllowNsfw { get; set; } = false;
        public string? PostUrl { get; set; }
        public int MinPostUpvotes { get; set; } = 0;
        public int MinPostCommentsCount { get; set; } = 0;
        public string? PostFilterStartDate { get; set; }
        public string? PostFilterEndDate { get; set; }
        public int MinCommentScore { get; set; } = int.MinValue;
        public int SubredditPostsToScan { get; set; } = 50;
        public bool BypassPostFilters { get; set; } = false;
        public bool BypassCommentScoreFilter { get; set; } = false;
        public string CommentSortOrder { get; set; } = "top";
        public List<string> CommentIncludeKeywords { get; set; } = new List<string>();
        public int NumberOfVideosInBatch { get; set; } = 1;
    }
}
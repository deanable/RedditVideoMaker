// RedditModels.cs (in RedditVideoMaker.Core project)
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace RedditVideoMaker.Core
{
    /// <summary>
    /// Represents the overall structure of a Reddit API listing response.
    /// This is a generic structure used for lists of posts and lists of comments.
    /// </summary>
    public class RedditListingResponse
    {
        /// <summary>
        /// Gets or sets the kind of the listing, indicating the type of data it contains (e.g., "Listing").
        /// </summary>
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        /// <summary>
        /// Gets or sets the data payload of the listing.
        /// </summary>
        [JsonPropertyName("data")]
        public RedditListingData? Data { get; set; }
    }

    /// <summary>
    /// Represents the 'data' part of a <see cref="RedditListingResponse"/>.
    /// It contains the actual items (children) and pagination information.
    /// </summary>
    public class RedditListingData
    {
        /// <summary>
        /// Gets or sets the list of child items in this listing.
        /// These can be posts (<see cref="RedditPostData"/>) or comments (<see cref="RedditCommentData"/>),
        /// contained within <see cref="RedditChild"/> wrappers.
        /// </summary>
        [JsonPropertyName("children")]
        public List<RedditChild>? Children { get; set; }

        /// <summary>
        /// Gets or sets the identifier for the next page of results, used for pagination.
        /// Null if there are no more results.
        /// </summary>
        [JsonPropertyName("after")]
        public string? After { get; set; }

        /// <summary>
        /// Gets or sets the identifier for the previous page of results, used for pagination.
        /// Null if this is the first page.
        /// </summary>
        [JsonPropertyName("before")]
        public string? Before { get; set; }
    }

    /// <summary>
    /// Represents a 'child' item within a <see cref="RedditListingData"/>.
    /// This acts as a wrapper, providing a 'kind' to identify the type of its 'Data' payload.
    /// </summary>
    public class RedditChild
    {
        /// <summary>
        /// Gets or sets the kind of the child item.
        /// Common kinds include "t3" for a link/post and "t1" for a comment.
        /// </summary>
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        /// <summary>
        /// Gets or sets the actual data payload of this child item.
        /// This property is of type <see cref="object"/> because its actual type (e.g., <see cref="RedditPostData"/> or <see cref="RedditCommentData"/>)
        /// depends on the <see cref="Kind"/> property or the context of the API request.
        /// Downstream code will typically deserialize or cast this to a specific type based on <see cref="Kind"/>.
        /// For example, if <see cref="Kind"/> is "t3", this <see cref="Data"/> is expected to be a <see cref="RedditPostData"/>.
        /// If <see cref="Kind"/> is "t1", this <see cref="Data"/> is expected to be a <see cref="RedditCommentData"/>.
        /// </summary>
        [JsonPropertyName("data")]
        public object? Data { get; set; }
    }

    /// <summary>
    /// Represents the detailed data of a Reddit post (also known as a "link" or "submission").
    /// </summary>
    public class RedditPostData
    {
        /// <summary>
        /// Gets or sets the title of the post.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the username of the post's author.
        /// </summary>
        [JsonPropertyName("author")]
        public string? Author { get; set; }

        /// <summary>
        /// Gets or sets the "fullname" of the post, which is a combination of its kind prefix (e.g., "t3_") and its ID.
        /// Example: "t3_abcdef".
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the unique base-36 ID of the post.
        /// Example: "abcdef".
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>
        /// Gets or sets the name of the subreddit where the post was submitted (without "r/" prefix).
        /// </summary>
        [JsonPropertyName("subreddit")]
        public string? Subreddit { get; set; }

        /// <summary>
        /// Gets or sets the score of the post (upvotes minus downvotes).
        /// </summary>
        [JsonPropertyName("score")]
        public int Score { get; set; }

        /// <summary>
        /// Gets or sets the permanent link (relative URL) to the post on Reddit.
        /// Example: "/r/subreddit/comments/abcdef/post_title_slug/".
        /// </summary>
        [JsonPropertyName("permalink")]
        public string? Permalink { get; set; }

        /// <summary>
        /// Gets or sets the direct URL of the link submitted.
        /// For self-posts, this URL points back to the post itself.
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        /// <summary>
        /// Gets or sets the self-text of the post, if it's a text post.
        /// This will be null or empty for link-only posts. The content is in Markdown format.
        /// </summary>
        [JsonPropertyName("selftext")]
        public string? Selftext { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the post contains a Reddit-hosted video.
        /// </summary>
        [JsonPropertyName("is_video")]
        public bool IsVideo { get; set; }

        /// <summary>
        /// Gets or sets the total number of comments on the post.
        /// </summary>
        [JsonPropertyName("num_comments")]
        public int NumberOfComments { get; set; }

        /// <summary>
        /// Gets or sets the Coordinated Universal Time (UTC) timestamp of when the post was created, represented as seconds since the Unix epoch.
        /// </summary>
        [JsonPropertyName("created_utc")]
        public double CreatedUtc { get; set; }
    }

    /// <summary>
    /// Represents the data of a Reddit comment.
    /// </summary>
    public class RedditCommentData
    {
        /// <summary>
        /// Gets or sets the unique base-36 ID of the comment.
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>
        /// Gets or sets the "fullname" of the comment, which is a combination of its kind prefix (e.g., "t1_") and its ID.
        /// Example: "t1_ghijkl".
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the username of the comment's author.
        /// Can be "[deleted]" if the author deleted their account or the comment.
        /// </summary>
        [JsonPropertyName("author")]
        public string? Author { get; set; }

        /// <summary>
        /// Gets or sets the body of the comment in Markdown format.
        /// </summary>
        [JsonPropertyName("body")]
        public string? Body { get; set; }

        /// <summary>
        /// Gets or sets the score of the comment (upvotes minus downvotes).
        /// </summary>
        [JsonPropertyName("score")]
        public int Score { get; set; }

        /// <summary>
        /// Gets or sets the Coordinated Universal Time (UTC) timestamp of when the comment was created, represented as seconds since the Unix epoch.
        /// </summary>
        [JsonPropertyName("created_utc")]
        public double CreatedUtc { get; set; }

        /// <summary>
        /// Gets or sets the permanent link (relative URL) to this specific comment.
        /// Example: "/r/subreddit/comments/abcdef/post_title_slug/ghijkl/".
        /// </summary>
        [JsonPropertyName("permalink")]
        public string? Permalink { get; set; }

        /// <summary>
        /// Gets or sets the depth of the comment in a comment thread (0 for top-level comments).
        /// </summary>
        [JsonPropertyName("depth")]
        public int Depth { get; set; }

        /// <summary>
        /// Gets or sets the replies to this comment.
        /// This property is of type <see cref="object"/> because its structure can vary:
        /// - It can be an empty string ("") if there are no replies.
        /// - It can be another <see cref="RedditListingResponse"/> object if there are replies.
        /// - It can be an object with a "kind": "more" for "load more comments" or "continue thread" links.
        /// Downstream code needs to inspect this object to determine how to handle it.
        /// </summary>
        [JsonPropertyName("replies")]
        public object? Replies { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the comment is "stickied" (pinned) by a moderator.
        /// </summary>
        [JsonPropertyName("stickied")]
        public bool IsStickied { get; set; }
    }
}
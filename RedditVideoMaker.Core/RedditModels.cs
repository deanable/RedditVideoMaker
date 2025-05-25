// RedditModels.cs (in RedditVideoMaker.Core project)
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace RedditVideoMaker.Core
{
    // Represents the overall structure of a Reddit API listing response
    // This is used for lists of posts AND lists of comments
    public class RedditListingResponse
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("data")]
        public RedditListingData? Data { get; set; }
    }

    // Represents the 'data' part of the main listing
    public class RedditListingData
    {
        [JsonPropertyName("children")]
        public List<RedditChild>? Children { get; set; } // Can contain RedditPostData or RedditCommentData

        [JsonPropertyName("after")]
        public string? After { get; set; }

        [JsonPropertyName("before")]
        public string? Before { get; set; }
    }

    // Represents a 'child' item in the listing
    // The 'Data' property will be dynamically typed or needs specific handling
    // For simplicity here, we'll rely on the 'kind' property to differentiate
    // if needed, or assume context (e.g. comments endpoint returns comment data)
    public class RedditChild
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; set; } // e.g., "t3" for a link post, "t1" for a comment

        // For GetFromJsonAsync to work directly with different types in Data,
        // you might need a more complex deserialization strategy or separate models
        // for post children vs comment children if their 'data' structure significantly differs
        // beyond what can be captured in a union-like way.
        // Here, we'll assume 'Data' can be deserialized into RedditPostData or RedditCommentData
        // based on the endpoint context.
        [JsonPropertyName("data")]
        public object? Data { get; set; } // Will be deserialized to RedditPostData or RedditCommentData
                                          // We will cast this later based on the 'kind' or context.
                                          // A more robust solution might use a custom JsonConverter.
    }

    // Represents the actual data of a Reddit post
    public class RedditPostData
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("author")]
        public string? Author { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("subreddit")]
        public string? Subreddit { get; set; }

        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("permalink")]
        public string? Permalink { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("selftext")]
        public string? Selftext { get; set; }

        [JsonPropertyName("is_video")]
        public bool IsVideo { get; set; }

        [JsonPropertyName("num_comments")]
        public int NumberOfComments { get; set; }

        [JsonPropertyName("created_utc")]
        public double CreatedUtc { get; set; }
    }

    // Represents the data of a Reddit comment
    public class RedditCommentData
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")] // Fullname, e.g., t1_abcdef
        public string? Name { get; set; }

        [JsonPropertyName("author")]
        public string? Author { get; set; }

        [JsonPropertyName("body")] // The actual comment text
        public string? Body { get; set; }

        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("created_utc")]
        public double CreatedUtc { get; set; }

        [JsonPropertyName("permalink")] // Relative URL to this specific comment
        public string? Permalink { get; set; }

        [JsonPropertyName("depth")] // Depth of the comment in a thread
        public int Depth { get; set; }

        // Replies to this comment. This can be complex.
        // It can be an empty string if no replies, or another RedditListingResponse.
        // For simplicity, we'll treat it as an object and handle it later if needed.
        // A value of "" (empty string) means no replies.
        // If it's a listing, it means there are replies.
        // Sometimes it's an object with a "kind": "more" for "load more comments" links.
        [JsonPropertyName("replies")]
        public object? Replies { get; set; } // Can be string (empty or "more") or RedditListingResponse

        public bool IsStickied { get; set; } // [JsonPropertyName("stickied")]
    }
}

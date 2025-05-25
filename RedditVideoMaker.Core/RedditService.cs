// RedditService.cs (in RedditVideoMaker.Core project)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json; // Required for JsonSerializer
using System.Threading.Tasks;
// using RedditVideoMaker.Core.Models; // Assuming models are in the same namespace

namespace RedditVideoMaker.Core
{
    public class RedditService
    {
        private static readonly HttpClient client = new HttpClient();
        private static readonly JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true // Helpful if Reddit API casing varies slightly
        };


        public RedditService()
        {
            if (client.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("CsharpRedditBot/0.1 by YourRedditUsername");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }
        }

        public async Task<List<RedditPostData>?> GetTopPostsAsync(string subreddit, int limit = 5)
        {
            if (string.IsNullOrWhiteSpace(subreddit))
            {
                Console.Error.WriteLine("Error: Subreddit cannot be empty.");
                return null;
            }
            string url = $"https://www.reddit.com/r/{subreddit}/top/.json?limit={limit}";
            Console.WriteLine($"Fetching top posts from URL: {url}");

            try
            {
                RedditListingResponse? listingResponse = await client.GetFromJsonAsync<RedditListingResponse>(url, jsonSerializerOptions);

                if (listingResponse?.Data?.Children != null)
                {
                    var posts = new List<RedditPostData>();
                    foreach (var child in listingResponse.Data.Children)
                    {
                        // The 'Data' property in RedditChild is 'object?'. We need to deserialize it properly.
                        if (child.Data is JsonElement element && child.Kind == "t3") // "t3" is a link/post
                        {
                            RedditPostData? postData = element.Deserialize<RedditPostData>(jsonSerializerOptions);
                            if (postData != null)
                            {
                                posts.Add(postData);
                            }
                        }
                    }
                    return posts;
                }
                else
                {
                    Console.WriteLine("No posts found or error in response structure for top posts.");
                    return new List<RedditPostData>();
                }
            }
            catch (HttpRequestException e)
            {
                Console.Error.WriteLine($"Request error fetching top posts: {e.Message}");
                // ... (existing error handling)
            }
            catch (JsonException e)
            {
                Console.Error.WriteLine($"JSON parsing error fetching top posts: {e.Message}");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"An unexpected error occurred fetching top posts: {e.Message}");
            }
            return null;
        }

        /// <summary>
        /// Fetches comments for a specific Reddit post.
        /// </summary>
        /// <param name="subreddit">The subreddit of the post.</param>
        /// <param name="postId">The ID of the post (without the "t3_" prefix).</param>
        /// <param name="commentLimit">Max number of top-level comments to fetch (Reddit default/max varies).</param>
        /// <param name="depth">Max depth of replies (not fully implemented here, Reddit default is usually deep).</param>
        /// <returns>A list of RedditCommentData objects, or null if an error occurs.</returns>
        public async Task<List<RedditCommentData>?> GetCommentsAsync(string subreddit, string postId, int commentLimit = 25)
        {
            if (string.IsNullOrWhiteSpace(subreddit) || string.IsNullOrWhiteSpace(postId))
            {
                Console.Error.WriteLine("Error: Subreddit and Post ID cannot be empty for fetching comments.");
                return null;
            }

            // Construct the URL for the post's comments JSON endpoint
            // Example: https://www.reddit.com/r/learnprogramming/comments/123xyz/slug.json?limit=25
            // The slug is optional.
            string url = $"https://www.reddit.com/r/{subreddit}/comments/{postId}.json?limit={commentLimit}";
            Console.WriteLine($"Fetching comments from URL: {url}");

            try
            {
                // The response for comments is an array of two listings:
                // [0]: The post itself
                // [1]: The comments
                List<RedditListingResponse>? responseListings = await client.GetFromJsonAsync<List<RedditListingResponse>>(url, jsonSerializerOptions);

                if (responseListings != null && responseListings.Count > 1)
                {
                    RedditListingResponse? commentListing = responseListings[1]; // Second element contains comments
                    if (commentListing?.Data?.Children != null)
                    {
                        var comments = new List<RedditCommentData>();
                        foreach (var child in commentListing.Data.Children)
                        {
                            if (child.Data is JsonElement element)
                            {
                                if (child.Kind == "t1") // "t1" is a comment
                                {
                                    RedditCommentData? commentData = element.Deserialize<RedditCommentData>(jsonSerializerOptions);
                                    if (commentData != null && !string.IsNullOrWhiteSpace(commentData.Body) && commentData.Author != "[deleted]")
                                    {
                                        comments.Add(commentData);
                                    }
                                }
                                else if (child.Kind == "more")
                                {
                                    // Handle "more" objects if needed (e.g., to load more replies)
                                    // For now, we'll just log it.
                                    // Console.WriteLine($"Encountered 'more' comments object (ID: {element.TryGetProperty("id", out var idEl) ? idEl.GetString() : "N/A"}).");
                                }
                            }
                        }
                        return comments;
                    }
                }
                Console.WriteLine("No comments found or error in comment response structure.");
                return new List<RedditCommentData>();
            }
            catch (HttpRequestException e)
            {
                Console.Error.WriteLine($"Request error fetching comments: {e.Message}");
                if (e.StatusCode.HasValue) Console.Error.WriteLine($"Status Code: {e.StatusCode.Value}");
            }
            catch (JsonException e)
            {
                Console.Error.WriteLine($"JSON parsing error fetching comments: {e.Message}");
                Console.Error.WriteLine($"Problematic URL: {url}");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"An unexpected error occurred fetching comments: {e.Message}");
            }
            return null;
        }
    }
}

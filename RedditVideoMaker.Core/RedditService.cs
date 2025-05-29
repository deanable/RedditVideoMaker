// RedditService.cs (in RedditVideoMaker.Core project)
using System;
using System.Collections.Generic;
using System.Globalization; // For DateTime parsing
using System.Linq; // For LINQ methods like OrderByDescending, FirstOrDefault
using System.Net.Http;
using System.Net.Http.Headers; // For MediaTypeWithQualityHeaderValue
using System.Net.Http.Json; // For ReadFromJsonAsync
using System.Text.Json; // Required for JsonElement and JsonSerializerOptions
using System.Text.RegularExpressions; // For Regex to parse PostUrl
using System.Threading.Tasks;
using Microsoft.Extensions.Options; // Required for IOptions

namespace RedditVideoMaker.Core
{
    /// <summary>
    /// Provides services for interacting with the Reddit API to fetch posts and comments.
    /// It uses configuration from <see cref="RedditOptions"/> to tailor requests and filter results.
    /// </summary>
    public class RedditService
    {
        // A static HttpClient is recommended for performance and to avoid socket exhaustion.
        private static readonly HttpClient _client = new HttpClient();
        private readonly RedditOptions _redditOptions;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="RedditService"/> class.
        /// </summary>
        /// <param name="redditOptions">The Reddit configuration options.</param>
        public RedditService(IOptions<RedditOptions> redditOptions)
        {
            _redditOptions = redditOptions.Value;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true // Allows matching JSON properties regardless of casing (e.g., "title" vs "Title")
            };

            // Initialize HttpClient headers only once.
            // Reddit API requires a custom User-Agent.
            if (_client.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                // IMPORTANT: Replace "YourRedditUsername" with your actual Reddit username or a unique bot identifier.
                // This is crucial for polite API usage as per Reddit's API rules.
                _client.DefaultRequestHeaders.UserAgent.ParseAdd($"CsharpRedditBot/0.7 by YourRedditUsername (config: {_redditOptions.Subreddit})");
                _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }
        }

        /// <summary>
        /// Fetches data for a single Reddit post given its subreddit and ID.
        /// </summary>
        /// <param name="subreddit">The name of the subreddit (e.g., "AskReddit").</param>
        /// <param name="postId">The ID of the post (e.g., "t3_abcdef").</param>
        /// <returns>A <see cref="RedditPostData"/> object if the post is found and parsed successfully; otherwise, null.</returns>
        private async Task<RedditPostData?> FetchSinglePostDataAsync(string subreddit, string postId)
        {
            // Construct the URL for the Reddit API endpoint for a specific post and its comments.
            // The .json extension is crucial for getting the JSON response.
            string url = $"https://www.reddit.com/r/{subreddit}/comments/{postId}.json";
            Console.WriteLine($"RedditService: Fetching single post from URL: {url}");

            try
            {
                HttpResponseMessage response = await _client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"RedditService: HTTP Error fetching post {url}. Status: {response.StatusCode}. Reason: {response.ReasonPhrase}");
                    // Optionally, log response content for debugging: await response.Content.ReadAsStringAsync();
                    return null;
                }

                // The response for a post/comments endpoint is an array of two RedditListingResponse objects:
                // 1. The first listing contains the post data itself (usually as a single child).
                // 2. The second listing contains the comments for that post.
                var responseListings = await response.Content.ReadFromJsonAsync<List<RedditListingResponse>>(_jsonOptions);

                if (responseListings != null && responseListings.Any())
                {
                    var postListing = responseListings[0]; // Post data is in the first listing
                    if (postListing?.Data?.Children != null && postListing.Data.Children.Any())
                    {
                        var postChild = postListing.Data.Children[0]; // The post is the first child
                        // The 'Data' property of RedditChild is an object, which System.Text.Json initially deserializes as a JsonElement.
                        // We need to further deserialize this JsonElement into our specific RedditPostData type.
                        if (postChild.Data is JsonElement element && postChild.Kind == "t3") // "t3" is the kind for posts.
                        {
                            return element.Deserialize<RedditPostData>(_jsonOptions);
                        }
                    }
                }
                Console.Error.WriteLine($"RedditService: Could not parse post data for {subreddit}/comments/{postId} from URL: {url}. Response structure might be unexpected.");
                return null;
            }
            catch (HttpRequestException httpEx)
            {
                Console.Error.WriteLine($"RedditService: HttpRequestException fetching single post {url}: {httpEx.Message} (StatusCode: {httpEx.StatusCode})");
                return null;
            }
            catch (JsonException jsonEx)
            {
                Console.Error.WriteLine($"RedditService: JsonException parsing single post {url}: {jsonEx.Message} (Path: {jsonEx.Path}, Line: {jsonEx.LineNumber}, Pos: {jsonEx.BytePositionInLine})");
                return null;
            }
            catch (NotSupportedException nsEx) // Can be thrown by ReadFromJsonAsync if content type is not supported (e.g., not JSON).
            {
                Console.Error.WriteLine($"RedditService: NotSupportedException (likely invalid content type) parsing single post {url}: {nsEx.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"RedditService: General Error fetching single post {url}: {ex.ToString()}");
                return null;
            }
        }

        /// <summary>
        /// Parses a Reddit post URL to extract the subreddit name and post ID.
        /// </summary>
        /// <param name="postUrl">The full URL of the Reddit post.</param>
        /// <returns>A tuple containing the subreddit name and post ID. Both can be null if parsing fails.</returns>
        private (string? Subreddit, string? PostId) ParseRedditPostUrl(string postUrl)
        {
            // Regex to capture subreddit and post ID from various Reddit URL formats.
            // Example: https://www.reddit.com/r/learnprogramming/comments/q4z1k2/any_good_resources_for_learning_c/
            var regex = new Regex(@"reddit\.com/r/(?<subreddit>[^/]+)/comments/(?<postid>[^/]+)/?", RegexOptions.IgnoreCase);
            var match = regex.Match(postUrl);

            if (match.Success)
            {
                return (match.Groups["subreddit"].Value, match.Groups["postid"].Value);
            }
            Console.Error.WriteLine($"RedditService: Could not parse subreddit and post ID from URL: {postUrl}");
            return (null, null);
        }

        /// <summary>
        /// Gets a list of top Reddit posts based on the configured options.
        /// It can either fetch a specific post by URL or scan a subreddit for posts meeting criteria.
        /// </summary>
        /// <returns>A list of <see cref="RedditPostData"/> objects. The list may be empty if no suitable posts are found.</returns>
        public async Task<List<RedditPostData>> GetTopPostsAsync()
        {
            var selectedPostsResult = new List<RedditPostData>();

            // Scenario 1: A specific post URL is provided in options.
            if (!string.IsNullOrWhiteSpace(_redditOptions.PostUrl))
            {
                Console.WriteLine($"RedditService: Attempting to fetch specific post from URL: {_redditOptions.PostUrl}");
                var (subreddit, postId) = ParseRedditPostUrl(_redditOptions.PostUrl);

                if (!string.IsNullOrWhiteSpace(subreddit) && !string.IsNullOrWhiteSpace(postId))
                {
                    RedditPostData? specificPost = await FetchSinglePostDataAsync(subreddit, postId);
                    if (specificPost != null)
                    {
                        bool meetsCriteria = true;
                        // Apply filters unless bypassed.
                        if (!_redditOptions.BypassPostFilters)
                        {
                            if (_redditOptions.MinPostCommentsCount > 0 && specificPost.NumberOfComments < _redditOptions.MinPostCommentsCount)
                            {
                                Console.WriteLine($"RedditService: Post from URL {specificPost.Id} has {specificPost.NumberOfComments} comments, less than MinPostCommentsCount of {_redditOptions.MinPostCommentsCount}.");
                                meetsCriteria = false;
                            }
                            if (meetsCriteria && _redditOptions.MinPostUpvotes > 0 && specificPost.Score < _redditOptions.MinPostUpvotes)
                            {
                                Console.WriteLine($"RedditService: Post from URL {specificPost.Id} has {specificPost.Score} upvotes, less than MinPostUpvotes of {_redditOptions.MinPostUpvotes}.");
                                meetsCriteria = false;
                            }
                            // Note: Date filters and self-post/image checks are typically for subreddit scans,
                            // but could be applied here if desired for consistency.
                        }

                        if (meetsCriteria)
                        {
                            selectedPostsResult.Add(specificPost);
                        }
                        else
                        {
                            Console.WriteLine($"RedditService: Specified post from URL does not meet all configured criteria and filters are not bypassed. Not processing.");
                        }
                    }
                    // FetchSinglePostDataAsync and ParseRedditPostUrl log their own errors if they fail.
                }
            }
            // Scenario 2: No specific PostUrl, scan a subreddit.
            else
            {
                Console.WriteLine($"RedditService: Scanning /r/{_redditOptions.Subreddit} for posts. Sort: '{_redditOptions.PostId}', Scan Limit: {_redditOptions.SubredditPostsToScan}");
                // Construct URL for subreddit listing (e.g., top, hot, new) with a limit.
                string url = $"https://www.reddit.com/r/{_redditOptions.Subreddit}/{_redditOptions.PostId}/.json?limit={_redditOptions.SubredditPostsToScan}";
                try
                {
                    HttpResponseMessage response = await _client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.Error.WriteLine($"RedditService: HTTP Error fetching posts from {url}. Status: {response.StatusCode}. Reason: {response.ReasonPhrase}");
                        return selectedPostsResult; // Return empty list
                    }

                    RedditListingResponse? listingResponse = await response.Content.ReadFromJsonAsync<RedditListingResponse>(_jsonOptions);

                    if (listingResponse?.Data?.Children != null)
                    {
                        var candidates = new List<RedditPostData>();
                        foreach (var child in listingResponse.Data.Children)
                        {
                            if (child.Data is JsonElement element && child.Kind == "t3") // "t3" for posts
                            {
                                RedditPostData? postData = element.Deserialize<RedditPostData>(_jsonOptions);
                                if (postData != null)
                                {
                                    // If bypassing filters, add directly.
                                    if (_redditOptions.BypassPostFilters)
                                    {
                                        candidates.Add(postData);
                                        continue;
                                    }

                                    // Apply Date Filters
                                    bool dateFilterPassed = true;
                                    if (!string.IsNullOrWhiteSpace(_redditOptions.PostFilterStartDate) &&
                                        DateTime.TryParseExact(_redditOptions.PostFilterStartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime startDate))
                                    {
                                        if (DateTimeOffset.FromUnixTimeSeconds((long)postData.CreatedUtc).UtcDateTime < startDate)
                                        { dateFilterPassed = false; }
                                    }
                                    if (dateFilterPassed && !string.IsNullOrWhiteSpace(_redditOptions.PostFilterEndDate) &&
                                        DateTime.TryParseExact(_redditOptions.PostFilterEndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime endDate))
                                    {
                                        // End date is inclusive of the day, so compare with the start of the next day.
                                        if (DateTimeOffset.FromUnixTimeSeconds((long)postData.CreatedUtc).UtcDateTime >= endDate.AddDays(1))
                                        { dateFilterPassed = false; }
                                    }

                                    // Apply Comments Count Filter
                                    bool commentsCountPassed = _redditOptions.MinPostCommentsCount <= 0 || postData.NumberOfComments >= _redditOptions.MinPostCommentsCount;
                                    // Apply Upvotes Filter
                                    bool upvotesPassed = _redditOptions.MinPostUpvotes <= 0 || postData.Score >= _redditOptions.MinPostUpvotes;
                                    // Apply Post Type Filter (self-post or direct image link, exclude Reddit videos or external links)
                                    // A self-post URL contains "/r/subreddit/comments/"
                                    // A direct image URL ends with .jpeg, .jpg, .gif, .png
                                    bool isSelfPostOrDirectImage = !postData.IsVideo &&
                                                                   (postData.Url != null &&
                                                                    (postData.Url.Contains($"/r/{postData.Subreddit}/comments/") ||
                                                                     Regex.IsMatch(postData.Url, @"\.(jpeg|jpg|gif|png)$", RegexOptions.IgnoreCase)));


                                    if (dateFilterPassed && commentsCountPassed && upvotesPassed && isSelfPostOrDirectImage)
                                    {
                                        candidates.Add(postData);
                                    }
                                }
                            }
                        }
                        Console.WriteLine($"RedditService: Found {candidates.Count} candidate posts after all filters (BypassPostFilters: {_redditOptions.BypassPostFilters}).");

                        // Select top N posts from candidates based on score for batch processing.
                        if (candidates.Any())
                        {
                            selectedPostsResult.AddRange(candidates
                                .OrderByDescending(p => p.Score) // Order by highest score
                                .Take(_redditOptions.NumberOfVideosInBatch) // Take the configured number for the batch
                                .ToList());
                            Console.WriteLine($"RedditService: Selected {selectedPostsResult.Count} posts for batch processing based on score.");
                        }
                    }
                }
                catch (HttpRequestException httpEx) { Console.Error.WriteLine($"RedditService: HttpRequestException fetching posts from {url}: {httpEx.Message} (StatusCode: {httpEx.StatusCode})"); }
                catch (JsonException jsonEx) { Console.Error.WriteLine($"RedditService: JsonException parsing posts from {url}: {jsonEx.Message}"); }
                catch (NotSupportedException nsEx) { Console.Error.WriteLine($"RedditService: NotSupportedException (likely invalid content type) parsing posts from {url}: {nsEx.Message}"); }
                catch (Exception ex) { Console.Error.WriteLine($"RedditService: General Error fetching posts from {url}: {ex.ToString()}"); }
            }

            if (!selectedPostsResult.Any())
            {
                Console.Error.WriteLine("RedditService: No suitable post(s) found based on current criteria and filters.");
            }
            return selectedPostsResult;
        }

        /// <summary>
        /// Fetches comments for a given Reddit post.
        /// </summary>
        /// <param name="subreddit">The subreddit of the post.</param>
        /// <param name="postId">The ID of the post.</param>
        /// <param name="commentFetchLimit">The maximum number of comments to attempt to fetch from the API before filtering. Default is 100.</param>
        /// <returns>A list of <see cref="RedditCommentData"/> objects, or null if an error occurs during fetching.</returns>
        public async Task<List<RedditCommentData>?> GetCommentsAsync(string subreddit, string postId, int commentFetchLimit = 100)
        {
            if (string.IsNullOrWhiteSpace(subreddit) || string.IsNullOrWhiteSpace(postId))
            {
                Console.Error.WriteLine("RedditService Error: Subreddit and Post ID cannot be empty for fetching comments.");
                return null;
            }

            // Construct API URL for comments.
            // depth=1 fetches only top-level comments.
            // sort can be: confidence, top, new, controversial, old, qa.
            string url = $"https://www.reddit.com/r/{subreddit}/comments/{postId}.json?limit={commentFetchLimit}&depth=1&sort={_redditOptions.CommentSortOrder}";
            Console.WriteLine($"RedditService: Fetching comments from URL: {url}");

            try
            {
                HttpResponseMessage response = await _client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"RedditService: HTTP Error fetching comments from {url}. Status: {response.StatusCode}. Reason: {response.ReasonPhrase}");
                    return null;
                }

                // As with single post fetching, the comments endpoint returns an array of two listings.
                // The first is the post data, the second is the comment data.
                var responseListings = await response.Content.ReadFromJsonAsync<List<RedditListingResponse>>(_jsonOptions);

                if (responseListings != null && responseListings.Count > 1)
                {
                    RedditListingResponse? commentListing = responseListings[1]; // Comments are in the second listing.
                    if (commentListing?.Data?.Children != null)
                    {
                        var comments = new List<RedditCommentData>();
                        foreach (var child in commentListing.Data.Children)
                        {
                            // "t1" is the kind for comments.
                            if (child.Data is JsonElement element && child.Kind == "t1")
                            {
                                RedditCommentData? commentData = element.Deserialize<RedditCommentData>(_jsonOptions);

                                // Initial filter for valid, non-deleted, non-stickied comments.
                                bool includeThisComment = commentData != null &&
                                                          !string.IsNullOrWhiteSpace(commentData.Body) &&
                                                          commentData.Author != "[deleted]" && // Exclude deleted authors
                                                          !commentData.IsStickied; // Exclude moderator-stickied comments

                                // Apply minimum score filter if not bypassed.
                                if (includeThisComment && !_redditOptions.BypassCommentScoreFilter)
                                {
                                    includeThisComment = _redditOptions.MinCommentScore == int.MinValue || // Allow if MinCommentScore is effectively "no filter"
                                                         (commentData != null && commentData.Score >= _redditOptions.MinCommentScore);
                                }

                                // Apply keyword inclusion filter if keywords are specified.
                                if (includeThisComment && _redditOptions.CommentIncludeKeywords.Any())
                                {
                                    includeThisComment = commentData != null && !string.IsNullOrWhiteSpace(commentData.Body) &&
                                                         _redditOptions.CommentIncludeKeywords
                                                            .Any(keyword => commentData.Body.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                                }

                                if (includeThisComment && commentData != null)
                                {
                                    comments.Add(commentData);
                                }
                            }
                            // Could also handle "more" kind (child.Kind == "more") here if deep comment fetching was needed.
                        }
                        Console.WriteLine($"RedditService: Fetched and filtered {comments.Count} comments for post {postId}.");
                        return comments;
                    }
                }
                Console.WriteLine($"RedditService: No comments found or error in comment response structure for post {postId} from URL: {url}.");
                return new List<RedditCommentData>(); // Return empty list if no comments or parse error
            }
            catch (HttpRequestException httpEx) { Console.Error.WriteLine($"RedditService: HttpRequestException fetching comments for {url}: {httpEx.Message} (StatusCode: {httpEx.StatusCode})"); }
            catch (JsonException jsonEx) { Console.Error.WriteLine($"RedditService: JsonException parsing comments for {url}: {jsonEx.Message}"); }
            catch (NotSupportedException nsEx) { Console.Error.WriteLine($"RedditService: NotSupportedException (likely invalid content type) parsing comments for {url}: {nsEx.Message}"); }
            catch (Exception ex) { Console.Error.WriteLine($"RedditService: General Error fetching comments for {url}: {ex.ToString()}"); }

            return null; // Return null on error
        }
    }
}
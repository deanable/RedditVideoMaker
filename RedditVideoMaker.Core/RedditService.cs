// RedditService.cs (in RedditVideoMaker.Core project)
using System;
using System.Collections.Generic;
using System.Globalization; // For DateTime parsing
using System.Linq; // For LINQ methods like OrderByDescending, FirstOrDefault
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json; // Required for JsonElement and JsonSerializerOptions
using System.Text.RegularExpressions; // For Regex to parse PostUrl
using System.Threading.Tasks;
using Microsoft.Extensions.Options; // Required for IOptions

namespace RedditVideoMaker.Core
{
    public class RedditService
    {
        private static readonly HttpClient _client = new HttpClient();
        private readonly RedditOptions _redditOptions;
        private readonly JsonSerializerOptions _jsonOptions;

        public RedditService(IOptions<RedditOptions> redditOptions)
        {
            _redditOptions = redditOptions.Value;
            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            if (_client.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                _client.DefaultRequestHeaders.UserAgent.ParseAdd($"CsharpRedditBot/0.6 by YourRedditUsername (config: {_redditOptions.Subreddit})");
                _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }
        }

        private async Task<RedditPostData?> FetchSinglePostDataAsync(string subreddit, string postId)
        {
            string url = $"https://www.reddit.com/r/{subreddit}/comments/{postId}.json";
            Console.WriteLine($"RedditService: Fetching single post from URL: {url}");
            try
            {
                // Using GetAsync and then ReadFromJsonAsync for more control over error response
                HttpResponseMessage response = await _client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"RedditService: HTTP Error fetching post {url}. Status: {response.StatusCode}. Reason: {response.ReasonPhrase}");
                    // Optionally, read and log response content for debugging if needed, e.g., await response.Content.ReadAsStringAsync();
                    return null;
                }

                // The response for comments endpoint is an array of two listings.
                var responseListings = await response.Content.ReadFromJsonAsync<List<RedditListingResponse>>(_jsonOptions);

                if (responseListings != null && responseListings.Any())
                {
                    var postListing = responseListings[0];
                    if (postListing?.Data?.Children != null && postListing.Data.Children.Any())
                    {
                        var postChild = postListing.Data.Children[0];
                        if (postChild.Data is JsonElement element && postChild.Kind == "t3")
                        {
                            return element.Deserialize<RedditPostData>(_jsonOptions);
                        }
                    }
                }
                Console.Error.WriteLine($"RedditService: Could not parse post data for {subreddit}/comments/{postId} from URL: {url}");
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
            catch (NotSupportedException nsEx) // Can be thrown by ReadFromJsonAsync if content type is not JSON
            {
                Console.Error.WriteLine($"RedditService: NotSupportedException (likely invalid content type) parsing single post {url}: {nsEx.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"RedditService: General Error fetching single post {url}: {ex.ToString()}"); // Log full exception details
                return null;
            }
        }

        private (string? Subreddit, string? PostId) ParseRedditPostUrl(string postUrl)
        {
            var regex = new Regex(@"reddit\.com/r/(?<subreddit>[^/]+)/comments/(?<postid>[^/]+)/?", RegexOptions.IgnoreCase);
            var match = regex.Match(postUrl);

            if (match.Success)
            {
                return (match.Groups["subreddit"].Value, match.Groups["postid"].Value);
            }
            Console.Error.WriteLine($"RedditService: Could not parse subreddit and post ID from URL: {postUrl}");
            return (null, null);
        }

        public async Task<List<RedditPostData>> GetTopPostsAsync()
        {
            var selectedPostsResult = new List<RedditPostData>();

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
                    // FetchSinglePostDataAsync already logs its errors
                }
                // ParseRedditPostUrl already logs its errors
            }
            else
            {
                Console.WriteLine($"RedditService: Scanning /r/{_redditOptions.Subreddit} for posts. Sort: '{_redditOptions.PostId}', Scan Limit: {_redditOptions.SubredditPostsToScan}");
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
                            if (child.Data is JsonElement element && child.Kind == "t3")
                            {
                                RedditPostData? postData = element.Deserialize<RedditPostData>(_jsonOptions);
                                if (postData != null)
                                {
                                    if (_redditOptions.BypassPostFilters)
                                    {
                                        candidates.Add(postData);
                                        continue;
                                    }

                                    bool dateFilterPassed = true;
                                    if (!string.IsNullOrWhiteSpace(_redditOptions.PostFilterStartDate) &&
                                        DateTime.TryParseExact(_redditOptions.PostFilterStartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime startDate))
                                    {
                                        if (DateTimeOffset.FromUnixTimeSeconds((long)postData.CreatedUtc).UtcDateTime < startDate)
                                        { dateFilterPassed = false; }
                                    }
                                    if (dateFilterPassed && !string.IsNullOrWhiteSpace(_redditOptions.PostFilterEndDate) &&
                                        DateTime.TryParseExact(_redditOptions.PostFilterEndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime endDate))
                                    {
                                        if (DateTimeOffset.FromUnixTimeSeconds((long)postData.CreatedUtc).UtcDateTime >= endDate.AddDays(1))
                                        { dateFilterPassed = false; }
                                    }

                                    bool commentsCountPassed = _redditOptions.MinPostCommentsCount <= 0 || postData.NumberOfComments >= _redditOptions.MinPostCommentsCount;
                                    bool upvotesPassed = _redditOptions.MinPostUpvotes <= 0 || postData.Score >= _redditOptions.MinPostUpvotes;
                                    bool isSelfPostOrDirectImage = !postData.IsVideo && (postData.Url.Contains($"/r/{postData.Subreddit}/comments/") || Regex.IsMatch(postData.Url, @"\.(jpeg|jpg|gif|png)$", RegexOptions.IgnoreCase));

                                    if (dateFilterPassed && commentsCountPassed && upvotesPassed && isSelfPostOrDirectImage)
                                    {
                                        candidates.Add(postData);
                                    }
                                }
                            }
                        }
                        Console.WriteLine($"RedditService: Found {candidates.Count} candidate posts after all filters (BypassPostFilters: {_redditOptions.BypassPostFilters}).");
                        if (candidates.Any())
                        {
                            selectedPostsResult.AddRange(candidates
                                .OrderByDescending(p => p.Score)
                                .Take(_redditOptions.NumberOfVideosInBatch)
                                .ToList());
                            Console.WriteLine($"RedditService: Selected {selectedPostsResult.Count} posts for batch processing.");
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

        public async Task<List<RedditCommentData>?> GetCommentsAsync(string subreddit, string postId, int commentFetchLimit = 100)
        {
            if (string.IsNullOrWhiteSpace(subreddit) || string.IsNullOrWhiteSpace(postId))
            {
                Console.Error.WriteLine("RedditService Error: Subreddit and Post ID cannot be empty for fetching comments.");
                return null;
            }
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

                List<RedditListingResponse>? responseListings = await response.Content.ReadFromJsonAsync<List<RedditListingResponse>>(_jsonOptions);

                if (responseListings != null && responseListings.Count > 1)
                {
                    RedditListingResponse? commentListing = responseListings[1];
                    if (commentListing?.Data?.Children != null)
                    {
                        var comments = new List<RedditCommentData>();
                        foreach (var child in commentListing.Data.Children)
                        {
                            if (child.Data is JsonElement element && child.Kind == "t1")
                            {
                                RedditCommentData? commentData = element.Deserialize<RedditCommentData>(_jsonOptions);
                                bool includeThisComment = commentData != null &&
                                                          !string.IsNullOrWhiteSpace(commentData.Body) &&
                                                          commentData.Author != "[deleted]" &&
                                                          !commentData.IsStickied;

                                if (includeThisComment && !_redditOptions.BypassCommentScoreFilter)
                                {
                                    includeThisComment = _redditOptions.MinCommentScore == int.MinValue || commentData!.Score >= _redditOptions.MinCommentScore;
                                }

                                if (includeThisComment && _redditOptions.CommentIncludeKeywords.Any())
                                {
                                    includeThisComment = _redditOptions.CommentIncludeKeywords
                                        .Any(keyword => commentData!.Body!.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                                }

                                if (includeThisComment)
                                {
                                    comments.Add(commentData!);
                                }
                            }
                        }
                        Console.WriteLine($"RedditService: Fetched {comments.Count} comments after all filters for post {postId}.");
                        return comments;
                    }
                }
                Console.WriteLine($"RedditService: No comments found or error in comment response structure for post {postId} from URL: {url}.");
                return new List<RedditCommentData>();
            }
            catch (HttpRequestException httpEx) { Console.Error.WriteLine($"RedditService: HttpRequestException fetching comments for {url}: {httpEx.Message} (StatusCode: {httpEx.StatusCode})"); }
            catch (JsonException jsonEx) { Console.Error.WriteLine($"RedditService: JsonException parsing comments for {url}: {jsonEx.Message}"); }
            catch (NotSupportedException nsEx) { Console.Error.WriteLine($"RedditService: NotSupportedException (likely invalid content type) parsing comments for {url}: {nsEx.Message}"); }
            catch (Exception ex) { Console.Error.WriteLine($"RedditService: General Error fetching comments for {url}: {ex.ToString()}"); }
            return null;
        }
    }
}

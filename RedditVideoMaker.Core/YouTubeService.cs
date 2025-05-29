// YouTubeService.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
// using System.Linq; // Not actively used by the logic in this file.
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2; // For UserCredential, GoogleWebAuthorizationBroker, GoogleClientSecrets
using Google.Apis.Services; // For BaseClientService
using Google.Apis.Upload; // For IUploadProgress, UploadStatus
using Google.Apis.YouTube.v3; // For YouTubeService and its Scope enum
using Google.Apis.YouTube.v3.Data; // For Video, VideoSnippet, VideoStatus
using Microsoft.Extensions.Options; // For IOptions

namespace RedditVideoMaker.Core
{
    /// <summary>
    /// Provides services for interacting with the YouTube Data API v3,
    /// specifically for authorizing and uploading videos.
    /// </summary>
    public class YouTubeService // This is our custom service class, distinct from Google.Apis.YouTube.v3.YouTubeService
    {
        private readonly YouTubeOptions _youTubeOptions;

        // Folder name within Environment.SpecialFolder.ApplicationData to store OAuth 2.0 user credentials.
        private const string UserCredentialsFolderName = "MyYouTubeCredentials";

        /// <summary>
        /// Initializes a new instance of the <see cref="YouTubeService"/> class.
        /// </summary>
        /// <param name="youTubeOptions">The YouTube configuration options, including API keys and default video settings.</param>
        public YouTubeService(IOptions<YouTubeOptions> youTubeOptions)
        {
            _youTubeOptions = youTubeOptions.Value;
        }

        /// <summary>
        /// Authorizes the application to access YouTube Data API on behalf of the user using OAuth 2.0.
        /// Credentials are obtained using a client secrets JSON file and stored locally for future use.
        /// The first time this runs, it will likely open a browser window for user consent.
        /// </summary>
        /// <returns>A <see cref="UserCredential"/> object containing the OAuth 2.0 access and refresh tokens.</returns>
        /// <exception cref="FileNotFoundException">Thrown if the client_secret.json file is not found or not configured.</exception>
        private async Task<UserCredential> AuthorizeAsync()
        {
            if (string.IsNullOrWhiteSpace(_youTubeOptions.ClientSecretJsonPath) || !File.Exists(_youTubeOptions.ClientSecretJsonPath))
            {
                string errorMessage = "YouTube client_secret.json path is not configured or the file was not found. " +
                                      "Please check appsettings.json and ensure the path is correct.";
                Console.Error.WriteLine($"YouTubeService Critical Error: {errorMessage} Path attempted: '{_youTubeOptions.ClientSecretJsonPath ?? "NULL"}'");
                throw new FileNotFoundException(errorMessage, _youTubeOptions.ClientSecretJsonPath);
            }

            UserCredential credential;
            using (var stream = new FileStream(_youTubeOptions.ClientSecretJsonPath, FileMode.Open, FileAccess.Read))
            {
                // Define the path where the user's OAuth token will be stored.
                // This is typically in a user-specific application data folder.
                string credentialPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), UserCredentialsFolderName);
                Console.WriteLine($"YouTubeService: Attempting to load/save user credentials from/to: {credentialPath}");

                // Request authorization from the user.
                // This method handles token refresh automatically if a refresh token is available.
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    // Scopes define the permissions the application requests.
                    // YoutubeUpload: Allows uploading videos.
                    // Youtube: General access, might be needed for setting some metadata or could be more specific.
                    new[] { Google.Apis.YouTube.v3.YouTubeService.Scope.YoutubeUpload, Google.Apis.YouTube.v3.YouTubeService.Scope.Youtube },
                    "user", // User identifier for the DataStore. "user" is common for single-user desktop apps.
                    CancellationToken.None,
                    new Google.Apis.Util.Store.FileDataStore(credentialPath, true) // Stores tokens in the specified path.
                );
                Console.WriteLine($"YouTubeService: User credential authorization successful. Credential file potentially created/updated at: {credentialPath}");
            }
            return credential;
        }

        /// <summary>
        /// Uploads a video file to YouTube with the specified metadata.
        /// </summary>
        /// <param name="videoPath">The local file path of the video to upload.</param>
        /// <param name="title">The title for the YouTube video.</param>
        /// <param name="description">The description for the YouTube video.</param>
        /// <param name="tags">An array of tags (keywords) for the YouTube video.</param>
        /// <param name="categoryId">The YouTube category ID for the video (e.g., "24" for Entertainment).</param>
        /// <param name="privacyStatus">The privacy status of the video ("private", "unlisted", or "public").</param>
        /// <returns>The uploaded <see cref="Video"/> object from YouTube if successful; otherwise, null.</returns>
        public async Task<Video?> UploadVideoAsync(string videoPath, string title, string description, string[] tags, string categoryId, string privacyStatus)
        {
            if (!File.Exists(videoPath))
            {
                Console.Error.WriteLine($"YouTubeService Error: Video file not found at '{videoPath}'. Cannot upload.");
                return null;
            }

            try
            {
                // Authorize the application.
                UserCredential credential = await AuthorizeAsync();

                // Create the YouTube Data API service instance.
                var youtubeApiService = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "Reddit Video Maker Bot C#" // Application name for API tracking.
                });

                // Create a Video object and set its snippet (metadata) and status (privacy).
                var videoToUpload = new Video();
                videoToUpload.Snippet = new VideoSnippet
                {
                    Title = title,
                    Description = description,
                    Tags = tags,
                    CategoryId = categoryId
                };
                videoToUpload.Status = new VideoStatus
                {
                    PrivacyStatus = privacyStatus
                };

                Console.WriteLine($"YouTubeService: Starting upload of '{Path.GetFileName(videoPath)}'. Title: '{title}'");

                // Open a stream to the video file.
                using (var fileStream = new FileStream(videoPath, FileMode.Open, FileAccess.Read))
                {
                    // Create an upload request. "snippet,status" indicates which parts of the video resource should be included in the request.
                    var videosInsertRequest = youtubeApiService.Videos.Insert(videoToUpload, "snippet,status", fileStream, "video/*"); // "video/*" is the content type.
                    videosInsertRequest.ProgressChanged += OnUploadProgress; // Handle progress updates.
                    videosInsertRequest.ResponseReceived += OnUploadResponse; // Handle successful response.

                    // Perform the upload.
                    IUploadProgress uploadStatus = await videosInsertRequest.UploadAsync();

                    if (uploadStatus.Status == UploadStatus.Completed)
                    {
                        Console.WriteLine("YouTubeService: Video upload completed successfully!");
                        return videosInsertRequest.ResponseBody; // The ResponseBody contains the Video resource as returned by YouTube.
                    }
                    else
                    {
                        Console.Error.WriteLine($"YouTubeService Error: Video upload failed. Status: {uploadStatus.Status}");
                        if (uploadStatus.Exception != null)
                        {
                            Console.Error.WriteLine($"YouTubeService Upload Exception: {uploadStatus.Exception.Message}");
                        }
                        return null;
                    }
                }
            }
            catch (FileNotFoundException fnfEx) // Specifically for client_secret.json not found from AuthorizeAsync.
            {
                Console.Error.WriteLine($"YouTubeService Error: {fnfEx.Message}"); // Already logged in AuthorizeAsync, but good to catch here too.
                return null;
            }
            catch (AggregateException ex)
            {
                // AggregateException is common during the OAuth flow if there are issues like user cancellation,
                // network problems, or misconfiguration of client secrets.
                Console.Error.WriteLine("YouTubeService Error: An aggregate error occurred, often related to the OAuth authorization flow.");
                foreach (var e in ex.InnerExceptions)
                {
                    Console.Error.WriteLine($"YouTubeService InnerException: {e.Message}");
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"YouTubeService Error: An unexpected error occurred during YouTube upload. {ex.Message}");
                Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Event handler for tracking video upload progress.
        /// </summary>
        /// <param name="progress">The current upload progress status.</param>
        private void OnUploadProgress(IUploadProgress progress)
        {
            switch (progress.Status)
            {
                case UploadStatus.Starting:
                    Console.WriteLine("YouTubeService: Upload starting...");
                    break;
                case UploadStatus.Uploading:
                    Console.WriteLine($"YouTubeService: Uploading... {progress.BytesSent} bytes sent.");
                    break;
                case UploadStatus.Failed:
                    Console.Error.WriteLine($"YouTubeService Error: Upload failed. Exception: {progress.Exception?.Message}");
                    break;
                case UploadStatus.NotStarted:
                    // This state might occur if the upload hasn't begun yet.
                    Console.WriteLine("YouTubeService: Upload not yet started.");
                    break;
                    // UploadStatus.Completed is handled by videosInsertRequest.ResponseReceived or the return of UploadAsync().
            }
        }

        /// <summary>
        /// Event handler for when the YouTube API responds after a successful video upload.
        /// </summary>
        /// <param name="video">The <see cref="Video"/> object returned by YouTube, containing details of the uploaded video.</param>
        private void OnUploadResponse(Video video)
        {
            Console.WriteLine($"YouTubeService: Upload Response Received. Video ID: '{video.Id}', Title: '{video.Snippet.Title}' was successfully uploaded.");
            Console.WriteLine($"Watch at: https://www.youtube.com/watch?v={video.Id}");
        }
    }
}
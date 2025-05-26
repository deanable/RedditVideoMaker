// YouTubeService.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3; // This namespace contains the YouTubeService class with the Scope enum
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Options;

namespace RedditVideoMaker.Core
{
    public class YouTubeService // This is our custom service class
    {
        private readonly YouTubeOptions _youTubeOptions;
        private const string UserCredentialsFolderName = "MyYouTubeCredentials"; // Folder to store OAuth tokens

        public YouTubeService(IOptions<YouTubeOptions> youTubeOptions)
        {
            _youTubeOptions = youTubeOptions.Value;
        }

        private async Task<UserCredential> AuthorizeAsync()
        {
            if (string.IsNullOrWhiteSpace(_youTubeOptions.ClientSecretJsonPath) || !File.Exists(_youTubeOptions.ClientSecretJsonPath))
            {
                throw new FileNotFoundException("YouTube client_secret.json path is not configured or file not found. Please check appsettings.json.", _youTubeOptions.ClientSecretJsonPath);
            }

            UserCredential credential;
            using (var stream = new FileStream(_youTubeOptions.ClientSecretJsonPath, FileMode.Open, FileAccess.Read))
            {
                string credPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), UserCredentialsFolderName);

                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    // Corrected: Scopes are part of Google.Apis.YouTube.v3.YouTubeService
                    new[] { Google.Apis.YouTube.v3.YouTubeService.Scope.YoutubeUpload, Google.Apis.YouTube.v3.YouTubeService.Scope.Youtube },
                    "user",
                    CancellationToken.None,
                    new Google.Apis.Util.Store.FileDataStore(credPath, true)
                );
                Console.WriteLine($"YouTubeService: Credential file saved/loaded from: {credPath}");
            }
            return credential;
        }

        public async Task<Video?> UploadVideoAsync(string videoPath, string title, string description, string[] tags, string categoryId, string privacyStatus)
        {
            if (!File.Exists(videoPath))
            {
                Console.Error.WriteLine($"YouTubeService Error: Video file not found at {videoPath}");
                return null;
            }

            try
            {
                UserCredential credential = await AuthorizeAsync();

                // Use the fully qualified name for Google's YouTubeService to avoid confusion if necessary,
                // though the 'using Google.Apis.YouTube.v3;' should make it clear.
                var youtubeService = new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "Reddit Video Maker Bot C#"
                });

                var video = new Video();
                video.Snippet = new VideoSnippet
                {
                    Title = title,
                    Description = description,
                    Tags = tags,
                    CategoryId = categoryId
                };
                video.Status = new VideoStatus
                {
                    PrivacyStatus = privacyStatus
                };

                Console.WriteLine($"YouTubeService: Starting upload of '{Path.GetFileName(videoPath)}' with title '{title}'...");

                using (var fileStream = new FileStream(videoPath, FileMode.Open, FileAccess.Read))
                {
                    var videosInsertRequest = youtubeService.Videos.Insert(video, "snippet,status", fileStream, "video/*");
                    videosInsertRequest.ProgressChanged += OnUploadProgress;
                    videosInsertRequest.ResponseReceived += OnUploadResponse;

                    IUploadProgress uploadStatus = await videosInsertRequest.UploadAsync();

                    if (uploadStatus.Status == UploadStatus.Completed)
                    {
                        Console.WriteLine("YouTubeService: Video upload completed successfully!");
                        return videosInsertRequest.ResponseBody;
                    }
                    else
                    {
                        Console.Error.WriteLine($"YouTubeService Error: Video upload failed. Status: {uploadStatus.Status}");
                        if (uploadStatus.Exception != null)
                        {
                            Console.Error.WriteLine($"YouTubeService Exception: {uploadStatus.Exception.Message}");
                        }
                        return null;
                    }
                }
            }
            catch (FileNotFoundException fnfEx)
            {
                Console.Error.WriteLine($"YouTubeService Error: {fnfEx.Message}");
                return null;
            }
            catch (AggregateException ex)
            {
                foreach (var e in ex.InnerExceptions)
                {
                    Console.Error.WriteLine("YouTubeService AggregateException: " + e.Message);
                }
                Console.Error.WriteLine("YouTubeService Error: An aggregate error occurred during YouTube upload. This can happen during the OAuth flow if you cancel or if there's a network issue.");
                return null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"YouTubeService Error: An unexpected error occurred during YouTube upload. {ex.Message}");
                Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");
                return null;
            }
        }

        private void OnUploadProgress(IUploadProgress progress)
        {
            switch (progress.Status)
            {
                case UploadStatus.Uploading:
                    Console.WriteLine($"YouTubeService: Uploading... {progress.BytesSent} bytes sent.");
                    break;
                case UploadStatus.Failed:
                    Console.Error.WriteLine($"YouTubeService Error: An error prevented the upload from completing.\n{progress.Exception}");
                    break;
            }
        }

        private void OnUploadResponse(Video video)
        {
            Console.WriteLine($"YouTubeService: Upload Response Received. Video ID: {video.Id}, Title: '{video.Snippet.Title}' was successfully uploaded.");
        }
    }
}

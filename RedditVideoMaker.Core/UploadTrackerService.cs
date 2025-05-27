// UploadTrackerService.cs (in RedditVideoMaker.Core project)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace RedditVideoMaker.Core
{
    public class UploadTrackerService
    {
        private readonly YouTubeOptions _youTubeOptions;
        private readonly string _logFilePath;
        private HashSet<string> _uploadedPostIds; // In-memory cache for quick lookups

        public UploadTrackerService(IOptions<YouTubeOptions> youTubeOptions)
        {
            _youTubeOptions = youTubeOptions.Value;

            // Determine the absolute path for the log file
            if (Path.IsPathRooted(_youTubeOptions.UploadedPostsLogPath))
            {
                _logFilePath = _youTubeOptions.UploadedPostsLogPath;
            }
            else
            {
                _logFilePath = Path.Combine(AppContext.BaseDirectory, _youTubeOptions.UploadedPostsLogPath);
            }

            _uploadedPostIds = LoadUploadedPostIds();
            Console.WriteLine($"UploadTrackerService: Initialized. Tracking {Path.GetFileName(_logFilePath)}. Loaded {_uploadedPostIds.Count} previously uploaded post IDs.");
        }

        private HashSet<string> LoadUploadedPostIds()
        {
            var ids = new HashSet<string>();
            if (!File.Exists(_logFilePath))
            {
                Console.WriteLine($"UploadTrackerService: Log file '{_logFilePath}' not found. Assuming no posts previously uploaded.");
                return ids;
            }

            try
            {
                var lines = File.ReadAllLines(_logFilePath);
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        ids.Add(line.Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"UploadTrackerService Error: Failed to load uploaded post IDs from '{_logFilePath}'. {ex.Message}");
                // Continue with an empty set in case of error, to avoid blocking new uploads.
            }
            return ids;
        }

        /// <summary>
        /// Checks if a Reddit post ID has already been logged as uploaded.
        /// </summary>
        /// <param name="postId">The Reddit post ID (without the "t3_" prefix).</param>
        /// <returns>True if the post ID is found in the log, false otherwise.</returns>
        public bool HasPostBeenUploaded(string postId)
        {
            if (!_youTubeOptions.EnableDuplicateCheck)
            {
                return false; // Duplicate check is disabled
            }
            return _uploadedPostIds.Contains(postId);
        }

        /// <summary>
        /// Adds a Reddit post ID to the log of uploaded posts.
        /// </summary>
        /// <param name="postId">The Reddit post ID (without the "t3_" prefix).</param>
        /// <returns>Task representing the asynchronous operation.</returns>
        public async Task AddPostIdToLogAsync(string postId)
        {
            if (!_youTubeOptions.EnableDuplicateCheck)
            {
                return; // Duplicate check is disabled, no need to log
            }

            if (string.IsNullOrWhiteSpace(postId))
            {
                Console.Error.WriteLine("UploadTrackerService Error: Cannot log an empty post ID.");
                return;
            }

            if (_uploadedPostIds.Contains(postId))
            {
                // Already logged (and in memory cache), no need to write again
                return;
            }

            try
            {
                // Ensure directory exists
                string? directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.AppendAllTextAsync(_logFilePath, postId + Environment.NewLine);
                _uploadedPostIds.Add(postId); // Add to in-memory cache
                Console.WriteLine($"UploadTrackerService: Post ID '{postId}' logged as uploaded to '{Path.GetFileName(_logFilePath)}'.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"UploadTrackerService Error: Failed to log post ID '{postId}' to '{_logFilePath}'. {ex.Message}");
            }
        }
    }
}

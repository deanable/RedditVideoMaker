// UploadTrackerService.cs (in RedditVideoMaker.Core project)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq; // Used by original code, but not strictly necessary for current revision. Can be removed if no LINQ methods are used.
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace RedditVideoMaker.Core
{
    /// <summary>
    /// Manages tracking of Reddit post IDs that have been processed and uploaded.
    /// This service helps prevent duplicate processing of the same Reddit content
    /// by maintaining a log file of processed post IDs.
    /// </summary>
    public class UploadTrackerService
    {
        private readonly YouTubeOptions _youTubeOptions;
        private readonly string _logFilePath;
        private readonly HashSet<string> _uploadedPostIds;

        // Lock object to ensure thread-safe access to the log file and the _uploadedPostIds HashSet during write operations.
        private static readonly object _fileLock = new object();

        /// <summary>
        /// Initializes a new instance of the <see cref="UploadTrackerService"/> class.
        /// It resolves the path for the upload log file and loads any previously logged post IDs.
        /// </summary>
        /// <param name="youTubeOptions">The YouTube options, containing settings for duplicate checking and log file path.</param>
        public UploadTrackerService(IOptions<YouTubeOptions> youTubeOptions)
        {
            _youTubeOptions = youTubeOptions.Value;
            _uploadedPostIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Use OrdinalIgnoreCase for case-insensitive post ID comparison

            string configuredPath = _youTubeOptions.UploadedPostsLogPath;

            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                // If no path is configured, default to a log file in the application's base directory.
                _logFilePath = Path.Combine(AppContext.BaseDirectory, "uploaded_post_ids_default.log");
                Console.Error.WriteLine($"UploadTrackerService Warning: UploadedPostsLogPath not configured, using default: {_logFilePath}");
            }
            else if (Path.IsPathRooted(configuredPath))
            {
                // If an absolute path is configured, use it directly.
                _logFilePath = configuredPath;
            }
            else
            {
                // If a relative path is configured, combine it with the application's base directory.
                _logFilePath = Path.Combine(AppContext.BaseDirectory, configuredPath);
            }

            // Ensure the log file path is canonical and absolute for clarity in logs.
            _logFilePath = Path.GetFullPath(_logFilePath);

            // Load existing post IDs from the log file into memory.
            LoadUploadedPostIds();
            Console.WriteLine($"UploadTrackerService: Initialized. Tracking uploaded posts in: '{_logFilePath}'. Loaded {_uploadedPostIds.Count} previously processed post IDs.");
        }

        /// <summary>
        /// Loads previously logged post IDs from the log file into the in-memory HashSet.
        /// This method is called during service initialization.
        /// </summary>
        private void LoadUploadedPostIds()
        {
            // Early exit if duplicate checking is disabled, no need to load the file.
            if (!_youTubeOptions.EnableDuplicateCheck)
            {
                Console.WriteLine("UploadTrackerService: Duplicate check is disabled. Skipping load of previously uploaded post IDs.");
                return;
            }

            Console.WriteLine($"UploadTrackerService: Attempting to load uploaded post IDs from: {_logFilePath}");
            try
            {
                if (!File.Exists(_logFilePath))
                {
                    Console.WriteLine($"UploadTrackerService: Log file '{_logFilePath}' not found. Assuming no posts previously processed. Will create if needed.");
                    return; // Return with an empty _uploadedPostIds set.
                }

                string[] lines;
                // Lock ensures that even if this method were somehow called concurrently (unlikely for constructor),
                // file reading is safe. Primarily, locks are for write contention or read/write contention.
                lock (_fileLock)
                {
                    lines = File.ReadAllLines(_logFilePath);
                }

                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _uploadedPostIds.Add(line.Trim());
                    }
                }
                Console.WriteLine($"UploadTrackerService: Successfully loaded {_uploadedPostIds.Count} IDs from '{Path.GetFileName(_logFilePath)}'.");
            }
            catch (Exception ex)
            {
                // Log the error but allow the application to continue with an empty (or partially filled) set.
                // This prevents a corrupted log file from halting the entire application.
                Console.Error.WriteLine($"UploadTrackerService Error: Failed to load uploaded post IDs from '{_logFilePath}'. {ex.ToString()}");
            }
        }

        /// <summary>
        /// Checks if a Reddit post with the given ID has already been processed and logged.
        /// </summary>
        /// <param name="postId">The ID of the Reddit post to check.</param>
        /// <returns>
        /// True if duplicate checking is enabled and the post ID is found in the log;
        /// false otherwise, or if duplicate checking is disabled.
        /// </returns>
        public bool HasPostBeenUploaded(string postId)
        {
            if (string.IsNullOrWhiteSpace(postId)) return false; // Cannot check for an empty post ID.

            if (!_youTubeOptions.EnableDuplicateCheck)
            {
                // If duplicate checking is disabled, always report as not uploaded to allow processing.
                return false;
            }

            // HashSet.Contains is thread-safe for reads as long as there are no concurrent writes without a lock.
            // Writes to _uploadedPostIds are locked in AddPostIdToLogAsync.
            // For a high-concurrency scenario, reads might also need locking or a concurrent collection.
            // Given the typical console app flow, this direct read is usually fine.
            bool wasUploaded = _uploadedPostIds.Contains(postId.Trim());

            // Verbose logging, uncomment if needed for debugging.
            // Console.WriteLine($"UploadTrackerService: Checking if Post ID '{postId}' was uploaded: {wasUploaded}. (Cache size: {_uploadedPostIds.Count})");
            return wasUploaded;
        }

        /// <summary>
        /// Adds a Reddit post ID to the in-memory set and appends it to the log file.
        /// This method is asynchronous but performs synchronous file I/O within a lock to ensure safety.
        /// </summary>
        /// <param name="postId">The ID of the Reddit post to log as processed.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task AddPostIdToLogAsync(string postId) // Marked async for TAP, though file I/O is sync in lock
        {
            if (string.IsNullOrWhiteSpace(postId))
            {
                Console.Error.WriteLine("UploadTrackerService Error: Cannot log an empty or whitespace post ID.");
                return;
            }

            string trimmedPostId = postId.Trim();

            // If duplicate checking is disabled, do not log the ID.
            if (!_youTubeOptions.EnableDuplicateCheck)
            {
                Console.WriteLine($"UploadTrackerService: Duplicate check disabled. Not logging '{trimmedPostId}'.");
                return;
            }

            bool addedToMemory;
            lock (_fileLock) // Synchronize access to _uploadedPostIds for writing.
            {
                // Add returns true if the item was added, false if it was already present.
                addedToMemory = _uploadedPostIds.Add(trimmedPostId);
            }

            if (!addedToMemory)
            {
                // Post ID was already in the in-memory set (e.g., logged earlier in this session or loaded from file).
                // No need to write to the file again if it was already loaded or added this session.
                Console.WriteLine($"UploadTrackerService: Post ID '{trimmedPostId}' already in in-memory set. Not writing to file again this session.");
                return;
            }

            // If it's a new addition to the in-memory set this session, log it to the file.
            Console.WriteLine($"UploadTrackerService: Post ID '{trimmedPostId}' added to in-memory cache. Attempting to write to log file: '{_logFilePath}'.");
            try
            {
                string? directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Console.WriteLine($"UploadTrackerService: Creating directory for upload log: {directory}");
                    Directory.CreateDirectory(directory);
                    Console.WriteLine($"UploadTrackerService: Directory created: {directory}");
                }

                // The method is async, but File.AppendAllText is synchronous.
                // This is done to use a simple 'lock' for thread safety with the file.
                // For truly asynchronous file writing with locking, a SemaphoreSlim would be used.
                // Given the application's typical flow (one video processed at a time),
                // this synchronous append within a lock is generally acceptable.
                lock (_fileLock) // Also lock file access to prevent concurrent writes from different calls.
                {
                    File.AppendAllText(_logFilePath, trimmedPostId + Environment.NewLine);
                }
                // If truly async operation is needed:
                // await _asyncFileLock.WaitAsync(); // Example with SemaphoreSlim
                // try { await File.AppendAllTextAsync(_logFilePath, trimmedPostId + Environment.NewLine); }
                // finally { _asyncFileLock.Release(); }

                Console.WriteLine($"UploadTrackerService: Successfully appended Post ID '{trimmedPostId}' to '{Path.GetFileName(_logFilePath)}'.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"UploadTrackerService Error: Failed to log post ID '{trimmedPostId}' to file '{_logFilePath}'. Exception: {ex.ToString()}");
                // If file write fails, the ID remains in the in-memory set for this session,
                // preventing re-processing during the current run. However, it won't be persisted for future runs
                // unless the file write succeeds later or is manually added.
                // Consider if _uploadedPostIds.Remove(trimmedPostId) should be called here under lock,
                // but that could lead to repeated processing attempts if the file error is persistent.
            }
            // Simulating an async operation if there were any true await calls.
            // In this version, it's effectively synchronous due to the file I/O choice.
            await Task.CompletedTask;
        }
    }
}
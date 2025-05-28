using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RedditVideoMaker.Core;
using FFMpegCore;
using SixLabors.Fonts;

public class Program
{
    // Helper function to resolve asset paths
    private static string ResolveAssetPath(string? configuredPath, string assetsRootDirectory, string appBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return string.Empty; // Return empty if no path is configured

        // If the configured path is already absolute, use it directly
        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        // Otherwise, treat it as relative to the assetsRootDirectory, which is relative to the appBaseDirectory
        return Path.GetFullPath(Path.Combine(appBaseDirectory, assetsRootDirectory, configuredPath));
    }

    public static async Task Main(string[] args)
    {
        // Capture original console streams BEFORE any redirection by FileLogger
        TextWriter originalConsoleOut = Console.Out;
        TextWriter originalConsoleError = Console.Error;

        string appBaseDirectoryForPaths = AppContext.BaseDirectory;

        var initialConfigForLogging = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        string logDirFromJson = initialConfigForLogging.GetValue<string>("GeneralOptions:LogFileDirectory") ?? "logs";
        int retentionDaysFromJson = initialConfigForLogging.GetValue<int?>("GeneralOptions:LogFileRetentionDays") ?? 7;
        string consoleLevelStringFromJson = initialConfigForLogging.GetValue<string>("GeneralOptions:ConsoleOutputLevel") ?? "Detailed";

        // This requires ConsoleLogLevel to be defined in RedditVideoMaker.Core namespace (likely in GeneralOptions.cs)
        if (!Enum.TryParse<ConsoleLogLevel>(consoleLevelStringFromJson, true, out ConsoleLogLevel consoleLogLevel))
        {
            consoleLogLevel = ConsoleLogLevel.Detailed;
            originalConsoleError.WriteLine($"Warning: Invalid ConsoleOutputLevel '{consoleLevelStringFromJson}' in appsettings.json. Defaulting to '{consoleLogLevel}'.");
        }

        string fullLogDirectoryPath = Path.Combine(appBaseDirectoryForPaths, logDirFromJson);

        FileLogger.Initialize(fullLogDirectoryPath, consoleLogLevel);
        FileLogger.CleanupOldLogFiles(retentionDaysFromJson);

        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] Application Startup.");

        try
        {
            string ffmpegBinFolder = Path.Combine(appBaseDirectoryForPaths, "ffmpeg_bin");
            if (Directory.Exists(ffmpegBinFolder) && File.Exists(Path.Combine(ffmpegBinFolder, "ffmpeg.exe")) && File.Exists(Path.Combine(ffmpegBinFolder, "ffprobe.exe")))
            { GlobalFFOptions.Configure(new FFOptions { BinaryFolder = ffmpegBinFolder }); Console.WriteLine($"FFMpegCore: Configured from: {ffmpegBinFolder}"); }
            else { Console.Error.WriteLine($"FFMpegCore Error: ffmpeg_bin or executables not found at {ffmpegBinFolder}. Using PATH if available."); }

            Console.WriteLine("\nReddit Video Maker Bot C# - Step 29: Configurable Assets Folder");

            IConfiguration configuration = initialConfigForLogging;

            var services = new ServiceCollection();
            services.Configure<GeneralOptions>(configuration.GetSection(GeneralOptions.SectionName));
            services.Configure<RedditOptions>(configuration.GetSection(RedditOptions.SectionName));
            services.Configure<VideoOptions>(configuration.GetSection(VideoOptions.SectionName));
            services.Configure<TtsOptions>(configuration.GetSection(TtsOptions.SectionName));
            services.Configure<YouTubeOptions>(configuration.GetSection(YouTubeOptions.SectionName));

            services.AddSingleton<RedditService>();
            services.AddSingleton<TtsService>();
            services.AddSingleton<ImageService>();
            services.AddSingleton<VideoService>();
            services.AddSingleton<YouTubeService>();
            services.AddSingleton<UploadTrackerService>();

            var serviceProvider = services.BuildServiceProvider();

            var generalOptions = serviceProvider.GetService<IOptions<GeneralOptions>>()?.Value;
            var redditOptions = serviceProvider.GetService<IOptions<RedditOptions>>()?.Value;
            var videoOptions = serviceProvider.GetService<IOptions<VideoOptions>>()?.Value;
            var ttsOptions = serviceProvider.GetService<IOptions<TtsOptions>>()?.Value;
            var youtubeOptions = serviceProvider.GetService<IOptions<YouTubeOptions>>()?.Value;

            if (generalOptions == null || redditOptions == null || videoOptions == null || ttsOptions == null || youtubeOptions == null)
            { Console.Error.WriteLine("Error: One or more options sections not configured properly in appsettings.json."); return; }

            if (string.IsNullOrWhiteSpace(redditOptions.PostUrl) && string.IsNullOrWhiteSpace(redditOptions.Subreddit))
            { Console.Error.WriteLine("Error: Either PostUrl or Subreddit must be configured in RedditOptions in appsettings.json."); return; }

            // Resolve asset paths using the helper function and appBaseDirectory
            string resolvedBackgroundVideoPath = ResolveAssetPath(videoOptions.BackgroundVideoPath, videoOptions.AssetsRootDirectory, appBaseDirectoryForPaths);

            Console.WriteLine($"Testing Mode Enabled: {generalOptions.IsInTestingModule}");
            Console.WriteLine($"Actual Console Output Level in use by FileLogger: {consoleLogLevel}");
            Console.WriteLine($"Duplicate Upload Check Enabled: {youtubeOptions.EnableDuplicateCheck}");
            Console.WriteLine($"Primary Font Path (relative to app): {videoOptions.PrimaryFontFilePath}");
            Console.WriteLine($"Assets Root Directory (relative to app): {videoOptions.AssetsRootDirectory}");
            Console.WriteLine($"Configured Background Video Path: {videoOptions.BackgroundVideoPath}, Resolved to: {resolvedBackgroundVideoPath}");


            if (string.IsNullOrWhiteSpace(resolvedBackgroundVideoPath) || !File.Exists(resolvedBackgroundVideoPath))
            { Console.Error.WriteLine($"Error: BackgroundVideoPath is not configured or file not found. Configured: '{videoOptions.BackgroundVideoPath}', Attempted: '{resolvedBackgroundVideoPath}'. Please check appsettings.json and ensure the file exists in the '{videoOptions.AssetsRootDirectory}' folder or is a valid absolute path."); return; }

            if (!string.IsNullOrWhiteSpace(videoOptions.BackgroundMusicFilePath) &&
                !File.Exists(ResolveAssetPath(videoOptions.BackgroundMusicFilePath, videoOptions.AssetsRootDirectory, appBaseDirectoryForPaths)) &&
                videoOptions.BackgroundMusicVolume > 0)
            { Console.Error.WriteLine($"Warning: BackgroundMusicFilePath is specified ('{videoOptions.BackgroundMusicFilePath}') but resolved file not found at '{ResolveAssetPath(videoOptions.BackgroundMusicFilePath, videoOptions.AssetsRootDirectory, appBaseDirectoryForPaths)}'. Background music will be skipped."); }

            if (!string.IsNullOrWhiteSpace(videoOptions.IntroVideoPath) &&
                !File.Exists(ResolveAssetPath(videoOptions.IntroVideoPath, videoOptions.AssetsRootDirectory, appBaseDirectoryForPaths)))
            { Console.Error.WriteLine($"Warning: IntroVideoPath is specified ('{videoOptions.IntroVideoPath}') but resolved file not found at '{ResolveAssetPath(videoOptions.IntroVideoPath, videoOptions.AssetsRootDirectory, appBaseDirectoryForPaths)}'. Intro video will be skipped."); }

            if (!string.IsNullOrWhiteSpace(videoOptions.OutroVideoPath) &&
                !File.Exists(ResolveAssetPath(videoOptions.OutroVideoPath, videoOptions.AssetsRootDirectory, appBaseDirectoryForPaths)))
            { Console.Error.WriteLine($"Warning: OutroVideoPath is specified ('{videoOptions.OutroVideoPath}') but resolved file not found at '{ResolveAssetPath(videoOptions.OutroVideoPath, videoOptions.AssetsRootDirectory, appBaseDirectoryForPaths)}'. Outro video will be skipped."); }


            if (!generalOptions.IsInTestingModule && (string.IsNullOrWhiteSpace(youtubeOptions.ClientSecretJsonPath) || !File.Exists(youtubeOptions.ClientSecretJsonPath)))
            { Console.Error.WriteLine($"Error: YouTube ClientSecretJsonPath is not configured or file not found. This is required when not in testing mode."); return; }


            var redditService = serviceProvider.GetRequiredService<RedditService>();
            var ttsService = serviceProvider.GetRequiredService<TtsService>();
            var imageService = serviceProvider.GetRequiredService<ImageService>();
            var videoService = serviceProvider.GetRequiredService<VideoService>();
            var youTubeService = serviceProvider.GetRequiredService<YouTubeService>();
            var uploadTracker = serviceProvider.GetRequiredService<UploadTrackerService>();

            Console.WriteLine($"\nAttempting to select Reddit post(s) based on configuration...");
            // GetTopPostsAsync now uses options injected into RedditService and returns a List
            List<RedditPostData> postsToProcess = await redditService.GetTopPostsAsync();

            if (postsToProcess == null || !postsToProcess.Any())
            {
                Console.WriteLine("No suitable posts found to process.");
            }
            else
            {
                int postCounter = 0;
                foreach (var selectedPost in postsToProcess)
                {
                    postCounter++;
                    Console.WriteLine($"\n--- Checking Post {postCounter} of {postsToProcess.Count}: \"{selectedPost.Title}\" (ID: {selectedPost.Id}) ---");

                    if (string.IsNullOrWhiteSpace(selectedPost.Id) || string.IsNullOrWhiteSpace(selectedPost.Subreddit) || string.IsNullOrWhiteSpace(selectedPost.Title))
                    {
                        Console.Error.WriteLine($"Skipping post {selectedPost.Id ?? "Unknown"} due to missing critical info.");
                        continue;
                    }

                    // Use youtubeOptions.EnableDuplicateCheck
                    if (youtubeOptions.EnableDuplicateCheck && uploadTracker.HasPostBeenUploaded(selectedPost.Id!))
                    {
                        Console.WriteLine($"Post ID '{selectedPost.Id}' has already been processed. Skipping.");
                        continue;
                    }

                    Console.WriteLine($"Processing Post: {selectedPost.Title} by {selectedPost.Author} (Score: {selectedPost.Score})");

                    List<string> individualVideoClips = new List<string>();
                    List<string> intermediateFilesToClean = new List<string>();

                    string baseOutputDir = Path.Combine(Directory.GetCurrentDirectory(), "output_files");
                    string ttsDir = Path.Combine(baseOutputDir, "tts");
                    string imgDir = Path.Combine(baseOutputDir, "images");
                    string clipsDir = Path.Combine(baseOutputDir, "clips");
                    string finalDir = Path.Combine(baseOutputDir, "final_video");
                    Directory.CreateDirectory(ttsDir);
                    Directory.CreateDirectory(imgDir);
                    Directory.CreateDirectory(clipsDir);
                    Directory.CreateDirectory(finalDir);

                    int finalVideoWidth = 1080; int finalVideoHeight = 1920;
                    if (!string.IsNullOrWhiteSpace(videoOptions.OutputResolution) && videoOptions.OutputResolution.Contains('x', StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = videoOptions.OutputResolution.Split(new char[] { 'x', 'X' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                        { finalVideoWidth = w; finalVideoHeight = h; }
                    }
                    Console.WriteLine($"TTS Engine Configured (before Testing Mode override): {ttsOptions.Engine}");

                    // Use imageService.LoadedFontFamily
                    FontFamily? measuringFontFamily = imageService.LoadedFontFamily;
                    if (measuringFontFamily == null)
                    {
                        Console.Error.WriteLine("Program.cs: Critical - No font could be loaded by ImageService. Text splitting for self-text will be skipped or may be inaccurate.");
                    }
                    else
                    {
                        Console.WriteLine($"Program.cs: Using font '{measuringFontFamily.Value.Name}' (from ImageService) for text measurement.");
                    }

                    // --- Add Intro Clip if specified ---
                    string currentIntroPath = ResolveAssetPath(videoOptions.IntroVideoPath, videoOptions.AssetsRootDirectory, appBaseDirectoryForPaths);
                    if (!string.IsNullOrWhiteSpace(currentIntroPath) && File.Exists(currentIntroPath))
                    {
                        Console.WriteLine($"Adding intro clip: {currentIntroPath}");
                        individualVideoClips.Add(currentIntroPath);
                    }

                    // --- Process Post Title as a Clip ---
                    Console.WriteLine($"\n--- Processing Post Title for post {selectedPost.Id} ---");
                    string cleanedTitleForTts = TextUtilities.CleanTextForTts(selectedPost.Title!);
                    string titleForCard = selectedPost.Title!;
                    string titleId = $"post_{selectedPost.Id!.Replace(" ", "_")}";
                    string titleTtsPath = Path.Combine(ttsDir, $"audio_{titleId}.wav");
                    string titleCardPath = Path.Combine(imgDir, $"image_{titleId}.png");
                    string titleClipPath = Path.Combine(clipsDir, $"clip_{titleId}.mp4");
                    intermediateFilesToClean.Add(titleTtsPath); intermediateFilesToClean.Add(titleCardPath);

                    if (await ttsService.TextToSpeechAsync(cleanedTitleForTts, titleTtsPath) &&
                        await imageService.CreateRedditContentCardAsync(
                            titleForCard, selectedPost.Author, selectedPost.Score, titleCardPath,
                            videoOptions.CardWidth, videoOptions.CardHeight, videoOptions.CardBackgroundColor,
                            videoOptions.CardFontColor, videoOptions.CardMetadataFontColor) &&
                        await videoService.CreateClipWithBackgroundAsync(resolvedBackgroundVideoPath, titleCardPath, titleTtsPath, titleClipPath, finalVideoWidth, finalVideoHeight))
                    {
                        Console.WriteLine($"Title clip created: {titleClipPath}");
                        individualVideoClips.Add(titleClipPath);
                    }
                    else { Console.Error.WriteLine($"Failed to process title clip for post {selectedPost.Id}."); }

                    // --- Process Post Self-Text as a Clip (with splitting) ---
                    if (!string.IsNullOrWhiteSpace(selectedPost.Selftext) && measuringFontFamily != null)
                    {
                        FontFamily actualMeasuringFontFamily = measuringFontFamily.Value;
                        Font selfTextMeasuringFont = actualMeasuringFontFamily.CreateFont(videoOptions.ContentTargetFontSize, FontStyle.Regular);
                        float textPadding = Math.Max(15f, Math.Min(videoOptions.CardWidth * 0.05f, videoOptions.CardHeight * 0.05f));
                        float selfTextCardContentWidth = videoOptions.CardWidth - (2 * textPadding);
                        float selfTextCardContentHeight = videoOptions.CardHeight - (2 * textPadding);
                        List<string> selfTextPagesForTts = TextUtilities.SplitTextIntoPages(TextUtilities.CleanTextForTts(selectedPost.Selftext), selfTextMeasuringFont, selfTextCardContentWidth, selfTextCardContentHeight);
                        List<string> selfTextPagesForCard = selfTextPagesForTts;
                        Console.WriteLine($"Self-text split into {selfTextPagesForTts.Count} page(s).");
                        int selfTextPageIndex = 0;
                        for (int i = 0; i < selfTextPagesForTts.Count; i++)
                        {
                            string selfTextPageContentForTts = selfTextPagesForTts[i];
                            string selfTextPageContentForCard = selfTextPagesForCard.Count > i ? selfTextPagesForCard[i] : selfTextPageContentForTts;
                            if (string.IsNullOrWhiteSpace(selfTextPageContentForTts)) continue;
                            selfTextPageIndex++;
                            string selfTextId = $"post_{selectedPost.Id!}_selftext_p{selfTextPageIndex}";
                            string selfTextPageTtsPath = Path.Combine(ttsDir, $"audio_{selfTextId}.wav");
                            string selfTextPageCardPath = Path.Combine(imgDir, $"image_{selfTextId}.png");
                            string selfTextPageClipPath = Path.Combine(clipsDir, $"clip_{selfTextId}.mp4");
                            intermediateFilesToClean.Add(selfTextPageTtsPath); intermediateFilesToClean.Add(selfTextPageCardPath);
                            string pageIndicator = selfTextPagesForTts.Count > 1 ? $" (Page {selfTextPageIndex}/{selfTextPagesForTts.Count})" : "";
                            if (await ttsService.TextToSpeechAsync(selfTextPageContentForTts, selfTextPageTtsPath) &&
                                await imageService.CreateRedditContentCardAsync(selfTextPageContentForCard + pageIndicator, null, null, selfTextPageCardPath, videoOptions.CardWidth, videoOptions.CardHeight, videoOptions.CardBackgroundColor, videoOptions.CardFontColor, videoOptions.CardMetadataFontColor) &&
                                await videoService.CreateClipWithBackgroundAsync(resolvedBackgroundVideoPath, selfTextPageCardPath, selfTextPageTtsPath, selfTextPageClipPath, finalVideoWidth, finalVideoHeight))
                            {
                                Console.WriteLine($"Self-text clip (page {selfTextPageIndex}) created: {selfTextPageClipPath}");
                                individualVideoClips.Add(selfTextPageClipPath);
                            }
                            else { Console.Error.WriteLine($"Failed to process self-text clip (page {selfTextPageIndex})."); }
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(selectedPost.Selftext))
                    {
                        Console.WriteLine("No self-text for this post.");
                    }
                    else if (measuringFontFamily == null)
                    {
                        Console.Error.WriteLine("Skipping self-text processing as no usable measuring font was available from ImageService.");
                    }

                    // --- Process Comments as Clips ---
                    Console.WriteLine($"\nFetching comments for post ID: {selectedPost.Id} from subreddit /r/{selectedPost.Subreddit}...");
                    // Corrected parameter name for GetCommentsAsync
                    int initialCommentFetchLimit = (videoOptions.NumberOfCommentsToInclude * 3) + 20;
                    List<RedditCommentData>? fetchedComments = await redditService.GetCommentsAsync(selectedPost.Subreddit!, selectedPost.Id!, commentFetchLimit: initialCommentFetchLimit);

                    if (fetchedComments != null && fetchedComments.Any())
                    {
                        var suitableComments = fetchedComments
                            .Where(c => !string.IsNullOrWhiteSpace(c.Body) && c.Body.Length > 10)
                            .Take(videoOptions.NumberOfCommentsToInclude)
                            .ToList();
                        Console.WriteLine($"\nProcessing {suitableComments.Count} suitable comments (target: {videoOptions.NumberOfCommentsToInclude})...");
                        int idx = 0;
                        foreach (var comment in suitableComments)
                        {
                            idx++;
                            string cleanedCommentBodyForTts = TextUtilities.CleanTextForTts(comment.Body!);
                            string commentBodyForCard = comment.Body!;
                            string cId = $"{selectedPost.Id}_c{idx}_{comment.Id?.Replace(" ", "_").Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_') ?? Guid.NewGuid().ToString()}";
                            string cTtsPath = Path.Combine(ttsDir, $"audio_{cId}.wav");
                            string cCardPath = Path.Combine(imgDir, $"image_{cId}.png");
                            string cClipPath = Path.Combine(clipsDir, $"clip_{cId}.mp4");
                            intermediateFilesToClean.Add(cTtsPath); intermediateFilesToClean.Add(cCardPath);
                            if (await ttsService.TextToSpeechAsync(cleanedCommentBodyForTts, cTtsPath) &&
                                await imageService.CreateRedditContentCardAsync(commentBodyForCard, comment.Author, comment.Score, cCardPath, videoOptions.CardWidth, videoOptions.CardHeight, videoOptions.CardBackgroundColor, videoOptions.CardFontColor, videoOptions.CardMetadataFontColor) &&
                                await videoService.CreateClipWithBackgroundAsync(resolvedBackgroundVideoPath, cCardPath, cTtsPath, cClipPath, finalVideoWidth, finalVideoHeight))
                            {
                                Console.WriteLine($"Comment clip {idx} created: {cClipPath}");
                                individualVideoClips.Add(cClipPath);
                            }
                            else { Console.Error.WriteLine($"Failed to process comment clip {idx}."); }
                        }
                    }
                    else { Console.WriteLine("No comments found for the post (or none met score criteria)."); }

                    // --- Add Outro Clip if specified ---
                    string currentOutroPath = ResolveAssetPath(videoOptions.OutroVideoPath, videoOptions.AssetsRootDirectory, appBaseDirectoryForPaths);
                    if (!string.IsNullOrWhiteSpace(currentOutroPath) && File.Exists(currentOutroPath))
                    {
                        Console.WriteLine($"Adding outro clip: {currentOutroPath}");
                        individualVideoClips.Add(currentOutroPath);
                    }

                    // --- Concatenate all video clips for the current post ---
                    if (individualVideoClips.Any())
                    {
                        string finalVideoPath = Path.Combine(finalDir, $"final_video_{selectedPost.Id}.mp4");
                        Console.WriteLine($"\nConcatenating {individualVideoClips.Count} clips for post {selectedPost.Id} into {finalVideoPath}...");
                        bool concatenationSuccess = await videoService.ConcatenateVideosAsync(individualVideoClips, finalVideoPath);

                        if (concatenationSuccess)
                        {
                            Console.WriteLine($"Final video created for post {selectedPost.Id}: {finalVideoPath}");

                            // Use youtubeOptions.EnableDuplicateCheck
                            if (youtubeOptions.EnableDuplicateCheck)
                            {
                                await uploadTracker.AddPostIdToLogAsync(selectedPost.Id!);
                            }

                            if (!generalOptions.IsInTestingModule)
                            {
                                Console.WriteLine($"\n--- Attempting YouTube Upload for post {selectedPost.Id} ---");
                                string videoTitle = !string.IsNullOrWhiteSpace(selectedPost.Title) ? TextUtilities.CleanTextForTts(selectedPost.Title) : youtubeOptions.DefaultVideoTitle;
                                string videoDescription = $"Reddit story from /r/{selectedPost.Subreddit}.\nOriginal post by u/{selectedPost.Author}.\n\n{youtubeOptions.DefaultVideoDescription}";
                                if (!string.IsNullOrWhiteSpace(selectedPost.Selftext))
                                { videoDescription += $"\n\nPost Text:\n{TextUtilities.CleanTextForTts(selectedPost.Selftext).Substring(0, Math.Min(TextUtilities.CleanTextForTts(selectedPost.Selftext).Length, 300))}..."; }

                                var uploadedVideo = await youTubeService.UploadVideoAsync(
                                    finalVideoPath, videoTitle, videoDescription, youtubeOptions.DefaultVideoTags.ToArray(),
                                    youtubeOptions.DefaultVideoCategoryId, youtubeOptions.DefaultVideoPrivacyStatus
                                );

                                if (uploadedVideo != null)
                                {
                                    Console.WriteLine($"Successfully uploaded video! YouTube ID: {uploadedVideo.Id}");
                                    Console.WriteLine($"Watch it at: https://www.youtube.com/watch?v={uploadedVideo.Id}");
                                }
                                else { Console.Error.WriteLine($"YouTube upload failed for post {selectedPost.Id}. Check previous logs."); }
                            }
                            else { Console.WriteLine($"\nTesting Mode: YouTube upload skipped for post {selectedPost.Id}."); }

                            if (videoOptions.CleanUpIntermediateFiles)
                            {
                                Console.WriteLine($"Cleaning up intermediate files for post {selectedPost.Id}...");
                                var generatedClipsForThisPost = individualVideoClips.Where(p => p.StartsWith(clipsDir));
                                intermediateFilesToClean.AddRange(generatedClipsForThisPost);

                                foreach (var filePath in intermediateFilesToClean)
                                { try { if (File.Exists(filePath)) File.Delete(filePath); } catch (Exception ex) { Console.Error.WriteLine($"Error deleting file {filePath}: {ex.Message}"); } }
                                Console.WriteLine($"Intermediate files cleanup process completed for post {selectedPost.Id}.");
                            }
                        }
                        else { Console.Error.WriteLine($"Failed to concatenate video clips for post {selectedPost.Id}."); }
                    }
                    else { Console.WriteLine($"No video clips were generated for post {selectedPost.Id} to concatenate."); }
                } // End of foreach post loop
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CRITICAL UNHANDLED EXCEPTION in Main: {ex.ToString()}");
            System.Console.Error.WriteLine("Application terminated due to a critical error.");
        }
        finally
        {
            Console.WriteLine("\nEnd of processing. All tasks completed.");
            FileLogger.Dispose();
            System.Console.Out.WriteLine("\nApplication finished. Press any key to exit.");
            System.Console.Out.Flush();
            try { System.Console.ReadKey(); }
            catch (InvalidOperationException ioex)
            { System.Console.Error.WriteLine($"Error during Console.ReadKey(): {ioex.Message}"); await Task.Delay(5000); }
        }
    }
}

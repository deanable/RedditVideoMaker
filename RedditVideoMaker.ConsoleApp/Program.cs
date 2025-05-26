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
    public static async Task Main(string[] args)
    {
        string baseDirectory = AppContext.BaseDirectory;
        string ffmpegBinFolder = Path.Combine(baseDirectory, "ffmpeg_bin");
        if (Directory.Exists(ffmpegBinFolder) && File.Exists(Path.Combine(ffmpegBinFolder, "ffmpeg.exe")) && File.Exists(Path.Combine(ffmpegBinFolder, "ffprobe.exe")))
        { GlobalFFOptions.Configure(new FFOptions { BinaryFolder = ffmpegBinFolder }); Console.WriteLine($"FFMpegCore: Configured from: {ffmpegBinFolder}"); }
        else { Console.Error.WriteLine($"FFMpegCore Error: ffmpeg_bin or executables not found at {ffmpegBinFolder}. Using PATH if available."); }

        Console.WriteLine("\nReddit Video Maker Bot C# - Step 23: Adding Background Music");

        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var services = new ServiceCollection();
        // Configure options
        services.Configure<GeneralOptions>(configuration.GetSection(GeneralOptions.SectionName));
        services.Configure<RedditOptions>(configuration.GetSection(RedditOptions.SectionName));
        services.Configure<VideoOptions>(configuration.GetSection(VideoOptions.SectionName));
        services.Configure<TtsOptions>(configuration.GetSection(TtsOptions.SectionName));
        services.Configure<YouTubeOptions>(configuration.GetSection(YouTubeOptions.SectionName));

        // Register services
        services.AddSingleton<RedditService>();
        services.AddSingleton<TtsService>();
        services.AddSingleton<ImageService>();
        services.AddSingleton<VideoService>();
        services.AddSingleton<YouTubeService>();

        var serviceProvider = services.BuildServiceProvider();

        var generalOptions = serviceProvider.GetService<IOptions<GeneralOptions>>()?.Value;
        var redditOptions = serviceProvider.GetService<IOptions<RedditOptions>>()?.Value;
        var videoOptions = serviceProvider.GetService<IOptions<VideoOptions>>()?.Value;
        var ttsOptions = serviceProvider.GetService<IOptions<TtsOptions>>()?.Value;
        var youtubeOptions = serviceProvider.GetService<IOptions<YouTubeOptions>>()?.Value;

        if (generalOptions == null || redditOptions == null || videoOptions == null || ttsOptions == null || youtubeOptions == null || string.IsNullOrWhiteSpace(redditOptions.Subreddit))
        { Console.Error.WriteLine("Error: One or more options sections not configured properly in appsettings.json."); Console.ReadKey(); return; }

        Console.WriteLine($"Testing Mode Enabled: {generalOptions.IsInTestingModule}");

        // Background video path is still essential for clip creation
        if (string.IsNullOrWhiteSpace(videoOptions.BackgroundVideoPath) || !File.Exists(videoOptions.BackgroundVideoPath))
        { Console.Error.WriteLine($"Error: BackgroundVideoPath is not configured or file not found: '{videoOptions.BackgroundVideoPath}'. Please check appsettings.json."); Console.ReadKey(); return; }

        // Background music path is optional, so only warn if specified but not found
        if (!string.IsNullOrWhiteSpace(videoOptions.BackgroundMusicFilePath) && !File.Exists(videoOptions.BackgroundMusicFilePath))
        { Console.Error.WriteLine($"Warning: BackgroundMusicFilePath is specified ('{videoOptions.BackgroundMusicFilePath}') but file not found. Background music will be skipped."); }


        if (!generalOptions.IsInTestingModule && (string.IsNullOrWhiteSpace(youtubeOptions.ClientSecretJsonPath) || !File.Exists(youtubeOptions.ClientSecretJsonPath)))
        { Console.Error.WriteLine($"Error: YouTube ClientSecretJsonPath is not configured or file not found: '{youtubeOptions.ClientSecretJsonPath}'. This is required when not in testing mode. Please check appsettings.json."); Console.ReadKey(); return; }


        var redditService = serviceProvider.GetRequiredService<RedditService>();
        var ttsService = serviceProvider.GetRequiredService<TtsService>();
        var imageService = serviceProvider.GetRequiredService<ImageService>();
        var videoService = serviceProvider.GetRequiredService<VideoService>();
        var youTubeService = serviceProvider.GetRequiredService<YouTubeService>();

        Console.WriteLine($"\nFetching top posts from /r/{redditOptions.Subreddit}");
        List<RedditPostData>? topPosts = await redditService.GetTopPostsAsync(redditOptions.Subreddit, limit: 1);

        if (topPosts != null && topPosts.Any())
        {
            RedditPostData selectedPost = topPosts.First();
            Console.WriteLine($"\nSelected post: {selectedPost.Title} by {selectedPost.Author} (Score: {selectedPost.Score})");

            if (string.IsNullOrWhiteSpace(selectedPost.Id) || string.IsNullOrWhiteSpace(selectedPost.Subreddit) || string.IsNullOrWhiteSpace(selectedPost.Title))
            { Console.Error.WriteLine("Fetched post missing critical info (ID, Subreddit, or Title)."); }
            else
            {
                List<string> individualVideoClips = new List<string>();
                List<string> intermediateFilesToClean = new List<string>();

                string baseOutputDir = Path.Combine(Directory.GetCurrentDirectory(), "output_files");
                string ttsDir = Path.Combine(baseOutputDir, "tts"); string imgDir = Path.Combine(baseOutputDir, "images");
                string clipsDir = Path.Combine(baseOutputDir, "clips"); string finalDir = Path.Combine(baseOutputDir, "final_video");
                Directory.CreateDirectory(ttsDir); Directory.CreateDirectory(imgDir); Directory.CreateDirectory(clipsDir); Directory.CreateDirectory(finalDir);

                int finalVideoWidth = 1080; int finalVideoHeight = 1920;
                if (!string.IsNullOrWhiteSpace(videoOptions.OutputResolution) && videoOptions.OutputResolution.Contains('x', StringComparison.OrdinalIgnoreCase))
                {
                    var parts = videoOptions.OutputResolution.Split(new char[] { 'x', 'X' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                    { finalVideoWidth = w; finalVideoHeight = h; }
                }
                Console.WriteLine($"Using final video dimensions: {finalVideoWidth}x{finalVideoHeight}");
                Console.WriteLine($"Using card dimensions: {videoOptions.CardWidth}x{videoOptions.CardHeight}");
                Console.WriteLine($"Number of comments to include: {videoOptions.NumberOfCommentsToInclude}");
                Console.WriteLine($"Transitions Enabled: {videoOptions.EnableTransitions}, Duration: {videoOptions.TransitionDurationSeconds}s");
                Console.WriteLine($"Font Sizes (Content Target/Min/Max): {videoOptions.ContentTargetFontSize}/{videoOptions.ContentMinFontSize}/{videoOptions.ContentMaxFontSize}");
                Console.WriteLine($"Font Sizes (Metadata Target/Min/Max): {videoOptions.MetadataTargetFontSize}/{videoOptions.MetadataMinFontSize}/{videoOptions.MetadataMaxFontSize}");
                Console.WriteLine($"Cleanup Intermediate Files: {videoOptions.CleanUpIntermediateFiles}");
                Console.WriteLine($"TTS Engine Configured (before Testing Mode override): {ttsOptions.Engine}");
                Console.WriteLine($"Background Music File: {(string.IsNullOrWhiteSpace(videoOptions.BackgroundMusicFilePath) ? "Not configured" : videoOptions.BackgroundMusicFilePath)} (Volume: {videoOptions.BackgroundMusicVolume})");


                FontFamily? nullableMeasuringFontFamily = null;
                FontFamily tempFontFamilyHolder;
                if (SystemFonts.TryGet("DejaVu Sans", out tempFontFamilyHolder) && !string.IsNullOrEmpty(tempFontFamilyHolder.Name))
                { nullableMeasuringFontFamily = tempFontFamilyHolder; Console.WriteLine("Program.cs: Using 'DejaVu Sans' for text measurement."); }
                else if (SystemFonts.Families.Any())
                {
                    FontFamily firstAvailableSystemFont = SystemFonts.Families.First();
                    if (!string.IsNullOrEmpty(firstAvailableSystemFont.Name)) { nullableMeasuringFontFamily = firstAvailableSystemFont; Console.WriteLine($"Program.cs: Warning - 'DejaVu Sans' not found. Using first available system font '{firstAvailableSystemFont.Name}' for text measurement."); }
                }
                if (nullableMeasuringFontFamily == null) { Console.Error.WriteLine("Program.cs: Critical - No usable system font found for text measurement."); }

                // --- Add Intro Clip if specified ---
                if (!string.IsNullOrWhiteSpace(videoOptions.IntroVideoPath) && File.Exists(videoOptions.IntroVideoPath))
                {
                    Console.WriteLine($"Adding intro clip: {videoOptions.IntroVideoPath}");
                    individualVideoClips.Add(videoOptions.IntroVideoPath);
                }

                // --- Process Post Title as a Clip ---
                Console.WriteLine($"\n--- Processing Post Title ---");
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
                    await videoService.CreateClipWithBackgroundAsync(videoOptions.BackgroundVideoPath!, titleCardPath, titleTtsPath, titleClipPath, finalVideoWidth, finalVideoHeight))
                {
                    Console.WriteLine($"Title clip created: {titleClipPath}");
                    individualVideoClips.Add(titleClipPath);
                }
                else { Console.Error.WriteLine("Failed to process title clip."); }

                // --- Process Post Self-Text as a Clip (with splitting) ---
                if (!string.IsNullOrWhiteSpace(selectedPost.Selftext) && nullableMeasuringFontFamily != null)
                {
                    FontFamily actualMeasuringFontFamily = (FontFamily)nullableMeasuringFontFamily;
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
                            await videoService.CreateClipWithBackgroundAsync(videoOptions.BackgroundVideoPath!, selfTextPageCardPath, selfTextPageTtsPath, selfTextPageClipPath, finalVideoWidth, finalVideoHeight))
                        {
                            Console.WriteLine($"Self-text clip (page {selfTextPageIndex}) created: {selfTextPageClipPath}");
                            individualVideoClips.Add(selfTextPageClipPath);
                        }
                        else { Console.Error.WriteLine($"Failed to process self-text clip (page {selfTextPageIndex})."); }
                    }
                } // ... (rest of self-text handling)


                // --- Process Comments as Clips ---
                Console.WriteLine($"\nFetching comments for post ID: {selectedPost.Id}...");
                int commentsToFetch = videoOptions.NumberOfCommentsToInclude > 0 ? (videoOptions.NumberOfCommentsToInclude * 2) + 5 : 10;
                List<RedditCommentData>? comments = await redditService.GetCommentsAsync(selectedPost.Subreddit!, selectedPost.Id!, commentLimit: commentsToFetch);
                if (comments != null && comments.Any())
                {
                    var suitableComments = comments.Where(c => !string.IsNullOrWhiteSpace(c.Body) && c.Body.Length > 10).Take(videoOptions.NumberOfCommentsToInclude).ToList();
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
                            await videoService.CreateClipWithBackgroundAsync(videoOptions.BackgroundVideoPath!, cCardPath, cTtsPath, cClipPath, finalVideoWidth, finalVideoHeight))
                        {
                            Console.WriteLine($"Comment clip {idx} created: {cClipPath}");
                            individualVideoClips.Add(cClipPath);
                        }
                        else { Console.Error.WriteLine($"Failed to process comment clip {idx}."); }
                    }
                }
                else { Console.WriteLine("No comments found for the post."); }

                // --- Add Outro Clip if specified ---
                if (!string.IsNullOrWhiteSpace(videoOptions.OutroVideoPath) && File.Exists(videoOptions.OutroVideoPath))
                {
                    Console.WriteLine($"Adding outro clip: {videoOptions.OutroVideoPath}");
                    individualVideoClips.Add(videoOptions.OutroVideoPath);
                }


                // --- Concatenate all video clips ---
                if (individualVideoClips.Any())
                {
                    string finalVideoPath = Path.Combine(finalDir, $"final_video_{selectedPost.Id}.mp4");
                    Console.WriteLine($"\nConcatenating {individualVideoClips.Count} clips into {finalVideoPath}...");
                    bool concatenationSuccess = await videoService.ConcatenateVideosAsync(individualVideoClips, finalVideoPath);

                    if (concatenationSuccess)
                    {
                        Console.WriteLine($"Final video created: {finalVideoPath}");
                        if (!generalOptions.IsInTestingModule)
                        {
                            Console.WriteLine("\n--- Attempting YouTube Upload ---");
                            string videoTitle = !string.IsNullOrWhiteSpace(selectedPost.Title) ? TextUtilities.CleanTextForTts(selectedPost.Title) : youtubeOptions.DefaultVideoTitle;
                            string videoDescription = $"Reddit story from /r/{selectedPost.Subreddit}.\nOriginal post by u/{selectedPost.Author}.\n\n{youtubeOptions.DefaultVideoDescription}";
                            if (!string.IsNullOrWhiteSpace(selectedPost.Selftext))
                            { videoDescription += $"\n\nPost Text:\n{TextUtilities.CleanTextForTts(selectedPost.Selftext).Substring(0, Math.Min(TextUtilities.CleanTextForTts(selectedPost.Selftext).Length, 300))}..."; }
                            var uploadedVideo = await youTubeService.UploadVideoAsync(finalVideoPath, videoTitle, videoDescription, youtubeOptions.DefaultVideoTags.ToArray(), youtubeOptions.DefaultVideoCategoryId, youtubeOptions.DefaultVideoPrivacyStatus);
                            if (uploadedVideo != null) { Console.WriteLine($"Successfully uploaded video! YouTube ID: {uploadedVideo.Id}"); }
                            else { Console.Error.WriteLine("YouTube upload failed. Check previous logs."); }
                        }
                        else { Console.WriteLine("\nTesting Mode: YouTube upload skipped."); }

                        if (videoOptions.CleanUpIntermediateFiles)
                        {
                            Console.WriteLine("Cleaning up intermediate files...");
                            foreach (var clipPath in individualVideoClips)
                            {
                                if (clipPath.StartsWith(clipsDir) && File.Exists(clipPath))
                                { File.Delete(clipPath); }
                            }
                            foreach (var filePath in intermediateFilesToClean)
                            { try { if (File.Exists(filePath)) File.Delete(filePath); } catch (Exception ex) { Console.Error.WriteLine($"Error deleting file {filePath}: {ex.Message}"); } }
                            Console.WriteLine("Intermediate files cleanup process completed.");
                        }
                    }
                    else { Console.Error.WriteLine("Failed to concatenate video clips. YouTube upload will be skipped."); }
                }
                else { Console.WriteLine("No video clips to concatenate for YouTube upload."); }
            }
        }
        else { Console.WriteLine($"No top posts found in /r/{redditOptions.Subreddit}."); }

        Console.WriteLine("\nEnd of Step 23. Press any key to exit.");
        Console.ReadKey();
    }
}

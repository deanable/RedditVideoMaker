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
using SixLabors.Fonts; // Required for FontFamily and Font for TextUtilities

public class Program
{
    public static async Task Main(string[] args)
    {
        string baseDirectory = AppContext.BaseDirectory;
        string ffmpegBinFolder = Path.Combine(baseDirectory, "ffmpeg_bin");
        if (Directory.Exists(ffmpegBinFolder) && File.Exists(Path.Combine(ffmpegBinFolder, "ffmpeg.exe")) && File.Exists(Path.Combine(ffmpegBinFolder, "ffprobe.exe")))
        { GlobalFFOptions.Configure(new FFOptions { BinaryFolder = ffmpegBinFolder }); Console.WriteLine($"FFMpegCore: Configured from: {ffmpegBinFolder}"); }
        else { Console.Error.WriteLine($"FFMpegCore Error: ffmpeg_bin or executables not found at {ffmpegBinFolder}. Using PATH if available."); }

        Console.WriteLine("\nReddit Video Maker Bot C# - Step 16.2: Handling Long Self-Texts");

        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var services = new ServiceCollection();
        services.Configure<RedditOptions>(configuration.GetSection(RedditOptions.SectionName));
        services.Configure<VideoOptions>(configuration.GetSection(VideoOptions.SectionName));
        services.AddSingleton<RedditService>();
        services.AddSingleton<TtsService>();
        services.AddSingleton<ImageService>();
        services.AddSingleton<VideoService>();
        var serviceProvider = services.BuildServiceProvider();

        var redditOptions = serviceProvider.GetService<IOptions<RedditOptions>>()?.Value;
        var videoOptions = serviceProvider.GetService<IOptions<VideoOptions>>()?.Value;

        if (redditOptions == null || videoOptions == null || string.IsNullOrWhiteSpace(redditOptions.Subreddit))
        { Console.Error.WriteLine("Error: Options not configured properly in appsettings.json."); Console.ReadKey(); return; }

        if (string.IsNullOrWhiteSpace(videoOptions.BackgroundVideoPath) || !File.Exists(videoOptions.BackgroundVideoPath))
        { Console.Error.WriteLine($"Error: BackgroundVideoPath is not configured or file not found: '{videoOptions.BackgroundVideoPath}'. Please check appsettings.json."); Console.ReadKey(); return; }


        var redditService = serviceProvider.GetRequiredService<RedditService>();
        var ttsService = serviceProvider.GetRequiredService<TtsService>();
        var imageService = serviceProvider.GetRequiredService<ImageService>();
        var videoService = serviceProvider.GetRequiredService<VideoService>();

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

                // --- Font setup for text splitting ---
                FontFamily? nullableMeasuringFontFamily = null;
                if (SystemFonts.TryGet("DejaVu Sans", out FontFamily dejavu))
                {
                    nullableMeasuringFontFamily = dejavu;
                    Console.WriteLine("Program.cs: Using 'DejaVu Sans' for text measurement.");
                }
                else if (SystemFonts.Families.Any()) // Check if any families exist before calling FirstOrDefault
                {
                    FontFamily firstSystemFont = SystemFonts.Families.First(); // This should be a valid one
                    // Check if the obtained font family is valid (e.g., has a name)
                    if (!string.IsNullOrEmpty(firstSystemFont.Name))
                    {
                        nullableMeasuringFontFamily = firstSystemFont;
                        Console.WriteLine($"Program.cs: Warning - 'DejaVu Sans' not found. Using first available system font '{firstSystemFont.Name}' for text measurement.");
                    }
                }

                if (nullableMeasuringFontFamily == null) // This check now means no valid font was found
                {
                    Console.Error.WriteLine("Program.cs: Critical - No usable system font found for text measurement. Self-text splitting might be inaccurate or skipped.");
                }


                // --- Process Post Title as a Clip ---
                Console.WriteLine($"\n--- Processing Post Title ---");
                string titleId = $"post_{selectedPost.Id!.Replace(" ", "_")}";
                string titleTtsPath = Path.Combine(ttsDir, $"audio_{titleId}.wav");
                string titleCardPath = Path.Combine(imgDir, $"image_{titleId}.png");
                string titleClipPath = Path.Combine(clipsDir, $"clip_{titleId}.mp4");

                if (await ttsService.TextToSpeechAsync(selectedPost.Title!, titleTtsPath) &&
                    await imageService.CreateRedditContentCardAsync(
                        selectedPost.Title!, selectedPost.Author, selectedPost.Score, titleCardPath,
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
                    Console.WriteLine($"\n--- Processing Post Self-Text (with splitting if needed) ---");

                    FontFamily actualMeasuringFontFamily = (FontFamily)nullableMeasuringFontFamily; // Cast is safe here due to the null check
                    Font selfTextMeasuringFont = actualMeasuringFontFamily.CreateFont(videoOptions.ContentTargetFontSize, FontStyle.Regular);

                    float textPadding = Math.Max(15f, Math.Min(videoOptions.CardWidth * 0.05f, videoOptions.CardHeight * 0.05f));
                    float selfTextCardContentWidth = videoOptions.CardWidth - (2 * textPadding);
                    float selfTextCardContentHeight = videoOptions.CardHeight - (2 * textPadding);

                    List<string> selfTextPages = TextUtilities.SplitTextIntoPages(
                        selectedPost.Selftext,
                        selfTextMeasuringFont,
                        selfTextCardContentWidth,
                        selfTextCardContentHeight);

                    Console.WriteLine($"Self-text split into {selfTextPages.Count} page(s).");
                    int selfTextPageIndex = 0;
                    foreach (string selfTextPage in selfTextPages)
                    {
                        if (string.IsNullOrWhiteSpace(selfTextPage)) continue;

                        selfTextPageIndex++;
                        Console.WriteLine($"-- Processing self-text page {selfTextPageIndex} --");
                        string selfTextId = $"post_{selectedPost.Id!}_selftext_p{selfTextPageIndex}";
                        string selfTextTtsPath = Path.Combine(ttsDir, $"audio_{selfTextId}.wav");
                        string selfTextCardPath = Path.Combine(imgDir, $"image_{selfTextId}.png");
                        string selfTextClipPath = Path.Combine(clipsDir, $"clip_{selfTextId}.mp4");

                        string pageIndicator = selfTextPages.Count > 1 ? $" (Page {selfTextPageIndex}/{selfTextPages.Count})" : "";

                        if (await ttsService.TextToSpeechAsync(selfTextPage, selfTextTtsPath) &&
                            await imageService.CreateRedditContentCardAsync(
                                selfTextPage + pageIndicator,
                                null,
                                null,
                                selfTextCardPath,
                                videoOptions.CardWidth,
                                videoOptions.CardHeight,
                                videoOptions.CardBackgroundColor,
                                videoOptions.CardFontColor,
                                videoOptions.CardMetadataFontColor) &&
                            await videoService.CreateClipWithBackgroundAsync(videoOptions.BackgroundVideoPath!, selfTextCardPath, selfTextTtsPath, selfTextClipPath, finalVideoWidth, finalVideoHeight))
                        {
                            Console.WriteLine($"Self-text clip (page {selfTextPageIndex}) created: {selfTextClipPath}");
                            individualVideoClips.Add(selfTextClipPath);
                        }
                        else { Console.Error.WriteLine($"Failed to process self-text clip (page {selfTextPageIndex})."); }
                    }
                }
                else if (string.IsNullOrWhiteSpace(selectedPost.Selftext))
                {
                    Console.WriteLine("No self-text for this post.");
                }
                else if (nullableMeasuringFontFamily == null)
                {
                    Console.Error.WriteLine("Skipping self-text processing as no usable measuring font was available.");
                }


                // --- Process Comments as Clips ---
                Console.WriteLine($"\nFetching comments for post ID: {selectedPost.Id}...");
                int commentsToFetch = videoOptions.NumberOfCommentsToInclude > 0 ? (videoOptions.NumberOfCommentsToInclude * 2) + 5 : 10;
                List<RedditCommentData>? comments = await redditService.GetCommentsAsync(selectedPost.Subreddit!, selectedPost.Id!, commentLimit: commentsToFetch);
                if (comments != null && comments.Any())
                {
                    var suitableComments = comments
                        .Where(c => !string.IsNullOrWhiteSpace(c.Body) && c.Body.Length > 10)
                        .Take(videoOptions.NumberOfCommentsToInclude)
                        .ToList();
                    Console.WriteLine($"\nProcessing {suitableComments.Count} suitable comments (target: {videoOptions.NumberOfCommentsToInclude})...");
                    int idx = 0;
                    foreach (var comment in suitableComments)
                    {
                        idx++;
                        Console.WriteLine($"\n--- Processing comment {idx} by {comment.Author} (Score: {comment.Score}) ---");
                        string cId = $"{selectedPost.Id}_c{idx}_{comment.Id?.Replace(" ", "_").Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_') ?? Guid.NewGuid().ToString()}";
                        string cTtsPath = Path.Combine(ttsDir, $"audio_{cId}.wav");
                        string cCardPath = Path.Combine(imgDir, $"image_{cId}.png");
                        string cClipPath = Path.Combine(clipsDir, $"clip_{cId}.mp4");

                        if (await ttsService.TextToSpeechAsync(comment.Body!, cTtsPath) &&
                            await imageService.CreateRedditContentCardAsync(
                                comment.Body!,
                                comment.Author,
                                comment.Score,
                                cCardPath,
                                videoOptions.CardWidth,
                                videoOptions.CardHeight,
                                videoOptions.CardBackgroundColor,
                                videoOptions.CardFontColor,
                                videoOptions.CardMetadataFontColor) &&
                            await videoService.CreateClipWithBackgroundAsync(videoOptions.BackgroundVideoPath!, cCardPath, cTtsPath, cClipPath, finalVideoWidth, finalVideoHeight))
                        {
                            Console.WriteLine($"Comment clip {idx} created: {cClipPath}");
                            individualVideoClips.Add(cClipPath);
                        }
                        else { Console.Error.WriteLine($"Failed to process comment clip {idx}."); }
                    }
                }
                else { Console.WriteLine("No comments found for the post."); }

                // --- Concatenate all video clips ---
                if (individualVideoClips.Any())
                {
                    string finalVideoPath = Path.Combine(finalDir, $"final_video_{selectedPost.Id}.mp4");
                    Console.WriteLine($"\nConcatenating {individualVideoClips.Count} clips into {finalVideoPath}...");
                    if (await videoService.ConcatenateVideosAsync(individualVideoClips, finalVideoPath))
                        Console.WriteLine($"Final video created: {finalVideoPath}");
                    else Console.Error.WriteLine("Failed to concatenate video clips.");
                }
                else { Console.WriteLine("No video clips to concatenate."); }
            }
        }
        else { Console.WriteLine($"No top posts found in /r/{redditOptions.Subreddit}."); }

        Console.WriteLine("\nEnd of Step 16.2. Press any key to exit.");
        Console.ReadKey();
    }
}

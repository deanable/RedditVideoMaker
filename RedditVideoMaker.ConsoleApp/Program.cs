using System;
using System.IO;
using System.Linq; // Required for .FirstOrDefault() and .Any()
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RedditVideoMaker.Core;
using FFMpegCore; // Required for GlobalFFOptions and FFOptions

public class Program
{
    public static async Task Main(string[] args)
    {
        // --- Start of FFmpeg Configuration ---
        string baseDirectory = AppContext.BaseDirectory;
        string ffmpegBinFolder = Path.Combine(baseDirectory, "ffmpeg_bin");

        if (Directory.Exists(ffmpegBinFolder) &&
            File.Exists(Path.Combine(ffmpegBinFolder, "ffmpeg.exe")) &&
            File.Exists(Path.Combine(ffmpegBinFolder, "ffprobe.exe")))
        {
            Console.WriteLine($"FFMpegCore: Configuring to use FFmpeg from: {ffmpegBinFolder}");
            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = ffmpegBinFolder });
            // Optional: Configure a temporary folder for FFMpegCore if needed
            // string tempFilesFolder = Path.Combine(Path.GetTempPath(), "RedditVideoMakerTemp");
            // Directory.CreateDirectory(tempFilesFolder); // Ensure it exists
            // GlobalFFOptions.Configure(new FFOptions { BinaryFolder = ffmpegBinFolder, TemporaryFilesFolder = tempFilesFolder });
        }
        else
        {
            Console.Error.WriteLine($"FFMpegCore Error: ffmpeg_bin folder, ffmpeg.exe, or ffprobe.exe not found at {ffmpegBinFolder}.");
            Console.Error.WriteLine("Please ensure FFmpeg executables are copied to the 'ffmpeg_bin' subfolder in the application's output directory.");
            Console.Error.WriteLine("The application might not be able to create videos. It will try to use system PATH FFmpeg if available (which might also fail if not configured).");
        }
        // --- End of FFmpeg Configuration ---

        Console.WriteLine("\nReddit Video Maker Bot C# - Step 6: Basic Video Creation");

        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var services = new ServiceCollection();
        services.Configure<RedditOptions>(configuration.GetSection(RedditOptions.SectionName));
        services.Configure<VideoOptions>(configuration.GetSection(VideoOptions.SectionName));

        // Register services for Dependency Injection
        services.AddSingleton<RedditService>();
        services.AddSingleton<TtsService>();
        services.AddSingleton<ImageService>();
        services.AddSingleton<VideoService>(); // Add VideoService

        var serviceProvider = services.BuildServiceProvider();

        var redditOptions = serviceProvider.GetService<IOptions<RedditOptions>>()?.Value;
        var videoOptions = serviceProvider.GetService<IOptions<VideoOptions>>()?.Value;

        if (redditOptions == null || string.IsNullOrWhiteSpace(redditOptions.Subreddit) || videoOptions == null)
        {
            Console.Error.WriteLine("Error: Reddit or Video options not configured properly in appsettings.json.");
            Console.ReadKey();
            return;
        }

        var redditService = serviceProvider.GetService<RedditService>();
        var ttsService = serviceProvider.GetService<TtsService>();
        var imageService = serviceProvider.GetService<ImageService>();
        var videoService = serviceProvider.GetService<VideoService>();

        if (redditService == null || ttsService == null || imageService == null || videoService == null)
        {
            Console.Error.WriteLine("Error: One or more services could not be resolved. Check DI registration.");
            Console.ReadKey();
            return;
        }

        Console.WriteLine($"\nFetching top posts from subreddit: /r/{redditOptions.Subreddit}");
        List<RedditPostData>? topPosts = await redditService.GetTopPostsAsync(redditOptions.Subreddit, limit: 1);

        if (topPosts != null && topPosts.Any())
        {
            RedditPostData firstPost = topPosts.First();
            Console.WriteLine($"\nSelected post: {firstPost.Title} by {firstPost.Author}");

            if (!string.IsNullOrWhiteSpace(firstPost.Id) && !string.IsNullOrWhiteSpace(firstPost.Subreddit))
            {
                Console.WriteLine($"\nFetching comments for post ID: {firstPost.Id}...");
                List<RedditCommentData>? comments = await redditService.GetCommentsAsync(firstPost.Subreddit, firstPost.Id, commentLimit: 5);

                if (comments != null && comments.Any())
                {
                    RedditCommentData? firstComment = comments.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Body));

                    if (firstComment != null && !string.IsNullOrWhiteSpace(firstComment.Body))
                    {
                        Console.WriteLine($"\nProcessing first comment by {firstComment.Author}: \"{firstComment.Body.Substring(0, Math.Min(firstComment.Body.Length, 70))}...\"");

                        // Define output directories
                        string baseOutputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "output_files");
                        string ttsOutputDirectory = Path.Combine(baseOutputDirectory, "tts");
                        string imageOutputDirectory = Path.Combine(baseOutputDirectory, "images");
                        string videoOutputDirectory = Path.Combine(baseOutputDirectory, "videos");

                        // Ensure directories exist
                        Directory.CreateDirectory(ttsOutputDirectory);
                        Directory.CreateDirectory(imageOutputDirectory);
                        Directory.CreateDirectory(videoOutputDirectory);

                        string safeFileId = firstComment.Id?.Replace(" ", "_").Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_') ?? Guid.NewGuid().ToString();
                        string ttsFilePath = Path.Combine(ttsOutputDirectory, $"comment_{safeFileId}.wav");
                        string imageFilePath = Path.Combine(imageOutputDirectory, $"comment_{safeFileId}.png");
                        string videoFilePath = Path.Combine(videoOutputDirectory, $"comment_{safeFileId}.mp4");

                        // Perform TTS
                        bool ttsSuccess = await ttsService.TextToSpeechAsync(firstComment.Body, ttsFilePath);
                        if (ttsSuccess) Console.WriteLine($"TTS successful: {ttsFilePath}");
                        else Console.Error.WriteLine("TTS failed. Video creation might fail or be silent.");

                        // Perform Image Generation
                        int imageWidth = 1080;
                        int imageHeight = 720;

                        if (!string.IsNullOrWhiteSpace(videoOptions.OutputResolution) && videoOptions.OutputResolution.Contains('x', StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = videoOptions.OutputResolution.Split('x', 'X');
                            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                            {
                                imageWidth = w;
                                imageHeight = h;
                            }
                        }
                        Console.WriteLine($"Using image dimensions: {imageWidth}x{imageHeight}");

                        bool imageSuccess = await imageService.CreateImageFromTextAsync(firstComment.Body, imageFilePath, imageWidth, imageHeight);
                        if (imageSuccess) Console.WriteLine($"Image generation successful: {imageFilePath}");
                        else Console.Error.WriteLine("Image generation failed. Video creation might fail or have no visuals.");

                        // Perform Video Creation if both TTS and Image were successful
                        if (ttsSuccess && imageSuccess)
                        {
                            Console.WriteLine($"\nAttempting video creation for comment {safeFileId}...");
                            bool videoSuccess = await videoService.CreateVideoFromImageAndAudioAsync(imageFilePath, ttsFilePath, videoFilePath);

                            if (videoSuccess) Console.WriteLine($"Video creation successful: {videoFilePath}");
                            else Console.Error.WriteLine("Video creation failed. Check FFmpeg configuration and file paths.");
                        }
                        else
                        {
                            Console.Error.WriteLine("Skipping video creation due to failures in TTS or image generation.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("No suitable comment found for processing (e.g., no comment with text).");
                    }
                }
                else
                {
                    Console.WriteLine("No comments found for the post.");
                }
            }
            else
            {
                Console.Error.WriteLine("Fetched post is missing ID or Subreddit name, cannot fetch comments.");
            }
        }
        else
        {
            Console.WriteLine($"No top posts found in /r/{redditOptions.Subreddit}.");
        }

        Console.WriteLine("\nEnd of Step 6. Press any key to exit.");
        Console.ReadKey();
    }
}

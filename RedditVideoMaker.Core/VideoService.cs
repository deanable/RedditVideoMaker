// VideoService.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
using System.Threading.Tasks;
using FFMpegCore; // Main FFMpegCore namespace
using FFMpegCore.Enums; // For enums like Speed (though we might use custom args for presets)
using System.Drawing; // For System.Drawing.Size (add System.Drawing.Common NuGet if not already present for .NET Core/5+)
                      // Or use SixLabors.ImageSharp.Size if preferred and System.Drawing.Common is problematic.

namespace RedditVideoMaker.Core
{
    public class VideoService
    {
        public VideoService()
        {
            // Constructor
            // GlobalFFOptions should be configured in Program.cs or a similar startup location
            // to specify the binary folder for ffmpeg.exe and ffprobe.exe if they are bundled.
            // Example (done in Program.cs):
            // string ffmpegBinFolder = Path.Combine(AppContext.BaseDirectory, "ffmpeg_bin");
            // GlobalFFOptions.Configure(new FFOptions { BinaryFolder = ffmpegBinFolder });
        }

        /// <summary>
        /// Creates a video from a single image and an audio file.
        /// The image will be displayed for the duration of the audio.
        /// </summary>
        /// <param name="imagePath">Path to the input image file.</param>
        /// <param name="audioPath">Path to the input audio file (e.g., WAV).</param>
        /// <param name="outputVideoPath">Path to save the output video file (e.g., MP4).</param>
        /// <returns>True if successful, false otherwise.</returns>
        public async Task<bool> CreateVideoFromImageAndAudioAsync(
            string imagePath,
            string audioPath,
            string outputVideoPath)
        {
            if (!File.Exists(imagePath))
            {
                Console.Error.WriteLine($"VideoService Error: Input image not found at {imagePath}");
                return false;
            }
            if (!File.Exists(audioPath))
            {
                Console.Error.WriteLine($"VideoService Error: Input audio not found at {audioPath}");
                return false;
            }
            if (string.IsNullOrWhiteSpace(outputVideoPath))
            {
                Console.Error.WriteLine("VideoService Error: Output video path cannot be empty.");
                return false;
            }

            try
            {
                // Ensure the output directory exists
                string? directory = Path.GetDirectoryName(outputVideoPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Get audio duration to set video length
                // FFProbe.AnalyseAsync requires ffprobe.exe to be accessible (either in PATH or configured in GlobalFFOptions)
                IMediaAnalysis audioInfo = await FFProbe.AnalyseAsync(audioPath);
                TimeSpan videoDuration = audioInfo.Duration;

                if (videoDuration <= TimeSpan.Zero)
                {
                    Console.Error.WriteLine($"VideoService Error: Could not determine audio duration from {audioPath}, or audio is empty. Duration reported: {videoDuration}. Cannot create video.");
                    return false;
                }
                Console.WriteLine($"VideoService: Determined video duration from audio: {videoDuration}");

                // Get image dimensions to set video size
                var imageInfoSharp = SixLabors.ImageSharp.Image.Identify(imagePath);
                if (imageInfoSharp == null)
                {
                    Console.Error.WriteLine($"VideoService Error: Could not read image dimensions from {imagePath} using ImageSharp.");
                    return false;
                }
                var videoWidth = imageInfoSharp.Width;
                var videoHeight = imageInfoSharp.Height;

                // Some codecs (like libx264) require even dimensions.
                if (videoWidth % 2 != 0) videoWidth++;
                if (videoHeight % 2 != 0) videoHeight++;

                Console.WriteLine($"VideoService: Creating video '{Path.GetFileName(outputVideoPath)}' from image '{Path.GetFileName(imagePath)}' and audio '{Path.GetFileName(audioPath)}'.");
                Console.WriteLine($"VideoService: Output resolution: {videoWidth}x{videoHeight}, Duration: {videoDuration}");

                bool success = await FFMpegArguments
                    .FromFileInput(imagePath, false, options => options.Loop(1)) // Input image, loop it. 'false' means don't verify file existence (we did it already)
                    .AddFileInput(audioPath) // Input audio
                    .OutputToFile(outputVideoPath, true, options => options // Output to file, true to overwrite if exists
                        .WithVideoCodec(VideoCodec.LibX264)
                        .WithAudioCodec(AudioCodec.Aac)
                        .WithAudioBitrate(192)              // 192k audio bitrate
                        .WithVideoBitrate(2000)             // 2000k video bitrate (adjust as needed)
                        .WithCustomArgument("-pix_fmt yuv420p") // Set pixel format using custom argument
                        .WithCustomArgument("-tune stillimage") // Set tune option using custom argument
                        .WithDuration(videoDuration)
                        .WithCustomArgument($"-s {videoWidth}x{videoHeight}") // Set video size using custom argument
                        .WithCustomArgument("-preset ultrafast") // Set preset/speed using custom argument
                        .UsingMultithreading(true)
                        .WithFastStart())
                    .ProcessAsynchronously();

                if (success)
                {
                    Console.WriteLine($"VideoService: Video successfully created at {outputVideoPath}");
                    return true;
                }
                else
                {
                    Console.Error.WriteLine($"VideoService Error: FFmpeg processing failed for {outputVideoPath}. Check FFmpeg logs or console output if FFMpegCore provides it.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"VideoService Error: An unexpected error occurred during video creation. {ex.Message}");
                Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");
                if (ex.Message.ToLower().Contains("ffmpeg") && (ex.Message.ToLower().Contains("not found") || ex.Message.ToLower().Contains("cannot find")))
                {
                    Console.Error.WriteLine("VideoService Hint: Ensure FFmpeg (ffmpeg.exe and ffprobe.exe) is correctly configured via GlobalFFOptions.Configure or is in your system's PATH.");
                }
                return false;
            }
        }
    }
}

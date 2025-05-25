// VideoService.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Arguments;
using System.Drawing;
using System.Linq;

namespace RedditVideoMaker.Core
{
    public class VideoService
    {
        public VideoService() { /* GlobalFFOptions configured in Program.cs */ }

        public async Task<bool> CreateClipWithBackgroundAsync(
            string backgroundVideoPath,
            string overlayCardPath,
            string ttsAudioPath,
            string outputClipPath,
            int finalVideoWidth,
            int finalVideoHeight)
        {
            if (string.IsNullOrWhiteSpace(backgroundVideoPath) || !File.Exists(backgroundVideoPath))
            { Console.Error.WriteLine($"VideoService Error: Background video not found or path empty: {backgroundVideoPath}"); return false; }
            if (!File.Exists(overlayCardPath)) { Console.Error.WriteLine($"VideoService Error: Overlay card image not found: {overlayCardPath}"); return false; }
            if (!File.Exists(ttsAudioPath)) { Console.Error.WriteLine($"VideoService Error: TTS audio not found: {ttsAudioPath}"); return false; }
            if (string.IsNullOrWhiteSpace(outputClipPath)) { Console.Error.WriteLine("VideoService Error: Output clip path empty."); return false; }

            try
            {
                string? directory = Path.GetDirectoryName(outputClipPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

                IMediaAnalysis audioInfo = await FFProbe.AnalyseAsync(ttsAudioPath);
                TimeSpan clipDuration = audioInfo.Duration;
                if (clipDuration <= TimeSpan.Zero) { Console.Error.WriteLine($"VideoService Error: Invalid audio duration for {ttsAudioPath}."); return false; }

                Console.WriteLine($"VideoService: Creating clip '{Path.GetFileName(outputClipPath)}'. Duration: {clipDuration}, Output Size: {finalVideoWidth}x{finalVideoHeight}");

                if (finalVideoWidth % 2 != 0) finalVideoWidth++;
                if (finalVideoHeight % 2 != 0) finalVideoHeight++;

                var inputArguments = FFMpegArguments
                    // Input 0: Background video - Use -stream_loop -1 for video inputs
                    .FromFileInput(backgroundVideoPath, false,
                        opt => opt.WithCustomArgument("-stream_loop -1"))
                    // Input 1: Overlay card image - Use -loop 1 and specify a framerate for image inputs
                    .AddFileInput(overlayCardPath, false,
                        opt => {
                            opt.WithCustomArgument("-loop 1");
                            opt.WithCustomArgument("-framerate 25"); // Assuming 25 fps for the image stream
                            // opt.WithCustomArgument("-c:v png"); // Optional: specify image codec if needed
                        });

                inputArguments.AddFileInput(ttsAudioPath); // Input 2: TTS Audio

                string filterGraph =
                    $"[0:v]scale={finalVideoWidth}:{finalVideoHeight}:force_original_aspect_ratio=decrease,pad={finalVideoWidth}:{finalVideoHeight}:(ow-iw)/2:(oh-ih)/2,setsar=1[bg];" +
                    $"[1:v]scale='iw*min(1,min({finalVideoWidth}*0.9/iw,{finalVideoHeight}*0.8/ih))':-1,format=rgba[fg];" +
                    $"[bg][fg]overlay=(W-w)/2:(H-h)/2";

                bool success = await inputArguments
                    .OutputToFile(outputClipPath, true, options => options
                        .WithVideoCodec(VideoCodec.LibX264)
                        .WithAudioCodec(AudioCodec.Aac)
                        .WithAudioBitrate(192)
                        .WithVideoBitrate(2500)
                        .WithCustomArgument($"-filter_complex \"{filterGraph}\"")
                        .WithCustomArgument("-map 2:a")
                        .WithDuration(clipDuration)
                        .WithCustomArgument("-preset medium")
                        .UsingMultithreading(true)
                        .WithFastStart())
                    .ProcessAsynchronously();

                if (success)
                {
                    Console.WriteLine($"VideoService: Clip with background successfully created: {outputClipPath}");
                    return true;
                }
                else
                {
                    Console.Error.WriteLine($"VideoService Error: FFmpeg processing failed for clip with background: {outputClipPath}.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"VideoService Error: An unexpected error occurred during clip creation with background. {ex.Message}");
                Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");
                return false;
            }
        }

        public async Task<bool> ConcatenateVideosAsync(List<string> videoClipPaths, string outputVideoPath)
        {
            if (videoClipPaths == null || !videoClipPaths.Any()) { Console.Error.WriteLine("VideoService Error: No video clips for concatenation."); return false; }
            foreach (var clipPath in videoClipPaths) { if (!File.Exists(clipPath)) { Console.Error.WriteLine($"VideoService Error: Input clip for concat not found: {clipPath}"); return false; } }
            if (string.IsNullOrWhiteSpace(outputVideoPath)) { Console.Error.WriteLine("VideoService Error: Output path for concat empty."); return false; }

            if (videoClipPaths.Count == 1)
            {
                try { File.Copy(videoClipPaths[0], outputVideoPath, true); Console.WriteLine($"VideoService: Single clip copied to {outputVideoPath}"); return true; }
                catch (Exception ex) { Console.Error.WriteLine($"VideoService Error: Failed to copy single clip. {ex.Message}"); return false; }
            }

            Console.WriteLine($"VideoService: Concatenating {videoClipPaths.Count} clips into {Path.GetFileName(outputVideoPath)}...");
            string fileListPath = Path.Combine(Path.GetTempPath(), $"ffmpeg_concat_list_{Guid.NewGuid()}.txt");
            try
            {
                var fileListContent = videoClipPaths.Select(path => $"file '{path.Replace("\\", "/")}'");
                await File.WriteAllLinesAsync(fileListPath, fileListContent);

                bool success = await FFMpegArguments
                    .FromFileInput(fileListPath, false, options =>
                    {
                        options.WithArgument(new CustomArgument("-f concat"));
                        options.WithArgument(new CustomArgument("-safe 0"));
                    })
                    .OutputToFile(outputVideoPath, true, options => options
                        .CopyChannel()
                        .WithFastStart())
                    .ProcessAsynchronously();

                if (success) { Console.WriteLine($"VideoService: Concatenation successful: {outputVideoPath}"); return true; }
                else { Console.Error.WriteLine($"VideoService Error: FFmpeg concatenation failed for {outputVideoPath}."); return false; }
            }
            catch (Exception ex) { Console.Error.WriteLine($"VideoService Error: Unexpected error during concat. {ex.Message}"); return false; }
            finally { if (File.Exists(fileListPath)) File.Delete(fileListPath); }
        }
    }
}

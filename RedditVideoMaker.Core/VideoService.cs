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
using System.Text;
using System.Globalization;
using Microsoft.Extensions.Options; // Required for IOptions

namespace RedditVideoMaker.Core
{
    public class VideoService
    {
        private readonly VideoOptions _videoOptions;

        public VideoService(IOptions<VideoOptions> videoOptions)
        {
            _videoOptions = videoOptions.Value;
            // GlobalFFOptions (like BinaryFolder for ffmpeg executables) should be configured 
            // in Program.cs or a similar application startup location.
        }

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
                    .FromFileInput(backgroundVideoPath, false,
                        opt => opt.WithCustomArgument("-stream_loop -1"))
                    .AddFileInput(overlayCardPath, false,
                        opt => {
                            opt.WithCustomArgument("-loop 1");
                            opt.WithCustomArgument("-framerate 25");
                        });

                inputArguments.AddFileInput(ttsAudioPath);

                string filterGraph =
                    $"[0:v]scale={finalVideoWidth}:{finalVideoHeight}:force_original_aspect_ratio=decrease,pad={finalVideoWidth}:{finalVideoHeight}:(ow-iw)/2:(oh-ih)/2,setsar=1[bg];" +
                    $"[1:v]scale='iw*min(1,min({finalVideoWidth}*0.9/iw,{finalVideoHeight}*0.8/ih))':-1,format=rgba[fg];" +
                    $"[bg][fg]overlay=(W-w)/2:(H-h)/2[vout]";

                bool success = await inputArguments
                    .OutputToFile(outputClipPath, true, options => options
                        .WithVideoCodec(VideoCodec.LibX264)
                        .WithAudioCodec(AudioCodec.Aac)
                        .WithAudioBitrate(192)
                        .WithVideoBitrate(2500)
                        .WithCustomArgument($"-filter_complex \"{filterGraph}\"")
                        .WithCustomArgument("-map [vout]")
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

        /// <summary>
        /// Concatenates multiple video clips into a single output video.
        /// Applies transitions if enabled in VideoOptions.
        /// </summary>
        /// <param name="videoClipPaths">A list of paths to the video clips to concatenate.</param>
        /// <param name="outputVideoPath">The path for the final concatenated video.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public async Task<bool> ConcatenateVideosAsync(List<string> videoClipPaths, string outputVideoPath)
        {
            if (videoClipPaths == null || !videoClipPaths.Any()) { Console.Error.WriteLine("VideoService Error: No video clips for concatenation."); return false; }
            foreach (var clipPath in videoClipPaths) { if (!File.Exists(clipPath)) { Console.Error.WriteLine($"VideoService Error: Input clip for concat not found: {clipPath}"); return false; } }
            if (string.IsNullOrWhiteSpace(outputVideoPath)) { Console.Error.WriteLine("VideoService Error: Output path for concat empty."); return false; }

            if (videoClipPaths.Count == 1)
            {
                try
                {
                    File.Copy(videoClipPaths[0], outputVideoPath, true);
                    Console.WriteLine($"VideoService: Single clip copied to {outputVideoPath}"); return true;
                }
                catch (Exception ex) { Console.Error.WriteLine($"VideoService Error: Failed to copy single clip. {ex.Message}"); return false; }
            }

            Console.WriteLine($"VideoService: Starting concatenation of {videoClipPaths.Count} clips into {Path.GetFileName(outputVideoPath)}.");
            if (_videoOptions.EnableTransitions)
            {
                Console.WriteLine($"VideoService: Transitions ENABLED, duration: {_videoOptions.TransitionDurationSeconds}s.");
            }
            else
            {
                Console.WriteLine("VideoService: Transitions DISABLED. Using simple concat filter.");
            }

            try
            {
                var arguments = FFMpegArguments.FromFileInput(videoClipPaths[0]);
                var filterComplexBuilder = new StringBuilder();

                var clipDurations = new List<TimeSpan>();
                var firstClipInfo = await FFProbe.AnalyseAsync(videoClipPaths[0]);
                if (firstClipInfo.Duration <= TimeSpan.Zero)
                {
                    Console.Error.WriteLine($"VideoService Error: Could not get valid duration for clip {videoClipPaths[0]}.");
                    return false;
                }
                clipDurations.Add(firstClipInfo.Duration);

                for (int i = 1; i < videoClipPaths.Count; i++)
                {
                    arguments = arguments.AddFileInput(videoClipPaths[i]);
                    var mediaInfo = await FFProbe.AnalyseAsync(videoClipPaths[i]);
                    if (mediaInfo.Duration <= TimeSpan.Zero)
                    {
                        Console.Error.WriteLine($"VideoService Error: Could not get valid duration for clip {videoClipPaths[i]}.");
                        return false;
                    }
                    clipDurations.Add(mediaInfo.Duration);
                }

                string currentVideoLabel = "[0:v]";
                string currentAudioLabel = "[0:a]";

                if (_videoOptions.EnableTransitions && videoClipPaths.Count > 1)
                {
                    double accumulatedDurationBeforeTransitionPoint = 0;
                    double transitionDuration = _videoOptions.TransitionDurationSeconds;

                    for (int i = 0; i < videoClipPaths.Count - 1; i++)
                    {
                        string nextVideoInputLabel = $"[{i + 1}:v]";
                        string nextAudioInputLabel = $"[{i + 1}:a]";

                        string videoOutStageLabel = (i == videoClipPaths.Count - 2) ? "[vout]" : $"[vtemp{i}]";
                        string audioOutStageLabel = (i == videoClipPaths.Count - 2) ? "[aout]" : $"[atemp{i}]";

                        double xfadeOffset = accumulatedDurationBeforeTransitionPoint + (clipDurations[i].TotalSeconds - transitionDuration);

                        filterComplexBuilder.Append($"{currentVideoLabel}{nextVideoInputLabel}" +
                                                    $"xfade=transition=fade" +
                                                    $":duration={transitionDuration.ToString(CultureInfo.InvariantCulture)}" +
                                                    $":offset={xfadeOffset.ToString(CultureInfo.InvariantCulture)}{videoOutStageLabel};");

                        filterComplexBuilder.Append($"{currentAudioLabel}{nextAudioInputLabel}" +
                                                    $"acrossfade=d={transitionDuration.ToString(CultureInfo.InvariantCulture)}{audioOutStageLabel};");

                        currentVideoLabel = videoOutStageLabel;
                        currentAudioLabel = audioOutStageLabel;

                        accumulatedDurationBeforeTransitionPoint += clipDurations[i].TotalSeconds - transitionDuration;
                    }
                }
                else // Simple concat filter if transitions are disabled or only one effective segment
                {
                    for (int i = 0; i < videoClipPaths.Count; i++)
                    {
                        filterComplexBuilder.Append($"[{i}:v:0][{i}:a:0]");
                    }
                    filterComplexBuilder.Append($"concat=n={videoClipPaths.Count}:v=1:a=1[vout][aout]");
                    currentVideoLabel = "[vout]"; // Final output labels for simple concat
                    currentAudioLabel = "[aout]";
                }

                if (filterComplexBuilder.Length > 0 && filterComplexBuilder[filterComplexBuilder.Length - 1] == ';')
                {
                    filterComplexBuilder.Length--;
                }

                bool success = await arguments
                    .OutputToFile(outputVideoPath, true, options => options
                        .WithVideoCodec(VideoCodec.LibX264)
                        .WithAudioCodec(AudioCodec.Aac)
                        .WithAudioBitrate(192)
                        .WithVideoBitrate(2500)
                        .WithCustomArgument($"-filter_complex \"{filterComplexBuilder.ToString()}\"")
                        .WithCustomArgument($"-map \"{currentVideoLabel}\"")
                        .WithCustomArgument($"-map \"{currentAudioLabel}\"")
                        .WithCustomArgument("-preset medium")
                        .WithFastStart())
                    .ProcessAsynchronously();

                if (success) { Console.WriteLine($"VideoService: Concatenation successful: {outputVideoPath}"); return true; }
                else { Console.Error.WriteLine($"VideoService Error: FFmpeg concatenation failed for {outputVideoPath}."); return false; }
            }
            catch (Exception ex) { Console.Error.WriteLine($"VideoService Error: Unexpected error during concat. {ex.Message}"); return false; }
        }
    }
}

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
using Microsoft.Extensions.Options;

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

        /// <summary>
        /// Creates a video clip by overlaying an image (card) onto a background video, 
        /// synchronized with TTS audio, and optionally mixing in background music.
        /// </summary>
        /// <param name="backgroundVideoPath">Path to the looping visual background video.</param>
        /// <param name="overlayCardPath">Path to the image card (e.g., comment image).</param>
        /// <param name="ttsAudioPath">Path to the TTS audio file for this clip.</param>
        /// <param name="outputClipPath">Path to save the output video clip.</param>
        /// <param name="finalVideoWidth">Width of the final output video clip.</param>
        /// <param name="finalVideoHeight">Height of the final output video clip.</param>
        /// <returns>True if successful, false otherwise.</returns>
        public async Task<bool> CreateClipWithBackgroundAsync(
            string backgroundVideoPath,
            string overlayCardPath,
            string ttsAudioPath,
            string outputClipPath,
            int finalVideoWidth,
            int finalVideoHeight)
        {
            if (string.IsNullOrWhiteSpace(backgroundVideoPath) || !File.Exists(backgroundVideoPath))
            { Console.Error.WriteLine($"VideoService Error: Visual background video not found: {backgroundVideoPath}"); return false; }
            if (!File.Exists(overlayCardPath)) { Console.Error.WriteLine($"VideoService Error: Overlay card image not found: {overlayCardPath}"); return false; }
            if (!File.Exists(ttsAudioPath)) { Console.Error.WriteLine($"VideoService Error: TTS audio not found: {ttsAudioPath}"); return false; }
            if (string.IsNullOrWhiteSpace(outputClipPath)) { Console.Error.WriteLine("VideoService Error: Output clip path empty."); return false; }

            try
            {
                string? directory = Path.GetDirectoryName(outputClipPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

                IMediaAnalysis ttsAudioInfo = await FFProbe.AnalyseAsync(ttsAudioPath);
                TimeSpan clipDuration = ttsAudioInfo.Duration;
                if (clipDuration <= TimeSpan.Zero) { Console.Error.WriteLine($"VideoService Error: Invalid audio duration for {ttsAudioPath}."); return false; }

                Console.WriteLine($"VideoService: Creating clip '{Path.GetFileName(outputClipPath)}'. Duration: {clipDuration}, Output Size: {finalVideoWidth}x{finalVideoHeight}");

                if (finalVideoWidth % 2 != 0) finalVideoWidth++;
                if (finalVideoHeight % 2 != 0) finalVideoHeight++;

                var inputArguments = FFMpegArguments
                    .FromFileInput(backgroundVideoPath, false,
                        opt => opt.WithCustomArgument("-stream_loop -1")) // Input 0: Visual Background video
                    .AddFileInput(overlayCardPath, false,
                        opt => {
                            opt.WithCustomArgument("-loop 1");          // Input 1: Overlay card image
                            opt.WithCustomArgument("-framerate 25");
                        });

                inputArguments.AddFileInput(ttsAudioPath); // Input 2: TTS Audio

                var filterComplexBuilder = new StringBuilder();
                // Video part of the filter graph (always present)
                filterComplexBuilder.Append(
                    $"[0:v]scale={finalVideoWidth}:{finalVideoHeight}:force_original_aspect_ratio=decrease,pad={finalVideoWidth}:{finalVideoHeight}:(ow-iw)/2:(oh-ih)/2,setsar=1[bg];" +
                    $"[1:v]scale='iw*min(1,min({finalVideoWidth}*0.9/iw,{finalVideoHeight}*0.8/ih))':-1,format=rgba[fg];" +
                    $"[bg][fg]overlay=(W-w)/2:(H-h)/2[vout]" // Video output stream labeled [vout]
                );

                string finalAudioMapLabel;

                bool useBackgroundMusic = !string.IsNullOrWhiteSpace(_videoOptions.BackgroundMusicFilePath) &&
                                          File.Exists(_videoOptions.BackgroundMusicFilePath) &&
                                          _videoOptions.BackgroundMusicVolume > 0.0;

                if (useBackgroundMusic)
                {
                    Console.WriteLine($"VideoService: Adding background music from '{_videoOptions.BackgroundMusicFilePath}' at volume {_videoOptions.BackgroundMusicVolume}");
                    inputArguments = inputArguments.AddFileInput(_videoOptions.BackgroundMusicFilePath!, false,
                        opt => opt.WithCustomArgument("-stream_loop -1")); // Input 3: Background Music (looped)

                    string musicVolume = _videoOptions.BackgroundMusicVolume.ToString(CultureInfo.InvariantCulture);
                    // For amix, dropout_transition should ideally be less than the shortest audio segment if it's very short.
                    // However, 'duration=first' makes the output as long as the TTS.
                    // A small dropout transition can help smooth the end if music is cut short.
                    double dropoutTransition = Math.Min(0.1, clipDuration.TotalSeconds / 10.0); // e.g. 10% of TTS duration, max 0.1s
                    dropoutTransition = Math.Max(0.01, dropoutTransition); // ensure it's a small positive value

                    // TTS is [2:a], Background Music is [3:a]
                    filterComplexBuilder.Append( // Append to existing video filter graph with a semicolon
                        $";[3:a]volume={musicVolume},aloop=loop=-1:size=2000000000[bgm_looped];" + // Loop bg music, large size for aloop
                        $"[2:a][bgm_looped]amix=inputs=2:duration=first:dropout_transition={dropoutTransition.ToString(CultureInfo.InvariantCulture)}[aout_final]"
                    );
                    finalAudioMapLabel = "[aout_final]"; // Use the mixed audio output
                }
                else
                {
                    // No music, explicitly pass through TTS audio [2:a] and label it for mapping
                    filterComplexBuilder.Append($";[2:a]anull[aout_final]");
                    finalAudioMapLabel = "[aout_final]";
                }

                var outputProcessor = inputArguments.OutputToFile(outputClipPath, true, options => {
                    options.WithVideoCodec(VideoCodec.LibX264)
                           .WithAudioCodec(AudioCodec.Aac)
                           .WithAudioBitrate(192)
                           .WithVideoBitrate(2500)
                           .WithCustomArgument($"-filter_complex \"{filterComplexBuilder.ToString()}\"")
                           .WithCustomArgument("-map [vout]")  // Map video output
                           .WithCustomArgument($"-map \"{finalAudioMapLabel}\"") // Map audio output (either mixed or passthrough TTS)
                           .WithDuration(clipDuration)
                           .WithCustomArgument("-preset medium") // Balanced speed and quality
                           .UsingMultithreading(true)
                           .WithFastStart();
                });

                bool success = await outputProcessor.ProcessAsynchronously();

                if (success)
                {
                    Console.WriteLine($"VideoService: Clip with background (music: {useBackgroundMusic}) successfully created: {outputClipPath}");
                    return true;
                }
                else
                {
                    Console.Error.WriteLine($"VideoService Error: FFmpeg processing failed for clip: {outputClipPath}.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"VideoService Error: An unexpected error occurred during clip creation. {ex.Message}");
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
                Console.WriteLine("VideoService: Transitions DISABLED. Using simple concat filter (re-encoding).");
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
                    // Calculate a safe transition duration once, based on all clips
                    double safeGlobalTransitionDuration = _videoOptions.TransitionDurationSeconds;
                    for (int i = 0; i < clipDurations.Count - 1; i++)
                    {
                        safeGlobalTransitionDuration = Math.Min(safeGlobalTransitionDuration, clipDurations[i].TotalSeconds / 2.01);
                        safeGlobalTransitionDuration = Math.Min(safeGlobalTransitionDuration, clipDurations[i + 1].TotalSeconds / 2.01);
                    }
                    safeGlobalTransitionDuration = Math.Max(0.01, safeGlobalTransitionDuration);

                    if (safeGlobalTransitionDuration <= 0.01 && videoClipPaths.Count > 1)
                    {
                        Console.WriteLine("VideoService Warning: Calculated transition duration too short or zero. Switching to simple concat for this operation.");
                        return await ConcatenateWithoutTransitions(arguments, videoClipPaths, outputVideoPath);
                    }
                    Console.WriteLine($"VideoService: Using effective transition duration: {safeGlobalTransitionDuration}s for xfade/acrossfade.");

                    for (int i = 0; i < videoClipPaths.Count - 1; i++)
                    {
                        string nextVideoInputLabel = $"[{i + 1}:v]";
                        string nextAudioInputLabel = $"[{i + 1}:a]";

                        string videoOutStageLabel = (i == videoClipPaths.Count - 2) ? "[vout]" : $"[vtemp{i}]";
                        string audioOutStageLabel = (i == videoClipPaths.Count - 2) ? "[aout]" : $"[atemp{i}]";

                        double xfadeOffset = accumulatedDurationBeforeTransitionPoint + (clipDurations[i].TotalSeconds - safeGlobalTransitionDuration);

                        filterComplexBuilder.Append($"{currentVideoLabel}{nextVideoInputLabel}" +
                                                    $"xfade=transition=fade" +
                                                    $":duration={safeGlobalTransitionDuration.ToString(CultureInfo.InvariantCulture)}" +
                                                    $":offset={xfadeOffset.ToString(CultureInfo.InvariantCulture)}{videoOutStageLabel};");

                        filterComplexBuilder.Append($"{currentAudioLabel}{nextAudioInputLabel}" +
                                                    $"acrossfade=d={safeGlobalTransitionDuration.ToString(CultureInfo.InvariantCulture)}{audioOutStageLabel};");

                        currentVideoLabel = videoOutStageLabel;
                        currentAudioLabel = audioOutStageLabel;

                        accumulatedDurationBeforeTransitionPoint += clipDurations[i].TotalSeconds - safeGlobalTransitionDuration;
                    }
                }
                else
                {
                    for (int i = 0; i < videoClipPaths.Count; i++)
                    {
                        filterComplexBuilder.Append($"[{i}:v:0][{i}:a:0]");
                    }
                    filterComplexBuilder.Append($"concat=n={videoClipPaths.Count}:v=1:a=1[vout][aout]");
                    currentVideoLabel = "[vout]";
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

        private async Task<bool> ConcatenateWithoutTransitions(FFMpegArguments arguments, List<string> videoClipPaths, string outputVideoPath)
        {
            Console.WriteLine("VideoService: Using simple concat filter (no transitions) as fallback.");
            var filterComplexBuilder = new StringBuilder();
            for (int i = 0; i < videoClipPaths.Count; i++)
            {
                filterComplexBuilder.Append($"[{i}:v:0][{i}:a:0]");
            }
            filterComplexBuilder.Append($"concat=n={videoClipPaths.Count}:v=1:a=1[vout][aout]");

            return await arguments
                .OutputToFile(outputVideoPath, true, options => options
                    .WithVideoCodec(VideoCodec.LibX264)
                    .WithAudioCodec(AudioCodec.Aac)
                    .WithAudioBitrate(192)
                    .WithVideoBitrate(2500)
                    .WithCustomArgument($"-filter_complex \"{filterComplexBuilder.ToString()}\"")
                    .WithCustomArgument("-map \"[vout]\"")
                    .WithCustomArgument("-map \"[aout]\"")
                    .WithCustomArgument("-preset medium")
                    .WithFastStart())
                .ProcessAsynchronously();
        }
    }
}

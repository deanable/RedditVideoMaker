// VideoService.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFMpegCore; // Main FFmpegCore library
using FFMpegCore.Enums; // For enums like VideoCodec, AudioCodec, Speed
using FFMpegCore.Arguments; // For specific argument classes if needed, though often fluent
using System.Linq; // For Any(), Take() etc.
using System.Text; // For StringBuilder
using System.Globalization; // For CultureInfo.InvariantCulture in ToString conversions for FFmpeg
using Microsoft.Extensions.Options; // For IOptions

namespace RedditVideoMaker.Core
{
    /// <summary>
    /// Provides services for video manipulation using FFmpeg.
    /// This includes creating video clips by overlaying images and audio on backgrounds,
    /// and concatenating multiple clips into a final video, with optional transitions.
    /// </summary>
    public class VideoService
    {
        private readonly VideoOptions _videoOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="VideoService"/> class.
        /// </summary>
        /// <param name="videoOptions">The video configuration options.</param>
        public VideoService(IOptions<VideoOptions> videoOptions)
        {
            _videoOptions = videoOptions.Value;
        }

        /// <summary>
        /// Creates a video clip by overlaying a card image and TTS audio onto a background video.
        /// Optionally mixes in background music.
        /// </summary>
        /// <param name="visualBackgroundVideoPath">Path to the background video file (e.g., gameplay footage).</param>
        /// <param name="overlayCardPath">Path to the image card (e.g., PNG with text) to overlay.</param>
        /// <param name="ttsAudioPath">Path to the TTS audio file corresponding to the overlay card content.</param>
        /// <param name="outputClipPath">Path where the generated video clip will be saved.</param>
        /// <param name="finalVideoWidth">The target width for the output clip.</param>
        /// <param name="finalVideoHeight">The target height for the output clip.</param>
        /// <returns>True if the clip was created successfully; false otherwise.</returns>
        public async Task<bool> CreateClipWithBackgroundAsync(
            string visualBackgroundVideoPath,
            string overlayCardPath,
            string ttsAudioPath,
            string outputClipPath,
            int finalVideoWidth,
            int finalVideoHeight)
        {
            // --- Input Validation ---
            if (string.IsNullOrWhiteSpace(visualBackgroundVideoPath) || !File.Exists(visualBackgroundVideoPath))
            {
                Console.Error.WriteLine($"VideoService Error: Visual background video not found or path invalid: {visualBackgroundVideoPath}");
                return false;
            }
            if (string.IsNullOrWhiteSpace(overlayCardPath) || !File.Exists(overlayCardPath))
            {
                Console.Error.WriteLine($"VideoService Error: Overlay card image not found or path invalid: {overlayCardPath}");
                return false;
            }
            if (string.IsNullOrWhiteSpace(ttsAudioPath) || !File.Exists(ttsAudioPath))
            {
                Console.Error.WriteLine($"VideoService Error: TTS audio not found or path invalid: {ttsAudioPath}");
                return false;
            }
            if (string.IsNullOrWhiteSpace(outputClipPath))
            {
                Console.Error.WriteLine("VideoService Error: Output clip path cannot be empty.");
                return false;
            }

            try
            {
                // Ensure output directory exists
                string? directory = Path.GetDirectoryName(outputClipPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Get the duration of the TTS audio, which determines the clip's length.
                IMediaAnalysis ttsAudioInfo = await FFProbe.AnalyseAsync(ttsAudioPath);
                TimeSpan clipDuration = ttsAudioInfo.Duration;
                if (clipDuration <= TimeSpan.Zero)
                {
                    Console.Error.WriteLine($"VideoService Error: Invalid or zero audio duration for {ttsAudioPath}. Cannot create clip.");
                    return false;
                }

                Console.WriteLine($"VideoService: Creating clip '{Path.GetFileName(outputClipPath)}'. Duration: {clipDuration}, Output Size: {finalVideoWidth}x{finalVideoHeight}");

                // Ensure video dimensions are even, as required by some codecs (like H.264 with YUV420p).
                if (finalVideoWidth % 2 != 0) finalVideoWidth++;
                if (finalVideoHeight % 2 != 0) finalVideoHeight++;

                // --- FFmpeg Argument Setup ---
                // Start with the main visual background video.
                // -stream_loop -1: Loops the background video indefinitely (or until shortest input ends).
                var inputArguments = FFMpegArguments
                    .FromFileInput(visualBackgroundVideoPath, false, // 'false' for verifyExists might be redundant if we already check
                        opt => opt.WithCustomArgument("-stream_loop -1"))
                    // Add the overlay card image.
                    // -loop 1: Loops the static image.
                    // -r: Set frame rate for the image input to match audio or a default (e.g., 25 fps).
                    .AddFileInput(overlayCardPath, false,
                        opt => {
                            opt.WithCustomArgument("-loop 1");
                            opt.WithCustomArgument($"-r {ttsAudioInfo.PrimaryVideoStream?.FrameRate ?? 25}"); // Use source audio's video stream FPS if available, or default
                        })
                    // Add the TTS audio input.
                    .AddFileInput(ttsAudioPath);

                var filterComplexBuilder = new StringBuilder();
                string finalVideoMapLabel = "[vout]"; // Label for the final video stream from filter_complex
                string finalAudioMapLabel; // Label for the final audio stream

                // --- Video Filter Graph ---
                // [0:v]: Video stream from the first input (background video).
                // scale=...: Scales the background to fit finalVideoWidth:finalVideoHeight while maintaining aspect ratio (decrease).
                // pad=...: Pads the scaled background to fill finalVideoWidth:finalVideoHeight, centering it.
                // setsar=1: Sets Sample Aspect Ratio to 1:1 (square pixels). Output is labeled [bg].
                filterComplexBuilder.Append(
                    $"[0:v]scale={finalVideoWidth}:{finalVideoHeight}:force_original_aspect_ratio=decrease,pad={finalVideoWidth}:{finalVideoHeight}:(ow-iw)/2:(oh-ih)/2,setsar=1[bg];"
                );
                // [1:v]: Video stream from the second input (overlay card image).
                // scale=...: Scales the overlay image to fit within 90% of video width or 80% of video height, maintaining aspect ratio.
                // format=rgba: Ensure the image has an alpha channel for potential transparency.
                // trim=duration=...: Trim the looped image stream to match the TTS audio duration. Output is labeled [fg].
                filterComplexBuilder.Append(
                    $"[1:v]scale='iw*min(1,min({finalVideoWidth}*0.9/iw,{finalVideoHeight}*0.8/ih))':-1:flags=lanczos,format=rgba,trim=duration={clipDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture)}[fg];"
                );
                // [bg][fg]overlay=...: Overlays the [fg] (card) onto the [bg] (background).
                // (W-w)/2:(H-h)/2: Centers the overlay.
                // shortest=1: Ensures the overlay duration doesn't exceed the background (which is looped but effectively trimmed by output duration).
                // The output of this overlay operation is labeled [vout].
                filterComplexBuilder.Append(
                    $"[bg][fg]overlay=(W-w)/2:(H-h)/2:shortest=1{finalVideoMapLabel}"
                );

                // --- Audio Processing ---
                // Resolve background music path
                string resolvedBackgroundMusicPath = string.Empty;
                if (!string.IsNullOrWhiteSpace(_videoOptions.BackgroundMusicFilePath))
                {
                    resolvedBackgroundMusicPath = Path.IsPathRooted(_videoOptions.BackgroundMusicFilePath)
                        ? _videoOptions.BackgroundMusicFilePath
                        : Path.Combine(AppContext.BaseDirectory, _videoOptions.AssetsRootDirectory, _videoOptions.BackgroundMusicFilePath);
                }

                bool useBackgroundMusic = !string.IsNullOrWhiteSpace(resolvedBackgroundMusicPath) &&
                                          File.Exists(resolvedBackgroundMusicPath) &&
                                          _videoOptions.BackgroundMusicVolume > 0.0;

                if (useBackgroundMusic)
                {
                    Console.WriteLine($"VideoService: Adding background music from '{resolvedBackgroundMusicPath}' at volume {_videoOptions.BackgroundMusicVolume}");
                    // Add background music as the 4th input (index 3).
                    // -stream_loop -1: Loop the music.
                    inputArguments = inputArguments.AddFileInput(resolvedBackgroundMusicPath, false,
                        opt => opt.WithCustomArgument("-stream_loop -1"));

                    // [3:a]: Audio stream from the fourth input (background music).
                    // volume=...: Adjusts the music volume.
                    // atrim=0:duration: Trims the music to the clip duration. Output is [bgm_trimmed].
                    filterComplexBuilder.Append(
                        $";[3:a]volume={_videoOptions.BackgroundMusicVolume.ToString(CultureInfo.InvariantCulture)},atrim=0:{clipDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture)}[bgm_trimmed];"
                    );
                    // [2:a]: Audio stream from the third input (TTS audio).
                    // [bgm_trimmed]: Trimmed and volume-adjusted background music.
                    // amix=inputs=2...: Mixes TTS audio and background music.
                    // duration=first: Duration of the mix is determined by the first input (TTS audio).
                    // dropout_transition: Time for volume change when one input ends.
                    // The mixed output is labeled [aout_final].
                    double dropoutTransition = Math.Min(0.1, clipDuration.TotalSeconds / 10.0); // Make transition relative but small
                    dropoutTransition = Math.Max(0.01, dropoutTransition); // Ensure a minimal transition
                    filterComplexBuilder.Append(
                        $"[2:a][bgm_trimmed]amix=inputs=2:duration=first:dropout_transition={dropoutTransition.ToString(CultureInfo.InvariantCulture)}[aout_final]"
                    );
                    finalAudioMapLabel = "[aout_final]";
                }
                else
                {
                    // If no background music, simply pass the TTS audio through.
                    // [2:a]: Audio stream from the third input (TTS audio).
                    // anull: No-operation audio filter, useful for labeling. Output is [aout_final].
                    filterComplexBuilder.Append($";[2:a]anull[aout_final]");
                    finalAudioMapLabel = "[aout_final]";
                }

                // --- Output Configuration ---
                var outputProcessor = inputArguments.OutputToFile(outputClipPath, true, options => { // 'true' to overwrite output
                    options.WithVideoCodec(VideoCodec.LibX264) // Standard H.264 video codec
                           .WithAudioCodec(AudioCodec.Aac)    // Standard AAC audio codec
                           .WithAudioBitrate(192)             // Decent audio bitrate (kbps)
                           .WithVideoBitrate(2500)            // Decent video bitrate (kbps) for general content
                           .WithCustomArgument($"-filter_complex \"{filterComplexBuilder.ToString()}\"") // Apply the constructed filter graph
                           .WithCustomArgument($"-map \"{finalVideoMapLabel}\"")  // Map the final video stream
                           .WithCustomArgument($"-map \"{finalAudioMapLabel}\"") // Map the final audio stream
                           .WithDuration(clipDuration)         // Set the output duration to match TTS audio
                           .WithCustomArgument("-preset medium") // FFmpeg preset for encoding speed/quality balance
                           .UsingMultithreading(true)          // Enable multithreading for encoding
                           .WithFastStart();                   // Optimizes for web streaming (moov atom at the beginning)
                });

                // Execute FFmpeg command.
                bool success = await outputProcessor.ProcessAsynchronously();

                if (success)
                {
                    Console.WriteLine($"VideoService: Clip with background (music: {useBackgroundMusic}) successfully created: {outputClipPath}");
                    return true;
                }
                else
                {
                    Console.Error.WriteLine($"VideoService Error: FFmpeg processing failed for clip: {outputClipPath}. Check FFmpeg logs if available.");
                    // The 'outputProcessor.ErrorData' or 'outputProcessor.OutputData' might contain FFmpeg's stderr/stdout.
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"VideoService Error: An unexpected error occurred during clip creation for '{outputClipPath}'. {ex.Message}");
                Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Concatenates a list of video clips into a single output video.
        /// Supports optional transitions between clips.
        /// </summary>
        /// <param name="videoClipPaths">A list of paths to the video clips to be concatenated.</param>
        /// <param name="outputVideoPath">The path where the final concatenated video will be saved.</param>
        /// <returns>True if concatenation was successful; false otherwise.</returns>
        public async Task<bool> ConcatenateVideosAsync(List<string> videoClipPaths, string outputVideoPath)
        {
            // --- Input Validation ---
            if (videoClipPaths == null || !videoClipPaths.Any())
            {
                Console.Error.WriteLine("VideoService Error: No video clips provided for concatenation.");
                return false;
            }
            foreach (var clipPath in videoClipPaths)
            {
                if (!File.Exists(clipPath))
                {
                    Console.Error.WriteLine($"VideoService Error: Input clip for concatenation not found: {clipPath}");
                    return false;
                }
            }
            if (string.IsNullOrWhiteSpace(outputVideoPath))
            {
                Console.Error.WriteLine("VideoService Error: Output path for concatenated video is empty.");
                return false;
            }

            // Handle single clip case: just copy it.
            if (videoClipPaths.Count == 1)
            {
                try
                {
                    File.Copy(videoClipPaths[0], outputVideoPath, true); // Overwrite if exists
                    Console.WriteLine($"VideoService: Single clip. Copied '{videoClipPaths[0]}' to '{outputVideoPath}'.");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"VideoService Error: Failed to copy single clip '{videoClipPaths[0]}' to '{outputVideoPath}'. {ex.Message}");
                    return false;
                }
            }

            Console.WriteLine($"VideoService: Starting concatenation of {videoClipPaths.Count} clips into '{Path.GetFileName(outputVideoPath)}'.");

            if (_videoOptions.EnableTransitions)
            {
                Console.WriteLine($"VideoService: Transitions ENABLED. Target duration: {_videoOptions.TransitionDurationSeconds}s.");
            }
            else
            {
                Console.WriteLine("VideoService: Transitions DISABLED. Using simple concat filter (re-encoding).");
            }

            try
            {
                FFMpegArguments arguments;
                var filterComplexBuilder = new StringBuilder();
                string finalOutputVideoLabel = "[vout]"; // Label for final concatenated video
                string finalOutputAudioLabel = "[aout]"; // Label for final concatenated audio

                // Add all input files to FFMpegArguments
                arguments = FFMpegArguments.FromFileInput(videoClipPaths[0]);
                for (int i = 1; i < videoClipPaths.Count; i++)
                {
                    arguments.AddFileInput(videoClipPaths[i]);
                }

                // --- Transition Logic (xfade/acrossfade) ---
                if (_videoOptions.EnableTransitions && videoClipPaths.Count > 1)
                {
                    var clipDurations = new List<TimeSpan>();
                    for (int i = 0; i < videoClipPaths.Count; i++)
                    {
                        var mediaInfo = await FFProbe.AnalyseAsync(videoClipPaths[i]);
                        if (mediaInfo.Duration <= TimeSpan.Zero)
                        {
                            Console.Error.WriteLine($"VideoService Error: Could not get valid duration for clip '{videoClipPaths[i]}' for transition calculation. Falling back to simple concat.");
                            return await ConcatenateWithoutTransitions(videoClipPaths, outputVideoPath);
                        }
                        clipDurations.Add(mediaInfo.Duration);
                    }

                    // Calculate effective transition duration, ensuring it's not longer than ~49.9% of the shorter of the two clips in a pair.
                    double effectiveTransitionDuration = _videoOptions.TransitionDurationSeconds;
                    for (int i = 0; i < clipDurations.Count - 1; i++)
                    {
                        double maxAllowedForPair = Math.Min(clipDurations[i].TotalSeconds * 0.499, clipDurations[i + 1].TotalSeconds * 0.499);
                        effectiveTransitionDuration = Math.Min(effectiveTransitionDuration, maxAllowedForPair);
                    }
                    effectiveTransitionDuration = Math.Max(0.01, effectiveTransitionDuration); // Ensure a minimal positive duration.

                    if (effectiveTransitionDuration < 0.02) // If transition is too short, it might not be worth it or cause issues.
                    {
                        Console.WriteLine($"VideoService Warning: Effective transition duration ({effectiveTransitionDuration:F3}s) is very short. Falling back to simple concat.");
                        return await ConcatenateWithoutTransitions(videoClipPaths, outputVideoPath);
                    }
                    Console.WriteLine($"VideoService: Using effective transition duration: {effectiveTransitionDuration:F3}s for xfade/acrossfade.");

                    string currentVideo = "[0:v]"; // Video stream of the first clip
                    string currentAudio = "[0:a]"; // Audio stream of the first clip

                    for (int i = 0; i < videoClipPaths.Count - 1; i++)
                    {
                        string nextVideo = $"[{i + 1}:v]"; // Video stream of the next clip
                        string nextAudio = $"[{i + 1}:a]"; // Audio stream of the next clip

                        // Determine output labels for this transition step
                        string tempVideoOut = (i == videoClipPaths.Count - 2) ? finalOutputVideoLabel : $"[v{i + 1}]"; // Final label for last transition
                        string tempAudioOut = (i == videoClipPaths.Count - 2) ? finalOutputAudioLabel : $"[a{i + 1}]";

                        // xfade: transition video streams. 'offset' is when the second video starts fading in,
                        // relative to the beginning of the *first* video in the xfade pair.
                        // Duration is the length of the fade itself.
                        double xfadeStartOffsetInCurrentClip = clipDurations[i].TotalSeconds - effectiveTransitionDuration;

                        filterComplexBuilder.Append($"{currentVideo}{nextVideo}xfade=transition=fade:duration={effectiveTransitionDuration.ToString(CultureInfo.InvariantCulture)}:offset={xfadeStartOffsetInCurrentClip.ToString(CultureInfo.InvariantCulture)}{tempVideoOut};");
                        // acrossfade: transition audio streams. 'd' is duration.
                        filterComplexBuilder.Append($"{currentAudio}{nextAudio}acrossfade=d={effectiveTransitionDuration.ToString(CultureInfo.InvariantCulture)}{tempAudioOut};");

                        // The output of this transition becomes the input for the next one.
                        currentVideo = tempVideoOut;
                        currentAudio = tempAudioOut;
                    }
                }
                // --- Simple Concatenation Logic ---
                else
                {
                    // Build filter for simple concatenation using the 'concat' filter.
                    // Maps video (v:0) and audio (a:0) streams from each input.
                    for (int i = 0; i < videoClipPaths.Count; i++)
                    {
                        filterComplexBuilder.Append($"[{i}:v:0][{i}:a:0]");
                    }
                    filterComplexBuilder.Append($"concat=n={videoClipPaths.Count}:v=1:a=1{finalOutputVideoLabel}{finalOutputAudioLabel}");
                }

                // Remove trailing semicolon if any
                if (filterComplexBuilder.Length > 0 && filterComplexBuilder[filterComplexBuilder.Length - 1] == ';')
                {
                    filterComplexBuilder.Length--;
                }

                // Execute FFmpeg command for concatenation.
                bool success = await arguments
                    .OutputToFile(outputVideoPath, true, options => options
                        .WithVideoCodec(VideoCodec.LibX264)
                        .WithAudioCodec(AudioCodec.Aac)
                        .WithAudioBitrate(192)
                        .WithVideoBitrate(2500) // Consistent bitrate with clips
                        .WithCustomArgument($"-filter_complex \"{filterComplexBuilder.ToString()}\"")
                        .WithCustomArgument($"-map \"{finalOutputVideoLabel}\"")
                        .WithCustomArgument($"-map \"{finalOutputAudioLabel}\"")
                        .WithCustomArgument("-preset medium")
                        .WithFastStart())
                    .ProcessAsynchronously();

                if (success)
                {
                    Console.WriteLine($"VideoService: Concatenation successful: {outputVideoPath}");
                    return true;
                }
                else
                {
                    Console.Error.WriteLine($"VideoService Error: FFmpeg concatenation failed for {outputVideoPath}.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"VideoService Error: Unexpected error during concatenation of clips for '{outputVideoPath}'. {ex.Message} {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Private helper method to concatenate videos without transitions using the simple 'concat' filter.
        /// This is used as a fallback or when transitions are disabled.
        /// </summary>
        /// <param name="videoClipPaths">List of video clip paths.</param>
        /// <param name="outputVideoPath">Output path for the concatenated video.</param>
        /// <returns>True if successful, false otherwise.</returns>
        private async Task<bool> ConcatenateWithoutTransitions(List<string> videoClipPaths, string outputVideoPath)
        {
            Console.WriteLine("VideoService: Using simple concat filter (no transitions) as fallback/default for re-encoding.");
            // Add all input files
            FFMpegArguments arguments = FFMpegArguments.FromFileInput(videoClipPaths[0]);
            for (int i = 1; i < videoClipPaths.Count; i++)
            {
                arguments.AddFileInput(videoClipPaths[i]);
            }

            var sb = new StringBuilder();
            // Prepare input stream specifiers for concat filter: e.g., [0:v:0][0:a:0][1:v:0][1:a:0]...
            for (int i = 0; i < videoClipPaths.Count; i++)
            {
                sb.Append($"[{i}:v:0][{i}:a:0]");
            }
            // Append concat filter: n=number of segments, v=1 (video output), a=1 (audio output)
            // Label outputs as [vout] and [aout]
            sb.Append($"concat=n={videoClipPaths.Count}:v=1:a=1[vout][aout]");

            return await arguments
                .OutputToFile(outputVideoPath, true, options => options
                    .WithVideoCodec(VideoCodec.LibX264)
                    .WithAudioCodec(AudioCodec.Aac)
                    .WithAudioBitrate(192)
                    .WithVideoBitrate(2500)
                    .WithCustomArgument($"-filter_complex \"{sb.ToString()}\"")
                    .WithCustomArgument("-map \"[vout]\"") // Map labeled video output
                    .WithCustomArgument("-map \"[aout]\"") // Map labeled audio output
                    .WithCustomArgument("-preset medium")
                    .WithFastStart())
                .ProcessAsynchronously();
        }
    }
}
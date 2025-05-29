// VideoOptions.cs (in RedditVideoMaker.Core project)
// Removed: using System.Collections.Generic; // This using statement was not needed for this class.

namespace RedditVideoMaker.Core
{
    /// <summary>
    /// Holds settings related to video generation, including resolution, styling,
    /// content inclusion, asset paths, and transitions.
    /// </summary>
    public class VideoOptions
    {
        /// <summary>
        /// Defines the section name in the configuration file (e.g., appsettings.json)
        /// from which these options will be loaded.
        /// </summary>
        public const string SectionName = "VideoOptions";

        /// <summary>
        /// Gets or sets the output resolution for the final video (e.g., "1080x1920" for portrait HD, "1920x1080" for landscape HD).
        /// Default is "1080x1920".
        /// </summary>
        public string OutputResolution { get; set; } = "1080x1920";

        /// <summary>
        /// Gets or sets the theme for the video, which might influence default color schemes or styles.
        /// (Currently, this property might be for future use or interpreted by specific logic not shown).
        /// Default is "dark".
        /// </summary>
        public string Theme { get; set; } = "dark";

        /// <summary>
        /// Gets or sets the path to the background video file.
        /// Can be an absolute path or a path relative to the <see cref="AssetsRootDirectory"/>.
        /// This video is often looped or tiled behind the content cards.
        /// Default is null.
        /// </summary>
        public string? BackgroundVideoPath { get; set; }

        /// <summary>
        /// Gets or sets the width of the content cards (holding text like title, comments) in pixels.
        /// Default is 800.
        /// </summary>
        public int CardWidth { get; set; } = 800;

        /// <summary>
        /// Gets or sets the height of the content cards (holding text like title, comments) in pixels.
        /// Default is 600.
        /// </summary>
        public int CardHeight { get; set; } = 600;

        /// <summary>
        /// Gets or sets the background color for the content cards.
        /// Can be a named color (e.g., "DarkSlateGray") or a hex code (e.g., "#2F4F4F").
        /// Default is "DarkSlateGray".
        /// </summary>
        public string CardBackgroundColor { get; set; } = "DarkSlateGray";

        /// <summary>
        /// Gets or sets the font color for the main text on content cards.
        /// Can be a named color (e.g., "White") or a hex code (e.g., "#FFFFFF").
        /// Default is "White".
        /// </summary>
        public string CardFontColor { get; set; } = "White";

        /// <summary>
        /// Gets or sets the font color for metadata text (like author, score) on content cards.
        /// Can be a named color (e.g., "LightGray") or a hex code (e.g., "#D3D3D3").
        /// Default is "LightGray".
        /// </summary>
        public string CardMetadataFontColor { get; set; } = "LightGray";

        /// <summary>
        /// Gets or sets the maximum number of comments to include in each generated video.
        /// Default is 3.
        /// </summary>
        public int NumberOfCommentsToInclude { get; set; } = 3;

        /// <summary>
        /// Gets or sets a value indicating whether to enable transitions (e.g., crossfades) between video clips.
        /// Default is true.
        /// </summary>
        public bool EnableTransitions { get; set; } = true;

        /// <summary>
        /// Gets or sets the duration of transitions between video clips, in seconds.
        /// Relevant only if <see cref="EnableTransitions"/> is true.
        /// Default is 0.5 seconds.
        /// </summary>
        public double TransitionDurationSeconds { get; set; } = 0.5;

        /// <summary>
        /// Gets or sets the target font size (in points) for main content text (titles, self-text, comment bodies).
        /// The actual size might be adjusted down to fit text within card boundaries, but not below <see cref="ContentMinFontSize"/>.
        /// Default is 36f.
        /// </summary>
        public float ContentTargetFontSize { get; set; } = 36f;

        /// <summary>
        /// Gets or sets the minimum font size (in points) for main content text.
        /// Text will not be rendered smaller than this size, even if it overflows.
        /// Default is 16f.
        /// </summary>
        public float ContentMinFontSize { get; set; } = 16f;

        /// <summary>
        /// Gets or sets the maximum font size (in points) for main content text.
        /// Text will not be rendered larger than this, even if there's ample space (unless it's also above <see cref="ContentTargetFontSize"/>).
        /// Default is 60f.
        /// </summary>
        public float ContentMaxFontSize { get; set; } = 60f;

        /// <summary>
        /// Gets or sets the target font size (in points) for metadata text (author, score).
        /// Adjusted similarly to <see cref="ContentTargetFontSize"/>.
        /// Default is 24f.
        /// </summary>
        public float MetadataTargetFontSize { get; set; } = 24f;

        /// <summary>
        /// Gets or sets the minimum font size (in points) for metadata text.
        /// Default is 12f.
        /// </summary>
        public float MetadataMinFontSize { get; set; } = 12f;

        /// <summary>
        /// Gets or sets the maximum font size (in points) for metadata text.
        /// Default is 32f.
        /// </summary>
        public float MetadataMaxFontSize { get; set; } = 32f;

        /// <summary>
        /// Gets or sets a value indicating whether to clean up (delete) intermediate files
        /// (like individual TTS audio files, image cards, and short video clips) after the final video is created.
        /// Default is true.
        /// </summary>
        public bool CleanUpIntermediateFiles { get; set; } = true;

        /// <summary>
        /// Gets or sets the path to an optional intro video file.
        /// If specified and the file exists, it will be prepended to the final video.
        /// Can be an absolute path or a path relative to the <see cref="AssetsRootDirectory"/>.
        /// Default is null.
        /// </summary>
        public string? IntroVideoPath { get; set; }

        /// <summary>
        /// Gets or sets the path to an optional outro video file.
        /// If specified and the file exists, it will be appended to the final video.
        /// Can be an absolute path or a path relative to the <see cref="AssetsRootDirectory"/>.
        /// Default is null.
        /// </summary>
        public string? OutroVideoPath { get; set; }

        /// <summary>
        /// Gets or sets the path to an optional background music file.
        /// If specified and the file exists, it will be mixed into the audio of the generated clips.
        /// Can be an absolute path or a path relative to the <see cref="AssetsRootDirectory"/>.
        /// Default is null.
        /// </summary>
        public string? BackgroundMusicFilePath { get; set; }

        /// <summary>
        /// Gets or sets the volume for the background music, typically ranging from 0.0 (silent) to 1.0 (full volume).
        /// Relevant only if <see cref="BackgroundMusicFilePath"/> is valid and points to an existing file.
        /// Default is 0.15 (15% volume).
        /// </summary>
        public double BackgroundMusicVolume { get; set; } = 0.15;

        /// <summary>
        /// Gets or sets the path to the primary font file (e.g., a .ttf or .otf file).
        /// This font will be used for rendering text on cards if available.
        /// Can be an absolute path or a path relative to the application's execution directory or <see cref="AssetsRootDirectory"/> (depending on implementation in ImageService).
        /// It's recommended to place fonts in the <see cref="AssetsRootDirectory"/> and use a relative path like "Fonts/MyFont.ttf".
        /// Default is "Fonts/DejaVuSans.ttf".
        /// </summary>
        public string PrimaryFontFilePath { get; set; } = "Fonts/DejaVuSans.ttf"; // [cite: 427]

        /// <summary>
        /// Gets or sets the name of a fallback font to use if the primary font cannot be loaded or found.
        /// This should be a font name that is likely to be installed on the system (e.g., "Arial", "Verdana").
        /// Default is "Arial".
        /// </summary>
        public string FallbackFontName { get; set; } = "Arial"; // [cite: 427]

        /// <summary>
        /// Gets or sets the root directory name for assets (e.g., "assets"), relative to the application execution path. [cite: 428]
        /// Paths for <see cref="BackgroundVideoPath"/>, <see cref="BackgroundMusicFilePath"/>,
        /// <see cref="IntroVideoPath"/>, <see cref="OutroVideoPath"/>, and potentially <see cref="PrimaryFontFilePath"/>
        /// will be treated as relative to this directory if they are not absolute paths. [cite: 429, 430]
        /// Default is "assets".
        /// </summary>
        public string AssetsRootDirectory { get; set; } = "assets"; // [cite: 430]
    }
}
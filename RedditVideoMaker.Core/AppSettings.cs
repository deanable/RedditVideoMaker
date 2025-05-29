// AppSettings.cs (in RedditVideoMaker.Core project)
namespace RedditVideoMaker.Core
{
    /// <summary>
    /// Acts as a potential top-level container for all application settings classes.
    /// This class can be used to group various specific options (like Reddit, Video, TTS settings)
    /// under a single configuration object. This approach can simplify dependency injection
    /// if you prefer to inject a single IOptions&lt;AppSettings&gt; instance.
    ///
    /// Note: Currently, the Program.cs setup appears to bind individual options classes
    /// (e.g., GeneralOptions, RedditOptions) directly from their respective configuration sections.
    /// To use AppSettings as a central hub, Program.cs would need to be adjusted to bind
    /// this class, and appsettings.json might need a corresponding structure if this class
    /// itself is bound from a specific section or the root.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Gets or sets the Reddit-specific application settings.
        /// Initialized to a new instance by default to ensure it's not null when accessed.
        /// </summary>
        public RedditOptions RedditOptions { get; set; } = new RedditOptions();

        /// <summary>
        /// Gets or sets the video generation-specific application settings.
        /// Initialized to a new instance by default to ensure it's not null when accessed.
        /// </summary>
        public VideoOptions VideoOptions { get; set; } = new VideoOptions();

        // To make this a more complete container, you could add other options classes here:
        // public GeneralOptions GeneralOptions { get; set; } = new GeneralOptions();
        // public TtsOptions TtsOptions { get; set; } = new TtsOptions();
        // public YouTubeOptions YouTubeOptions { get; set; } = new YouTubeOptions();
        //
        // If you add these, ensure your appsettings.json structure allows them to be bound
        // correctly, either as direct children of a root "AppSettings" section or individually
        // if this class is populated manually.
    }
}
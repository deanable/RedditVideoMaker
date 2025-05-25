// AppSettings.cs (Optional: if you want to group them - in RedditVideoMaker.Core project)
namespace RedditVideoMaker.Core
{
    // This class can act as a container for all your option classes.
    public class AppSettings
    {
        // Ensure these properties are initialized if you bind directly to AppSettings
        public RedditOptions RedditOptions { get; set; } = new RedditOptions();
        public VideoOptions VideoOptions { get; set; } = new VideoOptions();
        // You can add more options classes here as your project grows.
    }
}
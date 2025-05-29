// GeneralOptions.cs (in RedditVideoMaker.Core project)
// Removed: using System.Collections.Generic; // This using statement was not needed for this file.

namespace RedditVideoMaker.Core
{
    /// <summary>
    /// Defines the levels of verbosity for console output.
    /// </summary>
    public enum ConsoleLogLevel
    {
        /// <summary>
        /// All Console.Write/WriteLine and Console.Error messages are written to the console.
        /// </summary>
        Detailed,

        /// <summary>
        /// Only key summary messages and all Console.Error messages are written to the console.
        /// Standard Console.Write/WriteLine messages are suppressed unless they are errors.
        /// </summary>
        Summary,

        /// <summary>
        /// Only Console.Error messages are written to the console.
        /// All standard Console.Write/WriteLine messages are suppressed.
        /// </summary>
        ErrorsOnly,

        /// <summary>
        /// Nothing is written to the console.
        /// All log output (including errors) will still go to the log file if file logging is enabled.
        /// </summary>
        Quiet
    }

    /// <summary>
    /// Holds general application settings, typically configured in appsettings.json.
    /// </summary>
    public class GeneralOptions
    {
        /// <summary>
        /// Defines the section name in the configuration file (e.g., appsettings.json)
        /// from which these options will be loaded.
        /// </summary>
        public const string SectionName = "GeneralOptions";

        /// <summary>
        /// Gets or sets a value indicating whether the application is running in a testing/debug module.
        /// This can be used to alter behavior, such as skipping YouTube uploads or using a fallback TTS engine.
        /// Default is false.
        /// </summary>
        public bool IsInTestingModule { get; set; } = false;

        /// <summary>
        /// Gets or sets the directory where log files will be stored.
        /// This can be a relative or absolute path. If relative, it's typically based on the application's execution directory.
        /// Default is "logs".
        /// </summary>
        public string LogFileDirectory { get; set; } = "logs";

        /// <summary>
        /// Gets or sets the number of days for which log files should be retained.
        /// Older log files will be automatically deleted during application startup.
        /// Default is 7 days.
        /// </summary>
        public int LogFileRetentionDays { get; set; } = 7;

        /// <summary>
        /// Gets or sets the desired level of verbosity for output to the actual console window.
        /// Note: All messages, regardless of this setting, are typically written to the log file if file logging is active.
        /// Default is <see cref="ConsoleLogLevel.Detailed"/>.
        /// </summary>
        public ConsoleLogLevel ConsoleOutputLevel { get; set; } = ConsoleLogLevel.Detailed; 
    }
}
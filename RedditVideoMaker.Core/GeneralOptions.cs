// GeneralOptions.cs (in RedditVideoMaker.Core project)
using System.Collections.Generic;

namespace RedditVideoMaker.Core
{
    public enum ConsoleLogLevel
    {
        Detailed, // All Console.Write/WriteLine and Console.Error goes to console
        Summary,  // Key summary messages and all errors go to console
        ErrorsOnly, // Only Console.Error goes to console
        Quiet     // Nothing goes to console (everything still logged to file)
    }

    public class GeneralOptions
    {
        public const string SectionName = "GeneralOptions";

        public bool IsInTestingModule { get; set; } = false;

        public string LogFileDirectory { get; set; } = "logs";
        public int LogFileRetentionDays { get; set; } = 7;

        // New property for Step 27.2: Console Verbosity
        /// <summary>
        /// Controls the verbosity of output to the actual console window.
        /// All output is still written to the log file.
        /// Options: Detailed, Summary, ErrorsOnly, Quiet.
        /// </summary>
        public ConsoleLogLevel ConsoleOutputLevel { get; set; } = ConsoleLogLevel.Detailed; // Default to detailed
    }
}

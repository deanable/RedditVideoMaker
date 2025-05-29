// FileLogger.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
using System.Linq; // Used for Directory.GetFiles() and potentially other array/IEnumerable operations.
using System.Text;
using System.Globalization;

namespace RedditVideoMaker.Core
{
    /// <summary>
    /// Provides static methods for file-based logging and console output redirection.
    /// Note: This is a static logger. For more complex applications or for easier testing,
    /// an instance-based logger obtained via Dependency Injection is often preferred.
    /// However, for a console application, a static logger can be convenient.
    /// </summary>
    public static class FileLogger
    {
        private static string _logDirectory = "logs"; // Default log directory name
        private static string _logFileNameFormat = "app_run_{0:yyyy-MM-dd}.log"; // Default log file name format
        private static StreamWriter? _logStreamWriter; // Writes to the log file
        private static TextWriter? _originalConsoleOut; // Stores the original Console.Out stream
        private static TextWriter? _originalConsoleError; // Stores the original Console.Error stream
        private static bool _isInitializedAndLoggingToFile = false; // Flag to track initialization status
        private static ConsoleLogLevel _consoleLogLevel = ConsoleLogLevel.Detailed; // Default console verbosity

        /// <summary>
        /// A custom TextWriter that duplicates write operations to two underlying writers:
        /// one for the console and one for the log file.
        /// Console output can be filtered based on the global FileLogger._consoleLogLevel.
        /// </summary>
        private class DualWriter : TextWriter
        {
            private readonly TextWriter? _consoleStream; // The original console stream (Out or Error)
            private readonly bool _isErrorStreamDualWriter; // True if this writer handles Console.Error, false for Console.Out

            /// <summary>
            /// Initializes a new instance of the <see cref="DualWriter"/> class.
            /// </summary>
            /// <param name="fileStreamWriter">The <see cref="StreamWriter"/> for the log file. Shared from FileLogger.</param>
            /// <param name="consoleStream">The original console stream (e.g., Console.Out or Console.Error) to also write to.</param>
            /// <param name="isErrorStream">Indicates if this DualWriter instance is for the error stream (Console.Error).</param>
            public DualWriter(StreamWriter? fileStreamWriter, TextWriter? consoleStream, bool isErrorStream)
            {
                // _fileStreamWriterInternal is not used directly here anymore for writing,
                // as WriteInternal now accesses FileLogger._logStreamWriter.
                // This constructor mainly needs the consoleStream and isErrorStream.
                _consoleStream = consoleStream;
                _isErrorStreamDualWriter = isErrorStream;
            }

            /// <summary>
            /// Gets the encoding of the console stream or, if unavailable, the file stream, or system default.
            /// </summary>
            public override Encoding Encoding => _consoleStream?.Encoding ?? FileLogger._logStreamWriter?.Encoding ?? Encoding.Default;

            /// <summary>
            /// Determines if the current message should be written to the actual console
            /// based on the global <see cref="FileLogger._consoleLogLevel"/>.
            /// </summary>
            private bool ShouldWriteToConsole()
            {
                if (_consoleStream == null) return false; // No console stream to write to.

                switch (FileLogger._consoleLogLevel)
                {
                    case ConsoleLogLevel.Detailed:
                        return true; // Write all messages to console.
                    case ConsoleLogLevel.Summary:
                        // Write only if it's an error stream message.
                        // Key summary messages would need to be explicitly logged to Console.Error or handled differently if desired for Console.Out.
                        return _isErrorStreamDualWriter;
                    case ConsoleLogLevel.ErrorsOnly:
                        return _isErrorStreamDualWriter; // Write only if it's an error stream message.
                    case ConsoleLogLevel.Quiet:
                        return false; // Write nothing to console.
                    default:
                        return true; // Default to detailed if an unknown level is somehow set.
                }
            }

            // Override Write methods to perform dual writing.
            public override void Write(char value) => WriteInternal(value.ToString(), false);
            public override void Write(string? value) => WriteInternal(value, false);
            public override void WriteLine(string? value) => WriteInternal(value, true);

            /// <summary>
            /// Internal method to handle writing to both file and console.
            /// Note: If this application becomes highly multi-threaded with many concurrent Console.Write operations,
            /// the write to FileLogger._logStreamWriter might need external locking, as StreamWriter itself is not
            /// inherently thread-safe for concurrent writes from multiple threads. Standard Console.Out/Error are thread-safe.
            /// </summary>
            private void WriteInternal(string? value, bool isNewLine)
            {
                // --- Attempt to write to log file ---
                if (FileLogger._logStreamWriter != null && FileLogger._isInitializedAndLoggingToFile)
                {
                    try
                    {
                        // Prepend timestamp to messages written to the file.
                        string timedValue = $"[{DateTime.UtcNow:HH:mm:ss.fff UTC}] {value}";
                        if (isNewLine)
                        {
                            FileLogger._logStreamWriter.WriteLine(timedValue);
                        }
                        else
                        {
                            FileLogger._logStreamWriter.Write(timedValue);
                        }
                        // FileLogger._logStreamWriter has AutoFlush = true, so no explicit flush needed here per write.
                    }
                    catch (ObjectDisposedException)
                    {
                        // The log file stream was disposed, possibly due to an error or shutdown.
                        // Stop further attempts to log to the file.
                        FileLogger._isInitializedAndLoggingToFile = false;
                        FileLogger._originalConsoleError?.WriteLine($"DualWriter: Attempted to write to a disposed file log. Subsequent file logging is disabled. Message: {value}");
                    }
                    catch (Exception ex)
                    {
                        // Other error writing to file; disable further file logging to prevent repeated errors.
                        FileLogger._originalConsoleError?.WriteLine($"DualWriter Error (writing to file): {ex.Message}. Message: {value}. Subsequent file logging is disabled.");
                        FileLogger._isInitializedAndLoggingToFile = false;
                    }
                }

                // --- Attempt to write to console ---
                if (ShouldWriteToConsole() && _consoleStream != null)
                {
                    try
                    {
                        if (isNewLine)
                        {
                            _consoleStream.WriteLine(value);
                        }
                        else
                        {
                            _consoleStream.Write(value);
                        }
                    }
                    catch (Exception ex)
                    {
                        // If writing to original console fails, report to System.Console.Error to avoid recursion.
                        System.Console.Error.WriteLine($"DualWriter Error (writing to console): {ex.Message}");
                    }
                }
            }

            public override void Flush()
            {
                try { FileLogger._logStreamWriter?.Flush(); } catch { /* Ignore errors during flush */ }
                try { _consoleStream?.Flush(); } catch { /* Ignore errors during flush */ }
            }

            /// <summary>
            /// Disposes the DualWriter. This does NOT dispose the shared FileLogger._logStreamWriter,
            /// as its lifecycle is managed by FileLogger itself.
            /// </summary>
            protected override void Dispose(bool disposing)
            {
                // This DualWriter instance does not own the FileLogger._logStreamWriter.
                // FileLogger.Dispose() is responsible for managing _logStreamWriter.
                // We only need to ensure base TextWriter resources are handled if any.
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Initializes the file logger. Sets up log directory, log file, and redirects console output.
        /// This should be called once at application startup.
        /// </summary>
        /// <param name="logDirectory">The directory to store log files.</param>
        /// <param name="consoleLevel">The <see cref="ConsoleLogLevel"/> determining console output verbosity.</param>
        /// <param name="logFileNameFormat">The format string for log file names (e.g., "app_run_{0:yyyy-MM-dd}.log").</param>
        public static void Initialize(string logDirectory, ConsoleLogLevel consoleLevel, string logFileNameFormat = "app_run_{0:yyyy-MM-dd}.log")
        {
            // Store original console streams before redirection.
            _originalConsoleOut = Console.Out;
            _originalConsoleError = Console.Error;

            if (_isInitializedAndLoggingToFile)
            {
                _originalConsoleError.WriteLine("FileLogger Initialize Warning: Already initialized.");
                return;
            }

            _logDirectory = logDirectory;
            _logFileNameFormat = logFileNameFormat; // Use provided format
            _consoleLogLevel = consoleLevel;
            string logFilePath = ""; // To store the full path for error reporting

            try
            {
                // Validate and resolve log directory path.
                if (string.IsNullOrWhiteSpace(_logDirectory))
                {
                    _logDirectory = Path.Combine(AppContext.BaseDirectory, "logs_default");
                    _originalConsoleError.WriteLine($"FileLogger Init Warning: Log directory was null or empty, using default: {_logDirectory}");
                }
                else if (!Path.IsPathRooted(_logDirectory))
                {
                    _logDirectory = Path.Combine(AppContext.BaseDirectory, _logDirectory);
                }
                _logDirectory = Path.GetFullPath(_logDirectory); // Ensure it's an absolute path.

                _originalConsoleOut.WriteLine($"FileLogger: Initializing. Target log directory: {_logDirectory}");

                // Create log directory if it doesn't exist.
                if (!Directory.Exists(_logDirectory))
                {
                    _originalConsoleOut.WriteLine($"FileLogger: Creating log directory: {_logDirectory}");
                    Directory.CreateDirectory(_logDirectory);
                }

                // Construct log file path.
                logFilePath = Path.Combine(_logDirectory, string.Format(CultureInfo.InvariantCulture, _logFileNameFormat, DateTime.UtcNow));
                _originalConsoleOut.WriteLine($"FileLogger: Attempting to create/append to log file: {logFilePath}");

                // Setup FileStream and StreamWriter for the log file.
                // FileMode.Append: Opens the file if it exists and seeks to the end, or creates a new file.
                // FileAccess.Write: Opens the file with write access.
                // FileShare.Read: Allows other processes to read the file concurrently.
                var fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                _logStreamWriter = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = true }; // AutoFlush ensures data is written promptly.

                // Determine which original console streams to pass to DualWriter based on consoleLevel.
                TextWriter? actualConsoleOutTarget = null;
                TextWriter? actualConsoleErrorTarget = null;

                switch (_consoleLogLevel)
                {
                    case ConsoleLogLevel.Detailed:
                        actualConsoleOutTarget = _originalConsoleOut;
                        actualConsoleErrorTarget = _originalConsoleError;
                        break;
                    case ConsoleLogLevel.Summary:
                        actualConsoleOutTarget = null; // Suppress standard output, allow errors.
                        actualConsoleErrorTarget = _originalConsoleError;
                        break;
                    case ConsoleLogLevel.ErrorsOnly:
                        actualConsoleOutTarget = null; // Suppress standard output.
                        actualConsoleErrorTarget = _originalConsoleError; // Only errors to console.
                        break;
                    case ConsoleLogLevel.Quiet:
                        actualConsoleOutTarget = null; // Suppress all console output.
                        actualConsoleErrorTarget = null;
                        break;
                }

                // Redirect Console.Out and Console.Error to our DualWriter instances.
                Console.SetOut(new DualWriter(_logStreamWriter, actualConsoleOutTarget, false));
                Console.SetError(new DualWriter(_logStreamWriter, actualConsoleErrorTarget, true));

                _isInitializedAndLoggingToFile = true;
                Console.WriteLine($"FileLogger Initialized. ConsoleOutputLevel: {_consoleLogLevel}. Logging to: {logFilePath}");
            }
            catch (Exception ex)
            {
                _isInitializedAndLoggingToFile = false;
                // Critical error during initialization. Restore original console writers immediately.
                // Use System.Console here as our Console.SetOut/Error might be in a bad state or not yet fully set.
                if (Console.Out is DualWriter && _originalConsoleOut != null) Console.SetOut(_originalConsoleOut);
                if (Console.Error is DualWriter && _originalConsoleError != null) Console.SetError(_originalConsoleError);

                System.Console.Error.WriteLine($"CRITICAL FileLogger Error: Failed to initialize file logging. All logs will go to console only (if restored). Exception: {ex.ToString()}");
                System.Console.Error.WriteLine($"Attempted log directory: {_logDirectory}, Attempted log file: {logFilePath}");

                // Clean up the log stream writer if it was created.
                try { _logStreamWriter?.Dispose(); } catch { /* ignored */ }
                _logStreamWriter = null;
            }
        }

        /// <summary>
        /// Deletes log files older than a specified number of retention days from the log directory.
        /// </summary>
        /// <param name="retentionDays">The number of days to retain log files. Files older than this will be deleted.</param>
        public static void CleanupOldLogFiles(int retentionDays)
        {
            if (!_isInitializedAndLoggingToFile || string.IsNullOrWhiteSpace(_logDirectory) || !Directory.Exists(_logDirectory) || retentionDays <= 0)
            {
                string reason = "";
                if (!_isInitializedAndLoggingToFile) reason = "logger not successfully initialized for file output";
                else if (string.IsNullOrWhiteSpace(_logDirectory) || !Directory.Exists(_logDirectory)) reason = $"log directory '{_logDirectory ?? "NULL"}' is invalid or does not exist";
                else if (retentionDays <= 0) reason = "retention days is not positive";

                var errorWriter = _originalConsoleError ?? System.Console.Error;
                errorWriter.WriteLine($"FileLogger: CleanupOldLogFiles skipped. Reason: {reason}.");
                return;
            }

            Console.WriteLine($"FileLogger: Checking for log files older than {retentionDays} days in '{_logDirectory}' for cleanup...");
            try
            {
                var files = Directory.GetFiles(_logDirectory, "*.log"); // Get all .log files.

                // Determine prefix and suffix from the log file name format to correctly parse dates.
                // This assumes the date format is enclosed in {0:...} like in string.Format.
                int datePlaceholderStart = _logFileNameFormat.IndexOf("{0:", StringComparison.Ordinal);
                int datePlaceholderEnd = _logFileNameFormat.IndexOf("}", datePlaceholderStart, StringComparison.Ordinal);

                if (datePlaceholderStart == -1 || datePlaceholderEnd == -1)
                {
                    Console.Error.WriteLine($"FileLogger: Cannot parse log file name format ('{_logFileNameFormat}') for date extraction. Cleanup aborted.");
                    return;
                }

                string prefix = _logFileNameFormat.Substring(0, datePlaceholderStart);
                string suffix = _logFileNameFormat.Substring(datePlaceholderEnd + 1);
                // Extract the date format string itself (e.g., "yyyy-MM-dd")
                string dateFormatInName = _logFileNameFormat.Substring(datePlaceholderStart + "{0:".Length, datePlaceholderEnd - (datePlaceholderStart + "{0:".Length));


                foreach (var file in files)
                {
                    try
                    {
                        var fileNameOnly = Path.GetFileName(file);
                        if (fileNameOnly.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && fileNameOnly.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        {
                            // Extract the date string part from the filename.
                            string dateString = fileNameOnly.Substring(prefix.Length, fileNameOnly.Length - prefix.Length - suffix.Length);

                            // The original code had a specific check for an underscore within the dateString,
                            // potentially for formats like "yyyy-MM-dd_HH". If the dateFormatInName itself
                            // contains such structures, TryParseExact should handle it.
                            // For robustness, if the extracted 'dateFormatInName' is simple like 'yyyy-MM-dd',
                            // and 'dateString' might have extra parts (e.g. '2023-01-01_details'), we need to ensure
                            // TryParseExact uses a dateString that strictly matches dateFormatInName.
                            // The provided code snippet's logic for handling an underscore was:
                            // if (dateString.Contains("_")) dateString = dateString.Substring(0, dateString.IndexOf('_'));
                            // This assumes the core date part is before any underscore.
                            // We will rely on TryParseExact with the extracted dateFormatInName. If dateString
                            // is longer than what dateFormatInName describes, TryParseExact will fail, which is correct.

                            if (DateTime.TryParseExact(dateString, dateFormatInName, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fileDate))
                            {
                                // Compare fileDate (interpreted as UTC if not specified otherwise by format) with UtcNow.
                                // Convert fileDate to UTC for consistent comparison, assuming it represents the start of that day UTC.
                                if (fileDate.ToUniversalTime().Date < DateTime.UtcNow.AddDays(-retentionDays).Date)
                                {
                                    Console.WriteLine($"FileLogger: Deleting old log file: {fileNameOnly}");
                                    File.Delete(file);
                                }
                            }
                            else
                            {
                                // Log if a file matches prefix/suffix but its date part doesn't parse correctly.
                                Console.WriteLine($"FileLogger Warning: Could not parse date from log file '{fileNameOnly}' using format '{dateFormatInName}'. Skipping for cleanup.");
                            }
                        }
                    }
                    catch (Exception exInner)
                    {
                        Console.Error.WriteLine($"FileLogger: Error processing file '{file}' for cleanup. {exInner.Message}");
                    }
                }
                Console.WriteLine("FileLogger: Old log files cleanup complete.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FileLogger Error: Failed to cleanup old log files. {ex.Message}");
            }
        }

        /// <summary>
        /// Disposes the file logger, flushing any buffered messages,
        /// closing the log file stream, and restoring original console output streams.
        /// </summary>
        public static void Dispose()
        {
            // Log a shutdown message before restoring original writers, so it goes to the file if possible.
            if (_isInitializedAndLoggingToFile && _logStreamWriter != null)
            {
                try
                {
                    Console.WriteLine($"FileLogger Shutting Down. Restoring original console writers."); // This will use DualWriter
                    _logStreamWriter.Flush(); // Ensure final messages are written to the file.
                }
                catch (ObjectDisposedException) { /* _logStreamWriter already disposed, can't log to file. */ }
                catch (Exception ex)
                {
                    // If logging the shutdown message fails, report to original error stream (or System.Console.Error if unavailable).
                    (_originalConsoleError ?? System.Console.Error).WriteLine($"FileLogger: Error during final flush before dispose: {ex.Message}");
                }
            }

            // Restore original console writers first, regardless of logger state.
            // Check if Console.Out/Error are indeed our DualWriter instances before setting them back.
            if (_originalConsoleOut != null && (Console.Out is DualWriter || Console.Out != _originalConsoleOut))
            {
                Console.SetOut(_originalConsoleOut);
            }
            if (_originalConsoleError != null && (Console.Error is DualWriter || Console.Error != _originalConsoleError))
            {
                Console.SetError(_originalConsoleError);
            }

            // Now, safely dispose the log writer and stream.
            if (_logStreamWriter != null)
            {
                try
                {
                    _logStreamWriter.Close(); // Close also calls Dispose on the StreamWriter and its underlying stream.
                }
                catch (Exception ex)
                {
                    // Use System.Console here as our redirection should have been reverted.
                    System.Console.Error.WriteLine($"FileLogger Error during _logStreamWriter.Close(): {ex.Message}");
                }
                finally
                {
                    _logStreamWriter = null; // Ensure it's marked as disposed.
                }
            }
            _isInitializedAndLoggingToFile = false; // Mark as fully disposed/uninitialized.
        }
    }
}
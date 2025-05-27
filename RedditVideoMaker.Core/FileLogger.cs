// FileLogger.cs (in RedditVideoMaker.Core project)
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;

namespace RedditVideoMaker.Core
{
    public static class FileLogger
    {
        private static string _logDirectory = "logs";
        private static string _logFileNameFormat = "app_run_{0:yyyy-MM-dd}.log";
        private static StreamWriter? _logStreamWriter;
        private static TextWriter? _originalConsoleOut;
        private static TextWriter? _originalConsoleError;
        private static bool _isInitializedAndLoggingToFile = false;
        private static ConsoleLogLevel _consoleLogLevel = ConsoleLogLevel.Detailed;

        private class DualWriter : TextWriter
        {
            private readonly TextWriter? _consoleStream;
            private readonly StreamWriter? _fileStreamWriterInternal; // Keep a reference to the logger's stream writer
            private readonly bool _isErrorStreamDualWriter;

            // No _isFileStreamWriterDisposed flag needed here if FileLogger manages the StreamWriter lifecycle

            public DualWriter(StreamWriter? fileStreamWriter, TextWriter? consoleStream, bool isErrorStream)
            {
                _fileStreamWriterInternal = fileStreamWriter; // This is the shared _logStreamWriter from FileLogger
                _consoleStream = consoleStream;
                _isErrorStreamDualWriter = isErrorStream;
            }

            public override Encoding Encoding => _consoleStream?.Encoding ?? _fileStreamWriterInternal?.Encoding ?? Encoding.Default;

            private bool ShouldWriteToConsole()
            {
                if (_consoleStream == null) return false;
                switch (FileLogger._consoleLogLevel)
                {
                    case ConsoleLogLevel.Detailed: return true;
                    case ConsoleLogLevel.Summary: return _isErrorStreamDualWriter;
                    case ConsoleLogLevel.ErrorsOnly: return _isErrorStreamDualWriter;
                    case ConsoleLogLevel.Quiet: return false;
                    default: return true;
                }
            }

            public override void Write(char value) => WriteInternal(value.ToString(), false);
            public override void Write(string? value) => WriteInternal(value, false);
            public override void WriteLine(string? value) => WriteInternal(value, true);

            private void WriteInternal(string? value, bool isNewLine)
            {
                // Attempt to write to file if the main logger's stream writer is available
                if (FileLogger._logStreamWriter != null && FileLogger._isInitializedAndLoggingToFile)
                {
                    try
                    {
                        string timedValue = $"[{DateTime.UtcNow:HH:mm:ss.fff UTC}] {value}";
                        if (isNewLine) FileLogger._logStreamWriter.WriteLine(timedValue);
                        else FileLogger._logStreamWriter.Write(timedValue);
                        // AutoFlush is true on _logStreamWriter
                    }
                    catch (ObjectDisposedException)
                    {
                        // If it's disposed, we can't log to file anymore.
                        // This might happen if Dispose was called prematurely or concurrently.
                        FileLogger._isInitializedAndLoggingToFile = false; // Stop further file logging attempts
                        FileLogger._originalConsoleError?.WriteLine($"DualWriter: Attempted to write to disposed file log. Value: {value}");
                    }
                    catch (Exception ex)
                    {
                        FileLogger._originalConsoleError?.WriteLine($"DualWriter Error (to file): {ex.Message}. Value: {value}");
                        FileLogger._isInitializedAndLoggingToFile = false; // Stop further file logging attempts
                    }
                }

                if (ShouldWriteToConsole() && _consoleStream != null)
                {
                    try
                    {
                        if (isNewLine) _consoleStream.WriteLine(value);
                        else _consoleStream.Write(value);
                    }
                    catch (Exception ex)
                    {
                        FileLogger._originalConsoleError?.WriteLine($"DualWriter Error (to console): {ex.Message}");
                    }
                }
            }

            public override void Flush()
            {
                if (FileLogger._logStreamWriter != null && FileLogger._isInitializedAndLoggingToFile) try { FileLogger._logStreamWriter.Flush(); } catch { /* ignore */ }
                if (_consoleStream != null) try { _consoleStream.Flush(); } catch { /* ignore */ }
            }

            protected override void Dispose(bool disposing)
            {
                // This DualWriter does not own the _logStreamWriter.
                // FileLogger.Dispose() is responsible for the _logStreamWriter.
                // We only need to handle the base TextWriter disposal.
                base.Dispose(disposing);
            }
        }

        public static void Initialize(string logDirectory, ConsoleLogLevel consoleLevel, string logFileNameFormat = "app_run_{0:yyyy-MM-dd}.log")
        {
            _originalConsoleOut = Console.Out;
            _originalConsoleError = Console.Error;

            if (_isInitializedAndLoggingToFile) return;

            _logDirectory = logDirectory;
            _logFileNameFormat = logFileNameFormat;
            _consoleLogLevel = consoleLevel;
            string logFilePath = "";

            try
            {
                if (string.IsNullOrWhiteSpace(_logDirectory))
                {
                    _logDirectory = Path.Combine(AppContext.BaseDirectory, "logs_default");
                    _originalConsoleError.WriteLine($"FileLogger Init Warning: Log directory was null/empty, using default: {_logDirectory}");
                }
                else if (!Path.IsPathRooted(_logDirectory))
                {
                    _logDirectory = Path.Combine(AppContext.BaseDirectory, _logDirectory);
                }

                _originalConsoleOut.WriteLine($"FileLogger: Initializing. Target log directory: {_logDirectory}");

                if (!Directory.Exists(_logDirectory))
                {
                    _originalConsoleOut.WriteLine($"FileLogger: Creating log directory: {_logDirectory}");
                    Directory.CreateDirectory(_logDirectory);
                }

                logFilePath = Path.Combine(_logDirectory, string.Format(_logFileNameFormat, DateTime.UtcNow));
                _originalConsoleOut.WriteLine($"FileLogger: Attempting to create/append log file: {logFilePath}");

                var fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                _logStreamWriter = new StreamWriter(fileStream) { AutoFlush = true };

                TextWriter? actualConsoleOut = null;
                TextWriter? actualConsoleError = null;

                switch (_consoleLogLevel)
                {
                    case ConsoleLogLevel.Detailed: actualConsoleOut = _originalConsoleOut; actualConsoleError = _originalConsoleError; break;
                    case ConsoleLogLevel.Summary: actualConsoleOut = null; actualConsoleError = _originalConsoleError; break; // Only errors to console
                    case ConsoleLogLevel.ErrorsOnly: actualConsoleOut = null; actualConsoleError = _originalConsoleError; break;
                    case ConsoleLogLevel.Quiet: actualConsoleOut = null; actualConsoleError = null; break;
                }

                Console.SetOut(new DualWriter(_logStreamWriter, actualConsoleOut, false));
                Console.SetError(new DualWriter(_logStreamWriter, actualConsoleError, true));

                _isInitializedAndLoggingToFile = true;
                Console.WriteLine($"FileLogger Initialized. ConsoleOutputLevel: {_consoleLogLevel}. Logging to: {logFilePath}");
            }
            catch (Exception ex)
            {
                _isInitializedAndLoggingToFile = false;

                if (Console.Out is DualWriter && _originalConsoleOut != null) Console.SetOut(_originalConsoleOut);
                if (Console.Error is DualWriter && _originalConsoleError != null) Console.SetError(_originalConsoleError);

                System.Console.Error.WriteLine($"CRITICAL FileLogger Error: Failed to initialize file logging. All logs will go to console only. Exception: {ex.ToString()}");
                System.Console.Error.WriteLine($"Attempted log directory: {_logDirectory}, Attempted log file: {logFilePath}");

                _logStreamWriter?.Dispose();
                _logStreamWriter = null;
            }
        }

        public static void CleanupOldLogFiles(int retentionDays)
        {
            if (!_isInitializedAndLoggingToFile || string.IsNullOrWhiteSpace(_logDirectory) || retentionDays <= 0)
            {
                if (!_isInitializedAndLoggingToFile && _originalConsoleError != null) _originalConsoleError.WriteLine("FileLogger: CleanupOldLogFiles called but logger not successfully initialized for file output.");
                else if (!_isInitializedAndLoggingToFile) System.Console.Error.WriteLine("FileLogger: CleanupOldLogFiles called but logger not successfully initialized for file output (original error stream unavailable).");
                return;
            }

            Console.WriteLine($"FileLogger: Checking for log files older than {retentionDays} days in '{_logDirectory}'...");
            // ... (cleanup logic remains the same) ...
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Console.WriteLine($"FileLogger: Log directory '{_logDirectory}' does not exist. Nothing to clean up.");
                    return;
                }

                var files = Directory.GetFiles(_logDirectory, "*.log");
                string prefix = _logFileNameFormat.Substring(0, _logFileNameFormat.IndexOf('{')).TrimEnd('_');
                string suffix = _logFileNameFormat.Substring(_logFileNameFormat.IndexOf('}') + 1);

                foreach (var file in files)
                {
                    try
                    {
                        var fileNameOnly = Path.GetFileName(file);
                        if (fileNameOnly.StartsWith(prefix) && fileNameOnly.EndsWith(suffix))
                        {
                            string dateString = fileNameOnly.Substring(prefix.Length, fileNameOnly.Length - prefix.Length - suffix.Length);
                            if (dateString.Contains("_")) dateString = dateString.Substring(0, dateString.IndexOf('_'));

                            if (DateTime.TryParseExact(dateString, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fileDate))
                            {
                                if (fileDate.ToUniversalTime().Date < DateTime.UtcNow.AddDays(-retentionDays).Date)
                                {
                                    Console.WriteLine($"FileLogger: Deleting old log file: {fileNameOnly}");
                                    File.Delete(file);
                                }
                            }
                        }
                    }
                    catch (Exception exInner) { Console.Error.WriteLine($"FileLogger: Error processing file {file} for cleanup. {exInner.Message}"); }
                }
                Console.WriteLine("FileLogger: Old log files cleanup complete.");
            }
            catch (Exception ex) { Console.Error.WriteLine($"FileLogger Error: Failed to cleanup old log files. {ex.Message}"); }
        }

        public static void Dispose()
        {
            // Log shutdown message *before* restoring original writers, so it goes to file (if still possible)
            if (_isInitializedAndLoggingToFile && _logStreamWriter != null)
            {
                try
                {
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss UTC}] FileLogger Shutting Down. Restoring original console writers.");
                    _logStreamWriter.Flush(); // Ensure final messages are written
                }
                catch (ObjectDisposedException) { /* _logStreamWriter already disposed, can't log shutdown message to file */ }
                catch (Exception ex) { (_originalConsoleError ?? System.Console.Error).WriteLine($"FileLogger: Error during final flush before dispose: {ex.Message}"); }
            }

            // Restore original console writers first, regardless of logger state
            if (_originalConsoleOut != null && (Console.Out is DualWriter || Console.Out != _originalConsoleOut))
            {
                Console.SetOut(_originalConsoleOut);
            }
            if (_originalConsoleError != null && (Console.Error is DualWriter || Console.Error != _originalConsoleError))
            {
                Console.SetError(_originalConsoleError);
            }

            // Now, safely dispose the log writer
            if (_logStreamWriter != null)
            {
                try
                {
                    _logStreamWriter.Close(); // Close also calls Dispose on the underlying stream
                }
                catch (Exception ex)
                {
                    // Use System.Console here as our redirection is reverted
                    System.Console.Error.WriteLine($"FileLogger Error during _logStreamWriter.Close(): {ex.Message}");
                }
                finally
                {
                    _logStreamWriter = null;
                }
            }
            _isInitializedAndLoggingToFile = false; // Mark as fully disposed
        }
    }
}

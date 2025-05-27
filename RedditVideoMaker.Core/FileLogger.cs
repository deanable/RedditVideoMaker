// FileLogger.cs (in RedditVideoMaker.Core project)
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RedditVideoMaker.Core
{
    public static class FileLogger
    {
        private static string _logDirectory = "logs"; // Default
        private static string _logFileNameFormat = "app_run_{0:yyyy-MM-dd}.log";
        private static StreamWriter? _logWriter;
        private static TextWriter? _originalOut = Console.Out; // Capture original streams immediately
        private static TextWriter? _originalError = Console.Error;
        private static bool _isInitialized = false;
        private static ConsoleLogLevel _consoleLogLevel = ConsoleLogLevel.Detailed;

        private class DualWriter : TextWriter
        {
            private readonly TextWriter _fileWriterInstance; // Renamed to avoid confusion
            private readonly TextWriter? _consoleWriterInstance;
            private readonly bool _isErrorWriter;
            private volatile bool _isFileStreamDisposed = false; // Flag to indicate if file stream is disposed

            public DualWriter(TextWriter fileWriter, TextWriter? consoleWriter, bool isErrorWriter = false)
            {
                _fileWriterInstance = fileWriter;
                _consoleWriterInstance = consoleWriter;
                _isErrorWriter = isErrorWriter;
            }

            public override Encoding Encoding => _fileWriterInstance?.Encoding ?? _consoleWriterInstance?.Encoding ?? Encoding.Default;

            private bool ShouldWriteToConsole()
            {
                if (_consoleWriterInstance == null) return false;
                switch (FileLogger._consoleLogLevel)
                {
                    case ConsoleLogLevel.Detailed: return true;
                    case ConsoleLogLevel.Summary: return _isErrorWriter;
                    case ConsoleLogLevel.ErrorsOnly: return _isErrorWriter;
                    case ConsoleLogLevel.Quiet: return false;
                    default: return true;
                }
            }

            private void WriteInternal(string? s, bool appendNewLine)
            {
                if (!_isFileStreamDisposed)
                {
                    try
                    {
                        if (appendNewLine) _fileWriterInstance.WriteLine(s);
                        else _fileWriterInstance.Write(s);
                        _fileWriterInstance.Flush();
                    }
                    catch (ObjectDisposedException) { _isFileStreamDisposed = true; /* Don't try to write to file anymore */ }
                    catch (Exception ex) { _originalError?.WriteLine($"FileLogger.DualWriter Error writing to file: {ex.Message}"); _isFileStreamDisposed = true; }
                }

                if (ShouldWriteToConsole())
                {
                    try
                    {
                        if (appendNewLine) _consoleWriterInstance?.WriteLine(s);
                        else _consoleWriterInstance?.Write(s);
                        _consoleWriterInstance?.Flush();
                    }
                    catch (ObjectDisposedException) { /* Original console stream was somehow disposed, unlikely if we don't dispose it */ }
                    catch (Exception ex) { _originalError?.WriteLine($"FileLogger.DualWriter Error writing to console: {ex.Message}"); }
                }
            }

            public override void Write(char value) => WriteInternal(value.ToString(), false);
            public override void Write(string? value) => WriteInternal(value, false);
            public override void WriteLine(string? value)
            {
                // Timestamp is now added before calling this, or by a higher-level logger
                // For simplicity, let's add it here if missing, or assume higher level handles it.
                // Let's assume higher level (Program.cs messages) already include it for file consistency.
                // For file logging, the timestamp is added when Console.WriteLine is called by our Initialize or main Program.
                // We'll add it here for the file part of DualWriter for consistency.
                string timedValue = $"[{DateTime.UtcNow:HH:mm:ss.fff}] {value}";
                WriteInternal(timedValue, true); // For file
                if (ShouldWriteToConsole()) _consoleWriterInstance?.WriteLine(value); // Original value for console
            }

            public override void Flush()
            {
                if (!_isFileStreamDisposed) try { _fileWriterInstance.Flush(); } catch { _isFileStreamDisposed = true; }
                try { _consoleWriterInstance?.Flush(); } catch { /* ignore */ }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    // This DualWriter instance itself might be disposed by .NET when Console.Out/Error are finalized.
                    // We only explicitly created and own _fileWriterInstance (via _logWriter).
                    if (!_isFileStreamDisposed)
                    {
                        try { _fileWriterInstance?.Dispose(); } catch { /* ignore */ }
                        _isFileStreamDisposed = true;
                    }
                }
                base.Dispose(disposing);
            }
        }

        public static void Initialize(string logDirectory, ConsoleLogLevel consoleLevel, string logFileNameFormat = "app_run_{0:yyyy-MM-dd}.log")
        {
            if (_isInitialized) return;

            // Use original streams for initial logging in case of failure
            _originalOut = Console.Out;
            _originalError = Console.Error;

            _logDirectory = logDirectory;
            _logFileNameFormat = logFileNameFormat;
            _consoleLogLevel = consoleLevel;
            string logFilePath = "";

            try
            {
                if (string.IsNullOrWhiteSpace(_logDirectory))
                {
                    _originalError.WriteLine("FileLogger Init Error: Log directory path is null or empty. Using default 'logs' in AppContext.BaseDirectory.");
                    _logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
                }

                if (!Path.IsPathRooted(_logDirectory)) // Ensure directory is absolute
                {
                    _logDirectory = Path.Combine(AppContext.BaseDirectory, _logDirectory);
                }

                _originalOut.WriteLine($"FileLogger: Attempting to initialize. Log directory: {_logDirectory}");

                if (!Directory.Exists(_logDirectory))
                {
                    _originalOut.WriteLine($"FileLogger: Creating log directory: {_logDirectory}");
                    Directory.CreateDirectory(_logDirectory);
                }

                logFilePath = Path.Combine(_logDirectory, string.Format(_logFileNameFormat, DateTime.UtcNow));
                _originalOut.WriteLine($"FileLogger: Log file path will be: {logFilePath}");

                var fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                _logWriter = new StreamWriter(fileStream) { AutoFlush = true };

                TextWriter? actualConsoleOut = null;
                TextWriter? actualConsoleError = null;

                switch (_consoleLogLevel)
                {
                    case ConsoleLogLevel.Detailed:
                        actualConsoleOut = _originalOut; actualConsoleError = _originalError; break;
                    case ConsoleLogLevel.Summary:
                    case ConsoleLogLevel.ErrorsOnly:
                        actualConsoleOut = null; actualConsoleError = _originalError; break;
                    case ConsoleLogLevel.Quiet:
                        actualConsoleOut = null; actualConsoleError = null; break;
                }

                Console.SetOut(new DualWriter(_logWriter, actualConsoleOut, false));
                Console.SetError(new DualWriter(_logWriter, actualConsoleError, true));

                _isInitialized = true;
                Console.WriteLine($"FileLogger Initialized. ConsoleOutputLevel: {_consoleLogLevel}. Logging to: {logFilePath}");
            }
            catch (Exception ex)
            {
                _isInitialized = false; // Ensure it's false if init fails
                Console.SetOut(_originalOut); // Restore originals if anything went wrong with redirection
                Console.SetError(_originalError);
                System.Console.Error.WriteLine($"CRITICAL FileLogger Error: Failed to initialize. Logging will ONLY go to console. Exception: {ex.ToString()}");
                System.Console.Error.WriteLine($"Attempted log directory: {logDirectory}, Attempted log file: {logFilePath}");

                _logWriter?.Dispose(); // Clean up if partially created
                _logWriter = null;
            }
        }

        public static void CleanupOldLogFiles(int retentionDays)
        {
            if (!_isInitialized || _logWriter == null || retentionDays <= 0)
            {
                if (!_isInitialized) Console.Error.WriteLine("FileLogger: CleanupOldLogFiles called before successful Initialize or after Dispose.");
                return;
            }

            Console.WriteLine($"FileLogger: Checking for log files older than {retentionDays} days in '{_logDirectory}'...");
            // ... (cleanup logic remains the same) ...
            try
            {
                var files = Directory.GetFiles(_logDirectory, "*.log");
                string prefix = _logFileNameFormat.Substring(0, _logFileNameFormat.IndexOf('{'));
                string suffix = _logFileNameFormat.Substring(_logFileNameFormat.IndexOf('}') + 1);

                foreach (var file in files)
                {
                    try
                    {
                        var fileNameOnly = Path.GetFileName(file);
                        if (fileNameOnly.StartsWith(prefix) && fileNameOnly.EndsWith(suffix))
                        {
                            string dateString = fileNameOnly.Substring(prefix.Length, fileNameOnly.Length - prefix.Length - suffix.Length);
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
            if (_isInitialized)
            {
                Console.WriteLine($"FileLogger Shutting Down. Restoring original console writers.");

                TextWriter? currentOut = Console.Out;
                TextWriter? currentError = Console.Error;

                if (_originalOut != null) Console.SetOut(_originalOut);
                if (_originalError != null) Console.SetError(_originalError);

                // Dispose the DualWriter instances if they were set, which in turn disposes the _logWriter
                (currentOut as IDisposable)?.Dispose();
                (currentError as IDisposable)?.Dispose(); // Only if currentError is different from currentOut

                // _logWriter is disposed by DualWriter, so just nullify it here after restoring originals.
                // However, to be absolutely safe, if DualWriter's Dispose didn't nullify _logWriter for some reason
                // or if redirection failed, explicitly try disposing _logWriter if it's not already the same as currentOut's _fileWriter.
                // This part is tricky. DualWriter's Dispose *should* handle _logWriter.
                // For simplicity, let's rely on DualWriter's Dispose logic.
                _logWriter = null; // It's disposed via DualWriter
                _isInitialized = false;
                // _originalOut = null; // Don't nullify originals, they are the system's
                // _originalError = null;
            }
        }
    }
}

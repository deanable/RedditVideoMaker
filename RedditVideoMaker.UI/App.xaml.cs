// App.xaml.cs --- STEP 3: Launching Actual MainWindow via DI ---
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RedditVideoMaker.Core;
using RedditVideoMaker.UI; // For MainWindow, MainViewModel
using System;
using System.IO;
using System.Windows;

#nullable enable

namespace RedditVideoMaker.UI // Ensure this namespace matches your project
{
    public partial class App : Application
    {
        public IConfiguration? Configuration { get; private set; }
        public IServiceProvider? ServiceProvider { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            System.Diagnostics.Debug.WriteLine("App.OnStartup: STEP 3 - Entered OnStartup.");

            try
            {
                // 1. Build Configuration
                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                Configuration = builder.Build();
                System.Diagnostics.Debug.WriteLine("App.OnStartup: STEP 3 - Configuration built successfully.");

                // 2. Create ServiceCollection & Configure ALL Services (Core, ViewModels, Views)
                var serviceCollection = new ServiceCollection();
                ConfigureServices(serviceCollection); // Using the full ConfigureServices now
                System.Diagnostics.Debug.WriteLine("App.OnStartup: STEP 3 - All services configured.");

                // 3. Build ServiceProvider
                ServiceProvider = serviceCollection.BuildServiceProvider();
                System.Diagnostics.Debug.WriteLine("App.OnStartup: STEP 3 - ServiceProvider built successfully.");

                // 4. Initialize FileLogger
                if (ServiceProvider != null)
                {
                    var generalOptions = ServiceProvider.GetRequiredService<IOptions<GeneralOptions>>().Value;
                    string fullLogDirectoryPath = generalOptions.LogFileDirectory;
                    if (!Path.IsPathRooted(fullLogDirectoryPath))
                    {
                        fullLogDirectoryPath = Path.Combine(AppContext.BaseDirectory, fullLogDirectoryPath);
                    }
                    fullLogDirectoryPath = Path.GetFullPath(fullLogDirectoryPath);

                    FileLogger.Initialize(fullLogDirectoryPath, generalOptions.ConsoleOutputLevel);
                    FileLogger.CleanupOldLogFiles(generalOptions.LogFileRetentionDays);
                    Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] WPF App Startup (Step 3 - FileLogger Initialized).");
                    System.Diagnostics.Debug.WriteLine("App.OnStartup: STEP 3 - FileLogger initialized.");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("App.OnStartup: STEP 3 - ServiceProvider was null, FileLogger NOT initialized.");
                    // This case should ideally not be hit if DI setup is correct.
                }

                // --- 5. Resolve and Show ACTUAL MainWindow ---
                System.Diagnostics.Debug.WriteLine("App.OnStartup: STEP 3 - Attempting to resolve and show MainWindow.");
                if (ServiceProvider == null)
                {
                    System.Diagnostics.Debug.WriteLine("App.OnStartup: STEP 3 - ServiceProvider is null before resolving MainWindow. This is an error.");
                    MessageBox.Show("Fatal Error: ServiceProvider not initialized.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>(); // This will also resolve MainViewModel if injected into MainWindow
                System.Diagnostics.Debug.WriteLine("App.OnStartup: STEP 3 - MainWindow resolved from ServiceProvider.");
                mainWindow.Show();
                System.Diagnostics.Debug.WriteLine("App.OnStartup: STEP 3 - mainWindow.Show() called.");
            }
            catch (Exception ex)
            {
                // This catch block is crucial. If an error happens during DI resolution
                // of MainWindow or MainViewModel, or in their constructors, it will be caught here.
                string errorMessage = $"CRITICAL STARTUP ERROR (STEP 3 - MainWindow Launch): {ex.ToString()}";
                System.Diagnostics.Debug.WriteLine(errorMessage);
                try
                {
                    Console.Error.WriteLine("Attempting to log critical startup error to FileLogger as well: " + errorMessage);
                }
                catch { /* Ignore */ }
                MessageBox.Show($"Startup Error (Step 3 - MainWindow Launch):\n{ex.Message}\n\n{ex.StackTrace}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ConfigureServices now registers EVERYTHING: Core services, Options, ViewModels, and Views.
        private void ConfigureServices(IServiceCollection services)
        {
            System.Diagnostics.Debug.WriteLine("App.ConfigureServices: STEP 3 - Entered.");
            if (Configuration == null)
            {
                System.Diagnostics.Debug.WriteLine("App.ConfigureServices: STEP 3 - Configuration is unexpectedly null!");
                throw new InvalidOperationException("Configuration cannot be null when configuring services.");
            }

            services.AddSingleton<IConfiguration>(Configuration);

            services.Configure<GeneralOptions>(Configuration.GetSection(GeneralOptions.SectionName));
            services.Configure<RedditOptions>(Configuration.GetSection(RedditOptions.SectionName));
            services.Configure<VideoOptions>(Configuration.GetSection(VideoOptions.SectionName));
            services.Configure<TtsOptions>(Configuration.GetSection(TtsOptions.SectionName));
            services.Configure<YouTubeOptions>(Configuration.GetSection(YouTubeOptions.SectionName));
            System.Diagnostics.Debug.WriteLine("App.ConfigureServices: STEP 3 - Options classes configured.");

            services.AddSingleton<RedditService>();
            services.AddSingleton<ImageService>();
            services.AddSingleton<TtsService>();
            services.AddSingleton<VideoService>();
            services.AddSingleton<YouTubeService>();
            services.AddSingleton<UploadTrackerService>();
            System.Diagnostics.Debug.WriteLine("App.ConfigureServices: STEP 3 - Core services registered.");

            // --- Register UI Components ---
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainWindow>();
            System.Diagnostics.Debug.WriteLine("App.ConfigureServices: STEP 3 - MainViewModel and MainWindow registered.");

            // For later:
            // services.AddTransient<SettingsViewModel>();
            // services.AddTransient<SettingsWindow>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            FileLogger.Dispose();
            System.Diagnostics.Debug.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] WPF Application Exit.");
            base.OnExit(e);
        }
    }
}
#nullable disable
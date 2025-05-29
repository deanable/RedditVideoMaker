// MainViewModel.cs (Conceptual Outline)
using RedditVideoMaker.Core;
using Microsoft.Extensions.Options;
using System; // Crucial for Progress<T>
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

// TODO: Consider moving INotifyPropertyChanged and RelayCommand to separate files
// and potentially using a library like CommunityToolkit.Mvvm.

public class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        (GenerateVideoCommand as RelayCommand)?.RaiseCanExecuteChanged();
        return true;
    }

    private readonly RedditService _redditService;
    private readonly ImageService _imageService;
    private readonly TtsService _ttsService;
    private readonly VideoService _videoService;
    private readonly YouTubeService _youTubeService;
    private readonly UploadTrackerService _uploadTracker;

    private readonly GeneralOptions _generalOptions;
    private readonly RedditOptions _defaultRedditOptions;
    private readonly VideoOptions _defaultVideoOptions;
    private readonly YouTubeOptions _defaultYouTubeOptions;

    private string _postUrlInput = "";
    public string PostUrlInput
    {
        get => _postUrlInput;
        set => SetProperty(ref _postUrlInput, value);
    }

    private int _numberOfCommentsToInclude = 5;
    public int NumberOfCommentsToInclude
    {
        get => _numberOfCommentsToInclude;
        set => SetProperty(ref _numberOfCommentsToInclude, value);
    }

    private string _currentStatusMessage = "Ready";
    public string CurrentStatusMessage
    {
        get => _currentStatusMessage;
        private set => SetProperty(ref _currentStatusMessage, value);
    }

    private int _currentProgressPercentage;
    public int CurrentProgressPercentage
    {
        get => _currentProgressPercentage;
        private set => SetProperty(ref _currentProgressPercentage, value);
    }

    private bool _isGenerating;
    public bool IsGenerating
    {
        get => _isGenerating;
        private set => SetProperty(ref _isGenerating, value);
    }

    public ICommand GenerateVideoCommand { get; }

    public MainViewModel(
        RedditService redditService, ImageService imageService, TtsService ttsService,
        VideoService videoService, YouTubeService youTubeService, UploadTrackerService uploadTracker,
        IOptions<GeneralOptions> generalOptions, IOptions<RedditOptions> redditOptions,
        IOptions<VideoOptions> videoOptions, IOptions<TtsOptions> ttsOptions,
        IOptions<YouTubeOptions> youTubeOptions)
    {
        _redditService = redditService;
        _imageService = imageService;
        _ttsService = ttsService;
        _videoService = videoService;
        _youTubeService = youTubeService;
        _uploadTracker = uploadTracker;

        _generalOptions = generalOptions.Value;
        _defaultRedditOptions = redditOptions.Value;
        _defaultVideoOptions = videoOptions.Value;
        _defaultYouTubeOptions = youTubeOptions.Value;

        PostUrlInput = _defaultRedditOptions.PostUrl ?? "";
        NumberOfCommentsToInclude = _defaultVideoOptions.NumberOfCommentsToInclude;

        GenerateVideoCommand = new RelayCommand(async () => await ExecuteGenerateVideoAsync(), () => CanExecuteGenerateVideo());
    }

    private bool CanExecuteGenerateVideo()
    {
        return !IsGenerating;
    }

    private async Task ExecuteGenerateVideoAsync()
    {
        if (!CanExecuteGenerateVideo()) return;

        IsGenerating = true;
        CurrentStatusMessage = "Starting video generation...";
        CurrentProgressPercentage = 0;

        // Note: Using explicit cast to IProgress<T> for .Report() calls as a workaround
        // for compiler issues (CS1061) experienced in the user's environment.
        var textProgress = new Progress<string>(status => CurrentStatusMessage = status);
        var percentageProgress = new Progress<int>(percent => CurrentProgressPercentage = percent);
        IProgress<string> textProgressReporter = textProgress; // Or cast directly: ((IProgress<string>)textProgress)
        IProgress<int> percentageProgressReporter = percentageProgress;

        try
        {
            var currentRedditOptions = new RedditOptions
            {
                PostUrl = this.PostUrlInput,
                Subreddit = _defaultRedditOptions.Subreddit,
                NumberOfVideosInBatch = 1,
                // ... other reddit options from UI or defaults
            };

            var currentVideoOptions = new VideoOptions
            {
                NumberOfCommentsToInclude = this.NumberOfCommentsToInclude,
                // ... populate ALL relevant video options from UI or defaults from _defaultVideoOptions
                OutputResolution = _defaultVideoOptions.OutputResolution,
                AssetsRootDirectory = _defaultVideoOptions.AssetsRootDirectory,
                BackgroundVideoPath = _defaultVideoOptions.BackgroundVideoPath,
                // ... etc.
            };

            textProgressReporter.Report("Fetching Reddit posts...");
            List<RedditPostData> postsToProcess = await _redditService.GetTopPostsAsync(/* Pass currentRedditOptions if service methods are adapted */);

            if (postsToProcess == null || !postsToProcess.Any())
            {
                textProgressReporter.Report("No suitable Reddit posts found.");
                IsGenerating = false;
                return;
            }

            int postNum = 0;
            double baseProgressForPost = 0;
            double incrementPerPost = 100.0 / postsToProcess.Count;

            foreach (var selectedPost in postsToProcess)
            {
                postNum++;
                textProgressReporter.Report($"Processing post {postNum}/{postsToProcess.Count}: {selectedPost.Title?.Substring(0, Math.Min(30, selectedPost.Title?.Length ?? 0))}...");
                percentageProgressReporter.Report((int)(baseProgressForPost + (incrementPerPost * 0.05)));

                if (_defaultYouTubeOptions.EnableDuplicateCheck && _uploadTracker.HasPostBeenUploaded(selectedPost.Id!))
                {
                    textProgressReporter.Report($"Post ID '{selectedPost.Id}' already processed. Skipping.");
                    await Task.Delay(1000);
                    baseProgressForPost += incrementPerPost;
                    percentageProgressReporter.Report((int)baseProgressForPost);
                    continue;
                }

                textProgressReporter.Report("Generating title sequence...");
                await Task.Delay(500); // Placeholder for actual work
                percentageProgressReporter.Report((int)(baseProgressForPost + (incrementPerPost * 0.25)));

                if (!string.IsNullOrWhiteSpace(selectedPost.Selftext))
                {
                    textProgressReporter.Report("Generating self-text sequence...");
                    await Task.Delay(500); // Placeholder
                    percentageProgressReporter.Report((int)(baseProgressForPost + (incrementPerPost * 0.45)));
                }

                textProgressReporter.Report("Fetching comments...");
                List<RedditCommentData>? comments = await _redditService.GetCommentsAsync(selectedPost.Subreddit!, selectedPost.Id! /*, pass comment options */);
                if (comments != null && comments.Any())
                {
                    textProgressReporter.Report($"Generating sequences for {comments.Count} comments...");
                    await Task.Delay(1000); // Placeholder
                }
                percentageProgressReporter.Report((int)(baseProgressForPost + (incrementPerPost * 0.75)));

                textProgressReporter.Report("Concatenating video clips...");
                await Task.Delay(500); // Placeholder
                percentageProgressReporter.Report((int)(baseProgressForPost + (incrementPerPost * 0.90)));

                if (!_generalOptions.IsInTestingModule && !string.IsNullOrWhiteSpace(_defaultYouTubeOptions.ClientSecretJsonPath))
                {
                    textProgressReporter.Report("Uploading to YouTube...");
                    await Task.Delay(1000); // Placeholder
                }

                await _uploadTracker.AddPostIdToLogAsync(selectedPost.Id!);
                textProgressReporter.Report($"Post '{selectedPost.Id}' processed successfully.");
                baseProgressForPost += incrementPerPost;
                percentageProgressReporter.Report((int)Math.Min(100.0, baseProgressForPost));
            }

            CurrentStatusMessage = "Video generation process completed.";
        }
        catch (Exception ex)
        {
            CurrentStatusMessage = $"Error: {ex.Message}";
            Console.Error.WriteLine($"CRITICAL ERROR in MainViewModel: {ex.ToString()}");
        }
        finally
        {
            IsGenerating = false;
        }
    }
    // In MainViewModel.cs
    // (Assuming _serviceProvider is injected or available)
    // private readonly IServiceProvider _serviceProvider;

    // Command execution for OpenSettingsCommand
    private void ExecuteOpenSettings()
    {
        // Resolve SettingsWindow using DI. This also resolves its SettingsViewModel.
        // var settingsWindow = _serviceProvider.GetRequiredService<SettingsWindow>();
        // settingsWindow.Owner = Application.Current.MainWindow; // Good practice for dialogs
        // settingsWindow.ShowDialog(); // Show as a modal dialog
        // After ShowDialog returns, you could potentially refresh settings in MainViewModel if needed.
    }
}

// Placeholder for a simple RelayCommand (ideally use CommunityToolkit.Mvvm)
public class RelayCommand : ICommand
{
    private readonly Action? _execute; // Made nullable
    private readonly Func<Task>? _executeAsync;
    private readonly Func<bool>? _canExecute;

    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

    public async void Execute(object? parameter)
    {
        if (_execute != null)
            _execute();
        else if (_executeAsync != null)
            await _executeAsync(); // Asynchronously await the task
    }

    public void RaiseCanExecuteChanged()
    {
        // Ensure this is called on the UI thread if CanExecuteChanged subscribers expect it
        // For simple scenarios, direct invocation is fine. For complex ones, consider Dispatcher.
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
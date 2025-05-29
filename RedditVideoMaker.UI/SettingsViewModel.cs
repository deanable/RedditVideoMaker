// SettingsViewModel.cs (Conceptual Outline - with fixes)
using RedditVideoMaker.Core;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration; // If you plan to use IConfiguration for saving
using System;
using System.Windows.Input;
using System.ComponentModel; // For INotifyPropertyChanged
using System.Runtime.CompilerServices; // For CallerMemberName
using System.Collections.Generic; // For EqualityComparer
using System.IO; // For Path, File (if saving appsettings.json directly)

// Assuming you have a ViewModelBase or will implement INotifyPropertyChanged
// For this example, INotifyPropertyChanged is implemented directly.
public class SettingsViewModel : INotifyPropertyChanged
{
    // INotifyPropertyChanged implementation
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
        // If any commands depend on this property for their CanExecute status:
        // (SaveSettingsCommand as RelayCommand)?.RaiseCanExecuteChanged(); // Example
        return true;
    }

    // Injected services/options
    private readonly IOptionsMonitor<GeneralOptions> _generalOptionsMonitor;
    private readonly IOptionsMonitor<TtsOptions> _ttsOptionsMonitor;
    private readonly IOptionsMonitor<YouTubeOptions> _youTubeOptionsMonitor;
    private readonly IOptionsMonitor<RedditOptions> _redditOptionsMonitor; // Added for defaults
    private readonly IOptionsMonitor<VideoOptions> _videoOptionsMonitor; // Added for defaults
    // private readonly IConfiguration _configuration; // If needed for saving

    // --- Properties for UI Binding (Examples with full pattern) ---
    private bool _isInTestingModule;
    public bool IsInTestingModule
    {
        get => _isInTestingModule;
        set => SetProperty(ref _isInTestingModule, value);
    }

    private string _logFileDirectory = string.Empty;
    public string LogFileDirectory
    {
        get => _logFileDirectory;
        set => SetProperty(ref _logFileDirectory, value);
    }

    private int _logFileRetentionDays;
    public int LogFileRetentionDays
    {
        get => _logFileRetentionDays;
        set => SetProperty(ref _logFileRetentionDays, value);
    }

    private ConsoleLogLevel _selectedConsoleLogLevel;
    public ConsoleLogLevel SelectedConsoleLogLevel
    {
        get => _selectedConsoleLogLevel;
        set => SetProperty(ref _selectedConsoleLogLevel, value);
    }
    // public IEnumerable<ConsoleLogLevel> AvailableConsoleLogLevels => Enum.GetValues(typeof(ConsoleLogLevel)).Cast<ConsoleLogLevel>(); // Helper for ComboBox

    // --- TTS Properties example ---
    private string _selectedTtsEngine = string.Empty;
    public string SelectedTtsEngine
    {
        get => _selectedTtsEngine;
        set
        {
            if (SetProperty(ref _selectedTtsEngine, value))
            {
                // Update visibility properties when engine changes
                OnPropertyChanged(nameof(IsAzureTtsSelected));
                OnPropertyChanged(nameof(IsGoogleCloudTtsSelected));
                // OnPropertyChanged(nameof(IsSystemSpeechSelected)); // If needed
            }
        }
    }
    // public string[] AvailableTtsEngines => new[] { "SystemSpeech", "Azure", "GoogleCloud" }; // For ComboBox

    // Visibility properties for conditional UI sections
    public bool IsAzureTtsSelected => SelectedTtsEngine == "Azure";
    public bool IsGoogleCloudTtsSelected => SelectedTtsEngine == "GoogleCloud";

    private string _azureSpeechKey = string.Empty;
    public string AzureSpeechKey { get => _azureSpeechKey; set => SetProperty(ref _azureSpeechKey, value); }
    // ... Add all other properties for TTS, YouTube, default Reddit/Video options similarly ...
    private string _clientSecretJsonPath = string.Empty;
    public string ClientSecretJsonPath { get => _clientSecretJsonPath; set => SetProperty(ref _clientSecretJsonPath, value); }


    // --- Commands ---
    public ICommand SaveSettingsCommand { get; }
    // public ICommand BrowseLogDirectoryCommand { get; } // Example, implement if needed
    // public ICommand BrowseGoogleCredPathCommand { get; }
    // public ICommand BrowseYouTubeSecretPathCommand { get; }


    public SettingsViewModel(
        IOptionsMonitor<GeneralOptions> generalOptionsMonitor,
        IOptionsMonitor<TtsOptions> ttsOptionsMonitor,
        IOptionsMonitor<YouTubeOptions> youTubeOptionsMonitor,
        IOptionsMonitor<RedditOptions> redditOptionsMonitor, // Added
        IOptionsMonitor<VideoOptions> videoOptionsMonitor   // Added
        /* IConfiguration configuration */) // Inject IConfiguration if using it for saving
    {
        _generalOptionsMonitor = generalOptionsMonitor;
        _ttsOptionsMonitor = ttsOptionsMonitor;
        _youTubeOptionsMonitor = youTubeOptionsMonitor;
        _redditOptionsMonitor = redditOptionsMonitor; // Store
        _videoOptionsMonitor = videoOptionsMonitor;   // Store
        // _configuration = configuration;

        LoadCurrentSettings();

        // Initialize commands - Ensure this line is present and SaveSettingsCommand is not null!
        SaveSettingsCommand = new RelayCommand(ExecuteSaveSettings, CanExecuteSaveSettings);
        // Initialize other browse commands here if you add them.
    }

    private void LoadCurrentSettings()
    {
        var generalOpts = _generalOptionsMonitor.CurrentValue;
        IsInTestingModule = generalOpts.IsInTestingModule; // Now assignable
        LogFileDirectory = generalOpts.LogFileDirectory;   // Now assignable
        LogFileRetentionDays = generalOpts.LogFileRetentionDays;
        SelectedConsoleLogLevel = generalOpts.ConsoleOutputLevel;

        var ttsOpts = _ttsOptionsMonitor.CurrentValue;
        SelectedTtsEngine = ttsOpts.Engine;
        AzureSpeechKey = ttsOpts.AzureSpeechKey ?? string.Empty;
        // ... load all other settings from their respective IOptionsMonitor.CurrentValue ...
        // Ensure to handle nulls from options if properties are non-nullable string.Empty for strings is common.

        var youtubeOpts = _youTubeOptionsMonitor.CurrentValue;
        ClientSecretJsonPath = youtubeOpts.ClientSecretJsonPath ?? string.Empty;
        // ... load other YouTube options ...

        // Load defaults from RedditOptions and VideoOptions if you have properties for them
        // var defaultRedditOpts = _redditOptionsMonitor.CurrentValue;
        // DefaultSubreddit = defaultRedditOpts.Subreddit;
        // var defaultVideoOpts = _videoOptionsMonitor.CurrentValue;
        // AssetsRootDirectory = defaultVideoOpts.AssetsRootDirectory;
    }

    private bool CanExecuteSaveSettings() => true; // Or add validation logic

    private void ExecuteSaveSettings()
    {
        Console.WriteLine("SettingsViewModel: SaveSettingsCommand executed.");

        // --- WARNING: Persisting settings to appsettings.json at runtime is complex and has limitations. ---
        // It's generally better to save user-specific settings to a user profile location.
        // For simplicity, if you must modify appsettings.json, you'd need to:
        // 1. Read the current appsettings.json (e.g., using File.ReadAllText).
        // 2. Deserialize to a JObject (Newtonsoft.Json) or JsonDocument (System.Text.Json).
        // 3. Modify the specific values in the JSON structure.
        // 4. Serialize back to a string and write to appsettings.json.
        // 5. This change will likely require an application restart for IOptions to pick up changes,
        //    unless you have advanced IConfiguration reload mechanisms.

        // Example of how you might conceptually update GeneralOptions (needs robust implementation)
        var generalOptsToSave = new GeneralOptions
        {
            IsInTestingModule = this.IsInTestingModule,
            LogFileDirectory = this.LogFileDirectory,
            LogFileRetentionDays = this.LogFileRetentionDays,
            ConsoleOutputLevel = this.SelectedConsoleLogLevel
        };
        // --> Here you would call a service to persist 'generalOptsToSave'
        // e.g., _settingsPersistenceService.Save(generalOptsToSave, GeneralOptions.SectionName);

        var ttsOptsToSave = new TtsOptions
        {
            Engine = this.SelectedTtsEngine,
            AzureSpeechKey = this.AzureSpeechKey,
            // ... etc.
        };
        // --> Persist ttsOptsToSave

        // ... and so on for other options sections ...

        // After saving, you might want to inform the user.
        // If IOptionsMonitor is used elsewhere, some components might pick up changes if the underlying provider reloads.
        // However, writing to appsettings.json and having it auto-reload reliably into IOptionsMonitor
        // without an app restart can be tricky to set up correctly.
        Console.WriteLine("Settings have been conceptually 'saved'. Implement actual persistence logic.");
    }
}

// Ensure the placeholder RelayCommand (or a proper one from a library) is also in your project.
// public class RelayCommand : ICommand { ... } // (From previous response)
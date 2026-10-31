using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SE.Roleplay.Framework.Launcher.Models;
using SE.Roleplay.Framework.Launcher.Services;
namespace SE.Roleplay.Framework.Launcher;
public partial class MainWindow : Window
{
    private readonly GameLauncher _gameLauncher = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly GameInstallDetector _gameDetector = new();
    private readonly ServerDirectoryClient _directoryClient = new();
    private readonly ObservableCollection<ServerListing> _servers = new();
    private LauncherSettings _settings = new();
    public MainWindow()
    {
        InitializeComponent();
        ServerList.ItemsSource = _servers;
        ShowPage(HomePage, "WELCOME BACK", "Roleplay hub", HomeNav);
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _settingsStore.LoadAsync();
        DirectoryUrlBox.Text = _settings.ServerDirectoryUrl;
        GamePathBox.Text = _settings.GameExecutablePath;
        if (string.IsNullOrWhiteSpace(GamePathBox.Text) || !File.Exists(GamePathBox.Text))
        {
            var detected = _gameDetector.FindGameExecutable();
            if (detected is not null) GamePathBox.Text = detected;
        }
        UpdateGameStatus();
        if (!string.IsNullOrWhiteSpace(_settings.ServerDirectoryUrl)) await RefreshServerDirectoryAsync();
    }
    private void ShowPage(FrameworkElement page, string eyebrow, string title, Button active)
    {
        HomePage.Visibility = Visibility.Collapsed; ServersPage.Visibility = Visibility.Collapsed;
        CharactersPage.Visibility = Visibility.Collapsed; SettingsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible; PageEyebrow.Text = eyebrow; PageTitle.Text = title;
        foreach (var button in new[] { HomeNav, ServersNav, CharactersNav, SettingsNav })
        {
            button.Background = button == active ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("AltBrush");
            button.Foreground = button == active ? new SolidColorBrush(Color.FromRgb(11, 23, 18)) : (Brush)FindResource("TextBrush");
        }
    }
    private void UpdateGameStatus()
    {
        var path = GamePathBox.Text;
        var found = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        GameStatusText.Text = found ? "Space Engineers installation detected" : "Game path not detected — Steam launch available";
        HomeGamePathText.Text = found ? path : "Installation not detected. Steam launch will be used as a fallback.";
    }
    private void HomeNav_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage, "WELCOME BACK", "Roleplay hub", HomeNav);
    private void ServersNav_Click(object sender, RoutedEventArgs e) => ShowPage(ServersPage, "COMMUNITY DIRECTORY", "Find your server", ServersNav);
    private void CharactersNav_Click(object sender, RoutedEventArgs e) => ShowPage(CharactersPage, "PLAYER PROFILE", "Characters", CharactersNav);
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage, "PREFERENCES", "Settings", SettingsNav);
    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.ServerDirectoryUrl = DirectoryUrlBox.Text.Trim(); _settings.GameExecutablePath = GamePathBox.Text.Trim();
        try { await _settingsStore.SaveAsync(_settings); SettingsStatusText.Text = "Settings saved."; UpdateGameStatus(); if (!string.IsNullOrWhiteSpace(_settings.ServerDirectoryUrl)) await RefreshServerDirectoryAsync(); }
        catch (Exception ex) { SettingsStatusText.Text = $"Could not save settings: {ex.Message}"; }
    }
    private void DetectGame_Click(object sender, RoutedEventArgs e)
    {
        var path = _gameDetector.FindGameExecutable();
        if (path is null) { SettingsStatusText.Text = "Automatic detection did not find the default Steam library. Enter the SpaceEngineers.exe path manually."; return; }
        GamePathBox.Text = path; UpdateGameStatus(); SettingsStatusText.Text = "Installation detected. Save settings to remember this path.";
    }
    private async void RefreshServers_Click(object sender, RoutedEventArgs e) => await RefreshServerDirectoryAsync();
    private async Task RefreshServerDirectoryAsync()
    {
        var url = DirectoryUrlBox.Text.Trim(); if (string.IsNullOrWhiteSpace(url)) url = _settings.ServerDirectoryUrl;
        if (string.IsNullOrWhiteSpace(url)) { DirectoryStatusText.Text = "No server directory configured. Add its HTTPS URL in Settings."; return; }
        DirectoryStatusText.Text = "Connecting to server directory...";
        try
        {
            var results = await _directoryClient.GetServersAsync(url); _servers.Clear();
            foreach (var server in results) _servers.Add(server);
            DirectoryStatusText.Text = results.Count == 0 ? "Directory connected, but it returned no servers." : $"Loaded {results.Count} listing(s). Live reachability is not independently verified.";
        }
        catch (Exception ex) { _servers.Clear(); DirectoryStatusText.Text = $"Could not load directory: {ex.Message}"; }
    }
    private void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        try { _gameLauncher.Launch(GamePathBox.Text); }
        catch (Exception ex) { MessageBox.Show($"Could not open Space Engineers.\n\n{ex.Message}\n\nMake sure Steam is installed and Space Engineers is in your library.", "Unable to launch game", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}

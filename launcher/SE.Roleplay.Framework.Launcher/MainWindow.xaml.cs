using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using SE.Roleplay.Framework.Core.Identity;
using SE.Roleplay.Framework.Core.Persistence;
using SE.Roleplay.Framework.Launcher.Models;
using SE.Roleplay.Framework.Launcher.Services;

namespace SE.Roleplay.Framework.Launcher;

public partial class MainWindow : Window
{
    private readonly GameLauncher _gameLauncher = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly GameInstallDetector _gameDetector = new();
    private readonly ServerDirectoryClient _directoryClient = new();
    private readonly SteamOpenIdService _steamSignIn = new();
    private readonly PlayerIdentityService _identityService = new();
    private PlayerProfileService? _profileService;
    private PlayerRecord? _playerProfile;
    private const int MaximumCharactersPerPlayer = 5;
    private readonly ObservableCollection<ServerListing> _servers = new();
    private readonly ObservableCollection<ServerListing> _featuredServers = new();
    private readonly ObservableCollection<ServerListing> _recentServers = new();
    private LauncherSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        ServerList.ItemsSource = _servers;
        HomeServerList.ItemsSource = _featuredServers;
        RecentServerList.ItemsSource = _recentServers;
        ShowPage(HomePage, "SE1 COMMUNITY", "Home", HomeNav);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = await _settingsStore.LoadAsync();
            DirectoryUrlBox.Text = _settings.ServerDirectoryUrl;
            GamePathBox.Text = _settings.GameExecutablePath;
            if (string.IsNullOrWhiteSpace(GamePathBox.Text) || !File.Exists(GamePathBox.Text))
            {
                var detected = _gameDetector.FindGameExecutable();
                if (detected is not null) GamePathBox.Text = detected;
            }

            _settings.RecentlyLaunchedServers ??= new List<ServerListing>();
            foreach (var server in _settings.RecentlyLaunchedServers.Take(10))
                _recentServers.Add(server);

            UpdateSteamStatus();
            if (!string.IsNullOrWhiteSpace(_settings.SteamId64))
                await LoadPlayerProfileAsync();
            UpdateHomeEmptyStates();
            UpdateGameStatus();

            if (!string.IsNullOrWhiteSpace(_settings.ServerDirectoryUrl))
                await RefreshServerDirectoryAsync();
        }
        catch (Exception ex)
        {
            FrameworkStatusText.Text = "Launcher loaded with a settings warning.";
            MessageBox.Show($"Some launcher settings could not be loaded.\n\n{ex.Message}", "SE1 Roleplay", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateSteamStatus()
    {
        var signedIn = !string.IsNullOrWhiteSpace(_settings.SteamId64);
        SteamAccountText.Text = signedIn ? $"Steam ID: {_settings.SteamId64}" : "Steam not connected";
        SteamSignInButton.Content = signedIn ? "SWITCH STEAM ACCOUNT" : "SIGN IN WITH STEAM";
    }

    private void UpdateHomeEmptyStates()
    {
        RecentEmptyText.Visibility = _recentServers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HomeDirectoryEmptyText.Visibility = _featuredServers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowPage(FrameworkElement page, string eyebrow, string title, Button active)
    {
        HomePage.Visibility = Visibility.Collapsed;
        ServersPage.Visibility = Visibility.Collapsed;
        CharactersPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        PageEyebrow.Text = eyebrow;
        PageTitle.Text = title;

        foreach (var button in new[] { HomeNav, ServersNav, CharactersNav, SettingsNav })
        {
            button.Background = button == active ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("AltBrush");
            button.Foreground = button == active
                ? new SolidColorBrush(Color.FromRgb(11, 23, 18))
                : (Brush)FindResource("TextBrush");
        }
    }

    private void UpdateGameStatus()
    {
        var path = GamePathBox.Text;
        var found = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        GameStatusText.Text = found ? "Game installation detected · Steam launch enabled" : "Launch through your Steam client";
    }

    private void HomeNav_Click(object sender, RoutedEventArgs e) =>
        ShowPage(HomePage, "SE1 COMMUNITY", "Home", HomeNav);

    private void ServersNav_Click(object sender, RoutedEventArgs e) =>
        ShowPage(ServersPage, "COMMUNITY DIRECTORY", "Browse servers", ServersNav);

    private void BrowseServers_Click(object sender, RoutedEventArgs e) =>
        ShowPage(ServersPage, "COMMUNITY DIRECTORY", "Browse servers", ServersNav);

    private void CharactersNav_Click(object sender, RoutedEventArgs e) =>
        ShowPage(CharactersPage, "PLAYER PROFILE", "Characters", CharactersNav);

    private void SettingsNav_Click(object sender, RoutedEventArgs e) =>
        ShowPage(SettingsPage, "PREFERENCES", "Settings", SettingsNav);

    private async void SteamSignIn_Click(object sender, RoutedEventArgs e)
    {
        SteamSignInButton.IsEnabled = false;
        SteamSignInButton.Content = "WAITING FOR STEAM...";
        try
        {
            var steamId = await _steamSignIn.SignInAsync();
            _settings.SteamId64 = steamId;
            await _settingsStore.SaveAsync(_settings);
            UpdateSteamStatus();
            FrameworkStatusText.Text = "Steam account verified.";
        }
        catch (OperationCanceledException)
        {
            FrameworkStatusText.Text = "Steam sign-in timed out or was cancelled.";
            MessageBox.Show("Steam sign-in was cancelled or timed out. Please try again.", "Steam sign-in", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            FrameworkStatusText.Text = "Steam sign-in could not be completed.";
            MessageBox.Show($"Could not verify your Steam account.\n\n{ex.Message}", "Steam sign-in failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SteamSignInButton.IsEnabled = true;
            UpdateSteamStatus();
            if (!string.IsNullOrWhiteSpace(_settings.SteamId64))
                await LoadPlayerProfileAsync();
        }
    }

    private async Task LoadPlayerProfileAsync()
    {
        if (!ulong.TryParse(_settings.SteamId64, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId) || steamId == 0)
        {
            CharacterProfileStatusText.Text = "Your Steam account ID is invalid. Sign in again to load your profile.";
            CreateCharacterButton.IsEnabled = false;
            return;
        }

        try
        {
            if (!_identityService.TryGet(steamId, out _))
                _identityService.Connect(steamId, "Steam Player");

            var profileDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SE1Roleplay",
                "PlayerProfiles");
            var store = new JsonPlayerDataStore(profileDirectory);
            _profileService = new PlayerProfileService(store, _identityService, MaximumCharactersPerPlayer);
            _playerProfile = await _profileService.LoadAsync(steamId, "Steam Player");
            CharacterProfileStatusText.Text = $"Profile loaded for Steam account {steamId}. Characters are saved locally on this PC.";
            CreateCharacterButton.IsEnabled = true;
            await RefreshCharactersAsync();
        }
        catch (Exception ex)
        {
            CharacterProfileStatusText.Text = $"Could not load your character profile: {ex.Message}";
            CreateCharacterButton.IsEnabled = false;
        }
    }

    private async Task RefreshCharactersAsync(Guid? selectCharacterId = null)
    {
        if (_profileService is null || _playerProfile is null)
            return;

        var characters = await _profileService.GetCharactersAsync(_playerProfile.PlatformId);
        CharacterList.ItemsSource = characters;
        CharacterCountText.Text = $"{characters.Count} / {MaximumCharactersPerPlayer}";
        CharacterEmptyText.Visibility = characters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CreateCharacterButton.IsEnabled = characters.Count < MaximumCharactersPerPlayer;
        if (selectCharacterId.HasValue)
            CharacterList.SelectedItem = characters.FirstOrDefault(c => c.CharacterId == selectCharacterId.Value);
        else if (_playerProfile.SelectedCharacterId.HasValue)
            CharacterList.SelectedItem = characters.FirstOrDefault(c => c.CharacterId == _playerProfile.SelectedCharacterId.Value);

        UpdateCharacterButtons();
    }

    private void UpdateCharacterButtons()
    {
        var hasSelection = CharacterList.SelectedItem is PlayerCharacter;
        SelectCharacterButton.IsEnabled = hasSelection;
        DeleteCharacterButton.IsEnabled = hasSelection;
    }

    private async void CreateCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_profileService is null || _playerProfile is null) return;
        try
        {
            var name = CharacterNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Enter a character name first.", "Character name required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var character = await _profileService.CreateCharacterAsync(
                _playerProfile.PlatformId, name, CharacterDescriptionBox.Text);
            _playerProfile.SelectedCharacterId = character.CharacterId;
            CharacterNameBox.Clear();
            CharacterDescriptionBox.Clear();
            CharacterProfileStatusText.Text = $"Created {character.Name}. Your profile has been saved.";
            await RefreshCharactersAsync(character.CharacterId);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create character: {ex.Message}", "Character creation failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SelectCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_profileService is null || _playerProfile is null || CharacterList.SelectedItem is not PlayerCharacter character) return;
        try
        {
            await _profileService.SelectCharacterAsync(_playerProfile.PlatformId, character.CharacterId);
            _playerProfile.SelectedCharacterId = character.CharacterId;
            CharacterProfileStatusText.Text = $"Active character: {character.Name}. This selection is saved to your profile.";
            await RefreshCharactersAsync(character.CharacterId);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not select character: {ex.Message}", "Character selection failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeleteCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_profileService is null || _playerProfile is null || CharacterList.SelectedItem is not PlayerCharacter character) return;
        var answer = MessageBox.Show(
            $"Delete the character '{character.Name}'? This cannot be undone.",
            "Delete character",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            await _profileService.DeleteCharacterAsync(_playerProfile.PlatformId, character.CharacterId);
            _playerProfile.SelectedCharacterId = _identityService.TryGet(_playerProfile.PlatformId, out var identity)
                ? identity.CharacterId : null;
            CharacterProfileStatusText.Text = $"Deleted {character.Name}.";
            await RefreshCharactersAsync(_playerProfile.SelectedCharacterId);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete character: {ex.Message}", "Character deletion failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CharacterList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateCharacterButtons();

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.ServerDirectoryUrl = DirectoryUrlBox.Text.Trim();
        _settings.GameExecutablePath = GamePathBox.Text.Trim();
        try
        {
            await _settingsStore.SaveAsync(_settings);
            SettingsStatusText.Text = "Settings saved.";
            UpdateGameStatus();
            await RefreshServerDirectoryAsync();
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = $"Could not save settings: {ex.Message}";
        }
    }

    private void DetectGame_Click(object sender, RoutedEventArgs e)
    {
        var path = _gameDetector.FindGameExecutable();
        if (path is null)
        {
            SettingsStatusText.Text = "Automatic detection did not find the default Steam library. Steam launch will still work when Steam is installed.";
            return;
        }

        GamePathBox.Text = path;
        UpdateGameStatus();
        SettingsStatusText.Text = "Installation detected. Save settings to remember this path.";
    }

    private async void RefreshServers_Click(object sender, RoutedEventArgs e) =>
        await RefreshServerDirectoryAsync();

    private async Task RefreshServerDirectoryAsync()
    {
        var url = DirectoryUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url)) url = _settings.ServerDirectoryUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            DirectoryStatusText.Text = "No server directory configured. Add the SE1 community directory HTTPS URL in Settings.";
            _servers.Clear();
            _featuredServers.Clear();
            UpdateHomeEmptyStates();
            return;
        }

        DirectoryStatusText.Text = "Connecting to community directory...";
        try
        {
            var results = await _directoryClient.GetServersAsync(url);
            _servers.Clear();
            _featuredServers.Clear();
            foreach (var server in results)
            {
                _servers.Add(server);
                if (_featuredServers.Count < 6) _featuredServers.Add(server);
            }

            DirectoryStatusText.Text = results.Count == 0
                ? "Directory connected, but no communities have been published yet."
                : $"Showing {results.Count} published server(s). Player counts depend on directory updates.";
            UpdateHomeEmptyStates();
        }
        catch (Exception ex)
        {
            _servers.Clear();
            _featuredServers.Clear();
            DirectoryStatusText.Text = $"Could not load directory: {ex.Message}";
            UpdateHomeEmptyStates();
        }
    }

    private void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _gameLauncher.Launch();
            FrameworkStatusText.Text = "Opening Space Engineers through Steam...";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open Space Engineers through Steam. Make sure the Steam client is installed and running.\n\n{ex.Message}",
                "Unable to launch Space Engineers", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void JoinServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ServerListing server }) return;
        try
        {
            _gameLauncher.JoinServer(server.Address, server.Port);
            _settings.RecentlyLaunchedServers.RemoveAll(s =>
                (!string.IsNullOrWhiteSpace(server.Id) && s.Id == server.Id) ||
                (string.Equals(s.Address, server.Address, StringComparison.OrdinalIgnoreCase) && s.Port == server.Port));
            _settings.RecentlyLaunchedServers.Insert(0, server);
            _settings.RecentlyLaunchedServers = _settings.RecentlyLaunchedServers.Take(10).ToList();

            _recentServers.Clear();
            foreach (var recent in _settings.RecentlyLaunchedServers) _recentServers.Add(recent);
            UpdateHomeEmptyStates();
            await _settingsStore.SaveAsync(_settings);
            FrameworkStatusText.Text = $"Sent join request for {server.Name} to Steam.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not join this server. Check that its address and port are correct.\n\n{ex.Message}",
                "Unable to join server", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

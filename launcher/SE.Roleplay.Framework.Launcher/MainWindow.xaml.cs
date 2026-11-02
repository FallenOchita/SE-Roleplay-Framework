using System.Net.Http;
using System.Collections.ObjectModel;
using System.IO;
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
    private readonly SteamOpenIdService _steamSignIn = new();
    private readonly DesktopAuthorizationService _directoryAuthorization = new();
    private readonly LauncherDirectoryApiClient _launcherApi;
    private readonly ObservableCollection<ServerListing> _servers = new();
    private readonly ObservableCollection<ServerListing> _visibleServers = new();
    private readonly ObservableCollection<ServerListing> _featuredServers = new();
    private readonly ObservableCollection<ServerListing> _recentServers = new();
    private readonly ObservableCollection<ServerListing> _ownedServers = new();
    private LauncherSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        _launcherApi = new LauncherDirectoryApiClient(_directoryAuthorization);
        OwnedServersList.ItemsSource = _ownedServers;
        ServerList.ItemsSource = _visibleServers;
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
            _settings.FavoriteServerIds ??= new List<string>();
            foreach (var server in _settings.RecentlyLaunchedServers.Take(10))
                _recentServers.Add(server);

            UpdateSteamStatus();
            UpdateHomeEmptyStates();
            UpdateGameStatus();

            if (!string.IsNullOrWhiteSpace(_settings.ProtectedDirectoryRefreshToken) &&
                !string.IsNullOrWhiteSpace(_settings.ServerDirectoryUrl))
            {
                try
                {
                    var refresh = RefreshTokenProtector.Unprotect(_settings.ProtectedDirectoryRefreshToken);
                    var pair = await _directoryAuthorization.RefreshAsync(_settings.ServerDirectoryUrl, refresh, _settings.SteamId64);
                    _settings.ProtectedDirectoryRefreshToken = RefreshTokenProtector.Protect(pair.RefreshToken);
                    await _settingsStore.SaveAsync(_settings);
                    UpdateSteamStatus();
                    FrameworkStatusText.Text = "Directory account session restored.";
                }
                catch (Exception ex)
                {
                    UpdateSteamStatus();
                    FrameworkStatusText.Text = $"Could not restore directory session; saved credentials were retained for retry. ({ex.Message})";
                }
            }

            if (_directoryAuthorization.HasAccessToken)
                await RefreshAccountDataAsync();

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
        var directorySignedIn = _directoryAuthorization.HasAccessToken;
        SteamAccountText.Text = signedIn
            ? $"Steam ID: {_settings.SteamId64}" + (directorySignedIn ? " · Directory connected" : " · Directory not connected")
            : "Steam not connected";
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
        ShowPage(CharactersPage, "PLAYER ACCOUNT", "Profile & server management", CharactersNav);

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

            var directoryUrl = DirectoryUrlBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(directoryUrl)) directoryUrl = _settings.ServerDirectoryUrl;
            if (!string.IsNullOrWhiteSpace(directoryUrl))
            {
                FrameworkStatusText.Text = "Complete the directory authorization in your browser...";
                var pair = await _directoryAuthorization.AuthorizeAsync(directoryUrl, steamId);
                _settings.ServerDirectoryUrl = directoryUrl;
                _settings.ProtectedDirectoryRefreshToken = RefreshTokenProtector.Protect(pair.RefreshToken);
                await _settingsStore.SaveAsync(_settings);
                FrameworkStatusText.Text = "Steam verified and directory account connected.";
                await RefreshAccountDataAsync();
            }
            else
            {
                FrameworkStatusText.Text = "Steam verified. Set the directory HTTPS URL in Settings to connect your player account.";
            }
        }
        catch (OperationCanceledException)
        {
            FrameworkStatusText.Text = "Steam or directory sign-in was cancelled or timed out.";
            MessageBox.Show("Sign-in was cancelled or timed out. Please try again.", "Steam sign-in", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            FrameworkStatusText.Text = "Account sign-in could not be completed.";
            MessageBox.Show($"Could not complete account sign-in.\n\n{ex.Message}", "Sign-in failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SteamSignInButton.IsEnabled = true;
            UpdateSteamStatus();
        }
    }

    private async void RefreshAccount_Click(object sender, RoutedEventArgs e) =>
        await RefreshAccountDataAsync();

    private async Task RefreshAccountDataAsync()
    {
        if (!_directoryAuthorization.HasAccessToken)
        {
            ProfileStatusText.Text = "Sign in with Steam and authorize the directory to load your profile.";
            OwnerStatusText.Text = "Directory account not connected.";
            _ownedServers.Clear();
            return;
        }

        var url = string.IsNullOrWhiteSpace(_settings.ServerDirectoryUrl)
            ? DirectoryUrlBox.Text.Trim() : _settings.ServerDirectoryUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            ProfileStatusText.Text = "Configure the directory HTTPS URL in Settings.";
            return;
        }

        ProfileStatusText.Text = "Loading player profile...";
        OwnerStatusText.Text = "Loading owned servers...";
        try
        {
            var profile = await _launcherApi.GetProfileAsync(url);
            ProfileSteamIdText.Text = profile.SteamId64;
            ProfileDisplayNameBox.Text = profile.DisplayName;
            ProfileBiographyBox.Text = profile.Biography;
            ProfileStatusText.Text = string.IsNullOrWhiteSpace(profile.DisplayName)
                ? "Profile found. Add a display name and save to create your player profile."
                : $"Profile updated {profile.UpdatedAtUtc.ToLocalTime():g}.";

            var servers = await _launcherApi.GetOwnedServersAsync(url);
            _ownedServers.Clear();
            foreach (var server in servers) _ownedServers.Add(server);
            OwnerStatusText.Text = servers.Count == 0
                ? "No servers are registered to this Steam account yet."
                : $"Loaded {servers.Count} owned server(s). Select one to rotate its API key or delete it.";
        }
        catch (HttpRequestException ex)
        {
            ProfileStatusText.Text = $"Directory request failed: {ex.Message}";
            OwnerStatusText.Text = "Could not load owned servers.";
        }
        catch (Exception ex)
        {
            ProfileStatusText.Text = $"Could not load account data: {ex.Message}";
            OwnerStatusText.Text = "Could not load owned servers.";
        }
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!_directoryAuthorization.HasAccessToken)
        {
            ProfileStatusText.Text = "Connect your directory account before saving your profile.";
            return;
        }

        try
        {
            var profile = await _launcherApi.SaveProfileAsync(
                _settings.ServerDirectoryUrl, ProfileDisplayNameBox.Text, ProfileBiographyBox.Text);
            ProfileSteamIdText.Text = profile.SteamId64;
            ProfileStatusText.Text = $"Profile saved at {profile.UpdatedAtUtc.ToLocalTime():g}.";
        }
        catch (Exception ex)
        {
            ProfileStatusText.Text = $"Profile could not be saved: {ex.Message}";
        }
    }

    private async void RegisterOwnedServer_Click(object sender, RoutedEventArgs e)
    {
        if (!_directoryAuthorization.HasAccessToken)
        {
            OwnerStatusText.Text = "Connect your directory account before registering a server.";
            return;
        }
        if (!int.TryParse(OwnerServerPortBox.Text, out var port) || port is < 1 or > 65535 ||
            !int.TryParse(OwnerServerMaxPlayersBox.Text, out var maxPlayers) || maxPlayers is < 1 or > 10000)
        {
            OwnerStatusText.Text = "Enter a valid port (1–65535) and max player count (1–10000).";
            return;
        }

        try
        {
            var request = new RegisterOwnedServerDto(
                OwnerServerNameBox.Text.Trim(),
                OwnerServerDescriptionBox.Text.Trim(),
                OwnerServerAddressBox.Text.Trim(),
                port,
                maxPlayers,
                OwnerServerRegionBox.Text.Trim(),
                string.Empty,
                string.Empty,
                string.Empty,
                OwnerServerTagsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(20).ToList());
            var created = await _launcherApi.CreateOwnedServerAsync(_settings.ServerDirectoryUrl, request);
            OwnerServerApiKeyBox.Text = created.ApiKey;
            OwnerStatusText.Text = $"{created.Name} registered. Copy the API key now; it is only shown once.";
            OwnerServerNameBox.Clear();
            OwnerServerDescriptionBox.Clear();
            OwnerServerAddressBox.Clear();
            OwnerServerRegionBox.Clear();
            OwnerServerTagsBox.Clear();
            await RefreshAccountDataAsync();
        }
        catch (Exception ex)
        {
            OwnerStatusText.Text = $"Server registration failed: {ex.Message}";
        }
    }

    private async void RotateServerKey_Click(object sender, RoutedEventArgs e)
    {
        if (OwnedServersList.SelectedItem is not ServerListing server)
        {
            OwnerStatusText.Text = "Select one of your servers first.";
            return;
        }
        if (MessageBox.Show($"Rotate the API key for {server.Name}? The old key will stop working immediately.",
            "Rotate server API key", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            OwnerServerApiKeyBox.Text = await _launcherApi.RotateServerKeyAsync(_settings.ServerDirectoryUrl, server.Id);
            OwnerStatusText.Text = $"API key rotated for {server.Name}. Update the server configuration immediately.";
        }
        catch (Exception ex)
        {
            OwnerStatusText.Text = $"Could not rotate API key: {ex.Message}";
        }
    }

    private async void DeleteOwnedServer_Click(object sender, RoutedEventArgs e)
    {
        if (OwnedServersList.SelectedItem is not ServerListing server)
        {
            OwnerStatusText.Text = "Select one of your servers first.";
            return;
        }
        if (MessageBox.Show($"Permanently remove {server.Name} from the public directory? This does not stop the game server.",
            "Delete directory listing", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            await _launcherApi.DeleteOwnedServerAsync(_settings.ServerDirectoryUrl, server.Id);
            OwnerServerApiKeyBox.Clear();
            OwnerStatusText.Text = $"{server.Name} removed from the directory.";
            await RefreshAccountDataAsync();
        }
        catch (Exception ex)
        {
            OwnerStatusText.Text = $"Could not delete server listing: {ex.Message}";
        }
    }

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
            _visibleServers.Clear();
            _featuredServers.Clear();
            ApplyServerFilters();
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
            ApplyServerFilters();
            UpdateHomeEmptyStates();
        }
        catch (Exception ex)
        {
            _servers.Clear();
            _visibleServers.Clear();
            _featuredServers.Clear();
            DirectoryStatusText.Text = $"Could not load directory: {ex.Message}";
            ApplyServerFilters();
            UpdateHomeEmptyStates();
        }
    }

    private void ServerSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyServerFilters();

    private void ServerStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyServerFilters();

    private void ApplyServerFilters()
    {
        if (ServerSearchBox == null || ServerStatusFilter == null || ServerResultsText == null || ServerResultsEmptyText == null)
            return;

        var query = ServerSearchBox.Text.Trim();
        var filter = (ServerStatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All servers";
        var matches = _servers.Where(server =>
        {
            var searchable = string.Join(" ", server.Name, server.Description, server.Region,
                string.Join(" ", server.Tags ?? new List<string>()));
            var textMatch = string.IsNullOrWhiteSpace(query) || searchable.Contains(query, StringComparison.OrdinalIgnoreCase);
            var favorite = !string.IsNullOrWhiteSpace(server.Id) && _settings.FavoriteServerIds.Contains(server.Id);
            var status = (server.Status ?? string.Empty).Trim();
            var statusMatch = filter switch
            {
                "Online" => status.Equals("Online", StringComparison.OrdinalIgnoreCase),
                "Offline" => status.Equals("Offline", StringComparison.OrdinalIgnoreCase),
                "Favorites" => favorite,
                _ => true
            };
            return textMatch && statusMatch;
        }).ToList();

        _visibleServers.Clear();
        foreach (var server in matches) _visibleServers.Add(server);
        ServerResultsText.Text = $"{matches.Count} COMMUNITIES";
        ServerResultsEmptyText.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ServerDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ServerListing server }) ShowServerDetails(server);
    }

    private void ShowServerDetails(ServerListing server)
    {
        SelectedServerNameText.Text = server.Name;
        SelectedServerDescriptionText.Text = string.IsNullOrWhiteSpace(server.Description) ? "No description provided." : server.Description;
        SelectedServerAddressText.Text = $"Address: {server.Address}:{server.Port}";
        SelectedServerRegionText.Text = $"Region: {(string.IsNullOrWhiteSpace(server.Region) ? "Not specified" : server.Region)}";
        SelectedServerPlayersText.Text = $"Players: {Math.Max(0, server.Players)} / {Math.Max(0, server.MaxPlayers)}";
        SelectedServerStatusText.Text = $"Status: {server.Status}";
        SelectedServerVersionText.Text = $"Version: {(string.IsNullOrWhiteSpace(server.Version) ? "Not specified" : server.Version)}";
        SelectedServerTagsText.Text = server.Tags == null || server.Tags.Count == 0 ? "No tags" : string.Join(" · ", server.Tags);
        SelectedServerJoinButton.Tag = server;
        SelectedServerJoinButton.IsEnabled = IsJoinable(server);
        SelectedServerFavoriteButton.Tag = server;
        SelectedServerFavoriteButton.IsEnabled = !string.IsNullOrWhiteSpace(server.Id);
        SelectedServerFavoriteButton.Content = IsFavorite(server) ? "★ FAVORITED" : "☆ FAVORITE";
    }

    private bool IsFavorite(ServerListing server) =>
        !string.IsNullOrWhiteSpace(server.Id) && _settings.FavoriteServerIds.Contains(server.Id);

    private static bool IsJoinable(ServerListing server) =>
        !string.IsNullOrWhiteSpace(server.Address) && server.Port is >= 1 and <= 65535;

    private async void FavoriteServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ServerListing server }) await ToggleFavoriteAsync(server);
    }

    private async void SelectedServerFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedServerFavoriteButton.Tag is ServerListing server) await ToggleFavoriteAsync(server);
    }

    private async Task ToggleFavoriteAsync(ServerListing server)
    {
        if (string.IsNullOrWhiteSpace(server.Id)) return;
        if (IsFavorite(server)) _settings.FavoriteServerIds.Remove(server.Id);
        else _settings.FavoriteServerIds.Add(server.Id);
        _settings.FavoriteServerIds = _settings.FavoriteServerIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await _settingsStore.SaveAsync(_settings);
        ApplyServerFilters();
        if (SelectedServerFavoriteButton.Tag is ServerListing selected && selected.Id == server.Id)
            SelectedServerFavoriteButton.Content = IsFavorite(server) ? "★ FAVORITED" : "☆ FAVORITE";
        FrameworkStatusText.Text = IsFavorite(server) ? $"Saved {server.Name} to favorites." : $"Removed {server.Name} from favorites.";
    }

    private async void SelectedServerJoin_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedServerJoinButton.Tag is ServerListing server)
            await JoinServerAsync(server);
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
        await JoinServerAsync(server);
    }

    private async Task JoinServerAsync(ServerListing server)
    {
        if (!IsJoinable(server))
        {
            MessageBox.Show("This directory entry does not contain a valid server address and port.", "Invalid server listing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

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

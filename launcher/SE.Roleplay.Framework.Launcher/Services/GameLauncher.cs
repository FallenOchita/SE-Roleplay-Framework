using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace SE.Roleplay.Framework.Launcher.Services;

public sealed class GameLauncher
{
    private const string SteamLaunchUri = "steam://run/244850";

    public void Launch(string? executablePath = null)
    {
        // Always start through Steam so Steam authentication, updates and overlays are used.
        Process.Start(new ProcessStartInfo { FileName = SteamLaunchUri, UseShellExecute = true });
    }

    public void JoinServer(string address, int port)
    {
        var host = address.Trim();
        if (!IPAddress.TryParse(host, out _) && host.Contains(':') &&
            Uri.TryCreate("tcp://" + host, UriKind.Absolute, out var endpointWithPort) &&
            endpointWithPort.Port > 0)
        {
            if (port == 0) port = endpointWithPort.Port;
            host = endpointWithPort.Host;
        }

        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "The server must provide a valid connection port.");

        if (IPAddress.TryParse(host, out var ip))
        {
            if (ip.AddressFamily == AddressFamily.InterNetworkV6) host = $"[{ip}]";
        }
        else if (Uri.CheckHostName(host) != UriHostNameType.Dns ||
                 host.Length > 253 || host.Contains('/') || host.Contains('\\') || host.Contains(':'))
        {
            throw new InvalidOperationException("This server has an invalid address.");
        }

        var target = $"steam://connect/{host}:{port}";
        Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
    }
}

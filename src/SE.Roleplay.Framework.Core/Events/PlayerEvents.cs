namespace SE.Roleplay.Framework.Core.Events
{
    public sealed class PlayerConnected
    {
        public PlayerConnected(ulong platformId, string displayName) { PlatformId = platformId; DisplayName = displayName; }
        public ulong PlatformId { get; }
        public string DisplayName { get; }
    }

    public sealed class PlayerDisconnected
    {
        public PlayerDisconnected(ulong platformId) { PlatformId = platformId; }
        public ulong PlatformId { get; }
    }
}

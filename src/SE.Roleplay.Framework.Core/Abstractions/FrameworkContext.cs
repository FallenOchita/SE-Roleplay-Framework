using System;
using SE.Roleplay.Framework.Core.Events;
using SE.Roleplay.Framework.Core.Identity;
using SE.Roleplay.Framework.Core.Permissions;
using SE.Roleplay.Framework.Core.Persistence;

namespace SE.Roleplay.Framework.Core.Abstractions
{
    /// <summary>Default immutable service container for framework modules.</summary>
    public sealed class FrameworkContext : IFrameworkContext
    {
        public FrameworkContext(
            IEventBus events,
            IPlayerIdentityService players,
            IPermissionService permissions,
            IPlayerDataStore playerData,
            FrameworkOptions options,
            IFrameworkLogger logger)
        {
            Events = events ?? throw new ArgumentNullException(nameof(events));
            Players = players ?? throw new ArgumentNullException(nameof(players));
            Permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
            PlayerData = playerData ?? throw new ArgumentNullException(nameof(playerData));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IEventBus Events { get; }
        public IPlayerIdentityService Players { get; }
        public IPermissionService Permissions { get; }
        public IPlayerDataStore PlayerData { get; }
        public FrameworkOptions Options { get; }
        public IFrameworkLogger Logger { get; }
    }
}

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
        /// <summary>
        /// Initializes a framework context with the services required by framework modules.
        /// </summary>
        /// <param name="events">The framework event bus.</param>
        /// <param name="players">The player identity service.</param>
        /// <param name="sessions">The player session service.</param>
        /// <param name="profiles">The persistent player profile service.</param>
        /// <param name="permissions">The player permission service.</param>
        /// <param name="playerData">The persistent player data store.</param>
        /// <param name="options">The framework configuration options.</param>
        /// <param name="logger">The framework logger.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required service or options object is null.</exception>
        public FrameworkContext(
            IEventBus events,
            IPlayerIdentityService players,
            PlayerSessionService sessions,
            IPlayerProfileService profiles,
            IPermissionService permissions,
            IPlayerDataStore playerData,
            FrameworkOptions options,
            IFrameworkLogger logger)
        {
            Events = events ?? throw new ArgumentNullException(nameof(events));
            Players = players ?? throw new ArgumentNullException(nameof(players));
            Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            Profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            Permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
            PlayerData = playerData ?? throw new ArgumentNullException(nameof(playerData));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public IEventBus Events { get; }

        /// <inheritdoc />
        public IPlayerIdentityService Players { get; }

        /// <inheritdoc />
        public PlayerSessionService Sessions { get; }

        /// <inheritdoc />
        public IPlayerProfileService Profiles { get; }

        /// <inheritdoc />
        public IPermissionService Permissions { get; }

        /// <inheritdoc />
        public IPlayerDataStore PlayerData { get; }

        /// <inheritdoc />
        public FrameworkOptions Options { get; }

        /// <inheritdoc />
        public IFrameworkLogger Logger { get; }
    }
}

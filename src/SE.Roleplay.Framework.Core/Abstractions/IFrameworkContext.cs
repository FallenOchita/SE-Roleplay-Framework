using SE.Roleplay.Framework.Core.Events;
using SE.Roleplay.Framework.Core.Identity;
using SE.Roleplay.Framework.Core.Permissions;
using SE.Roleplay.Framework.Core.Persistence;

namespace SE.Roleplay.Framework.Core.Abstractions
{
    /// <summary>Services exposed to framework modules, independent of the game host.</summary>
    public interface IFrameworkContext
    {
        IEventBus Events { get; }
        IPlayerIdentityService Players { get; }
        IPermissionService Permissions { get; }
        IPlayerDataStore PlayerData { get; }
        FrameworkOptions Options { get; }
        IFrameworkLogger Logger { get; }
    }

    public interface IFrameworkLogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message, System.Exception? exception = null);
    }
}

using System;

namespace SE.Roleplay.Framework.Core.Events
{
    public interface IEventBus
    {
        IDisposable Subscribe<TEvent>(Action<TEvent> handler);
        void Publish<TEvent>(TEvent eventData);
    }
}

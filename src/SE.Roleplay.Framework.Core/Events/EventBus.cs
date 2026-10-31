using System;
using System.Collections.Generic;
using System.Linq;

namespace SE.Roleplay.Framework.Core.Events
{
    /// <summary>Synchronous typed event bus. Handlers should remain short and must not block game threads.</summary>
    public sealed class EventBus : IEventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var type = typeof(TEvent);
            if (!_handlers.TryGetValue(type, out var handlers))
                _handlers[type] = handlers = new List<Delegate>();
            handlers.Add(handler);
            return new Subscription(() => handlers.Remove(handler));
        }

        public void Publish<TEvent>(TEvent eventData)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var handlers)) return;
            foreach (var handler in handlers.ToArray())
                ((Action<TEvent>)handler)(eventData);
        }

        private sealed class Subscription : IDisposable
        {
            private Action? _dispose;
            public Subscription(Action dispose) => _dispose = dispose;
            public void Dispose()
            {
                _dispose?.Invoke();
                _dispose = null;
            }
        }
    }
}

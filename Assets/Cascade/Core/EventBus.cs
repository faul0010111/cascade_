using System;
using System.Collections.Generic;

namespace Cascade.Core
{
    /// <summary>
    /// Typed event bus. Events are structs. Immediate dispatch with Publish, deferred dispatch
    /// (next FlushDeferred) with Enqueue. Agents do not subscribe individually to global events:
    /// runtime routers subscribe once and deliver to agents in range.
    /// </summary>
    public sealed class EventBus
    {
        private readonly Dictionary<Type, Delegate> _handlers = new Dictionary<Type, Delegate>();
        private readonly Queue<Action> _deferred = new Queue<Action>();

        public void Subscribe<T>(Action<T> handler) where T : struct
        {
            Delegate existing;
            _handlers.TryGetValue(typeof(T), out existing);
            _handlers[typeof(T)] = Delegate.Combine(existing, handler);
        }

        public void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            Delegate existing;
            if (!_handlers.TryGetValue(typeof(T), out existing)) return;
            var result = Delegate.Remove(existing, handler);
            if (result == null) _handlers.Remove(typeof(T));
            else _handlers[typeof(T)] = result;
        }

        public void Publish<T>(T evt) where T : struct
        {
            Delegate d;
            if (_handlers.TryGetValue(typeof(T), out d)) ((Action<T>)d)(evt);
        }

        public void Enqueue<T>(T evt) where T : struct
        {
            _deferred.Enqueue(() => Publish(evt));
        }

        public void FlushDeferred()
        {
            int count = _deferred.Count; // events enqueued during the flush run next time
            while (count-- > 0) _deferred.Dequeue()();
        }

        public void Clear()
        {
            _handlers.Clear();
            _deferred.Clear();
        }
    }
}

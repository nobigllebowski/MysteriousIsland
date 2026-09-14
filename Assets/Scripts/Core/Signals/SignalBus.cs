// Ported from NATION: WORLD ORDER (nobigllebowski/MobileGame), Assets/Scripts/Core/Signals/SignalBus.cs.
// Adapted for Vardholm: namespace changed to ForgottenIsle.Core.Signals; XML documentation expanded.
// The copy-on-write handler array and the publish-time snapshot are kept byte-for-byte in behaviour.

using System;
using System.Collections.Generic;

namespace ForgottenIsle.Core.Signals
{
    /// <summary>
    /// Typed publish/subscribe bus.
    /// </summary>
    /// <remarks>
    /// WHY the copy-on-write handler array rather than a <c>List&lt;T&gt;</c>: publishing must allocate
    /// nothing, because signals fire every tick on a mobile target where a per-frame allocation shows up
    /// as a GC spike. Handlers live in a plain array that is rebuilt only on subscribe/unsubscribe — a
    /// cost paid at screen-open time, not in the hot loop. <see cref="SubscriptionList{TSignal}.Invoke"/>
    /// snapshots the array reference before iterating, so a handler may unsubscribe itself (or anyone
    /// else) mid-publish without a "collection modified" throw; the removed handler still receives the
    /// signal currently in flight, which is the safe half of the trade.
    /// <para>
    /// WHY generic dispatch on <c>typeof(TSignal)</c>: the lookup is by the STATIC type argument, so
    /// publishing a signal as a base type will not reach subscribers of the derived type. Always publish
    /// with the concrete signal type.
    /// </para>
    /// <para>
    /// NOT thread-safe. The bus belongs to the main thread.
    /// </para>
    /// </remarks>
    public sealed class SignalBus
    {
        private readonly Dictionary<Type, ISubscriptionList> _lists = new Dictionary<Type, ISubscriptionList>();

        /// <summary>
        /// Registers <paramref name="handler"/> for signals of type <typeparamref name="TSignal"/>.
        /// </summary>
        /// <returns>
        /// A token that unsubscribes when disposed. Dispose is idempotent. Callers that outlive the bus
        /// MUST dispose, or the handler — and everything its closure captures — leaks for the process
        /// lifetime. UI screens dispose theirs when they leave the stack.
        /// </returns>
        public IDisposable Subscribe<TSignal>(Action<TSignal> handler) where TSignal : ISignal
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var list = GetOrCreateList<TSignal>();
            list.Add(handler);
            return new Subscription<TSignal>(list, handler);
        }

        /// <summary>
        /// Delivers <paramref name="signal"/> to every current subscriber, in subscription order.
        /// </summary>
        /// <remarks>
        /// A throwing handler aborts the publish and the remaining handlers never see the signal, by
        /// design: a handler that throws has a bug, and swallowing it here would hide the bug while
        /// leaving the game in a half-notified state that is far harder to diagnose.
        /// </remarks>
        public void Publish<TSignal>(TSignal signal) where TSignal : ISignal
        {
            if (_lists.TryGetValue(typeof(TSignal), out var list))
            {
                ((SubscriptionList<TSignal>)list).Invoke(signal);
            }
        }

        /// <summary>How many handlers are currently registered. Exists for leak assertions in tests.</summary>
        public int SubscriberCount<TSignal>() where TSignal : ISignal
        {
            return _lists.TryGetValue(typeof(TSignal), out var list) ? list.Count : 0;
        }

        private SubscriptionList<TSignal> GetOrCreateList<TSignal>() where TSignal : ISignal
        {
            if (!_lists.TryGetValue(typeof(TSignal), out var list))
            {
                list = new SubscriptionList<TSignal>();
                _lists.Add(typeof(TSignal), list);
            }

            return (SubscriptionList<TSignal>)list;
        }

        /// <summary>Non-generic façade so lists of differing signal types can share one dictionary.</summary>
        private interface ISubscriptionList
        {
            int Count { get; }
        }

        private sealed class SubscriptionList<TSignal> : ISubscriptionList where TSignal : ISignal
        {
            private static readonly Action<TSignal>[] Empty = new Action<TSignal>[0];

            private Action<TSignal>[] _handlers = Empty;

            public int Count => _handlers.Length;

            public void Add(Action<TSignal> handler)
            {
                var next = new Action<TSignal>[_handlers.Length + 1];
                Array.Copy(_handlers, next, _handlers.Length);
                next[_handlers.Length] = handler;
                _handlers = next;
            }

            public void Remove(Action<TSignal> handler)
            {
                var index = Array.IndexOf(_handlers, handler);
                if (index < 0)
                {
                    return;
                }

                if (_handlers.Length == 1)
                {
                    _handlers = Empty;
                    return;
                }

                var next = new Action<TSignal>[_handlers.Length - 1];
                Array.Copy(_handlers, 0, next, 0, index);
                Array.Copy(_handlers, index + 1, next, index, _handlers.Length - index - 1);
                _handlers = next;
            }

            public void Invoke(TSignal signal)
            {
                // Snapshot the reference, not the contents: Add/Remove swap in a whole new array, so this
                // local keeps iterating the set of handlers that existed when the publish began.
                var handlers = _handlers;
                for (var i = 0; i < handlers.Length; i++)
                {
                    handlers[i](signal);
                }
            }
        }

        private sealed class Subscription<TSignal> : IDisposable where TSignal : ISignal
        {
            private SubscriptionList<TSignal> _list;
            private Action<TSignal> _handler;

            public Subscription(SubscriptionList<TSignal> list, Action<TSignal> handler)
            {
                _list = list;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_list == null)
                {
                    return;
                }

                _list.Remove(_handler);

                // Null both fields so a disposed token stops rooting the handler's closure.
                _list = null;
                _handler = null;
            }
        }
    }
}

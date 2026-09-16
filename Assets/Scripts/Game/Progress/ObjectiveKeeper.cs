using System;
using System.Collections.Generic;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Game.Fire;
using ForgottenIsle.Game.Items;
using ForgottenIsle.Game.Radio;

namespace ForgottenIsle.Game.Progress
{
    /// <summary>
    /// Keeps the objective line in step with the radio and the fire.
    /// </summary>
    /// <remarks>
    /// The objective is derived (ADR-0015) from progression and, since the puzzles arrived, from
    /// two services progression cannot see. This is the one place that reads them: on every
    /// radio or fire change it restates the facts to <see cref="ProgressService"/>, which
    /// republishes only if the line changed. Also on entering the world, because a restored run
    /// must show the line its state implies before anything happens.
    /// </remarks>
    public sealed class ObjectiveKeeper : IDisposable
    {
        private readonly ProgressService _progress;
        private readonly RadioService _radio;
        private readonly FireService _fire;
        private readonly InventoryService _inventory;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>(4);

        /// <param name="progress">Owner of the objective line.</param>
        /// <param name="radio">Read for found, working, heard. Null tolerated.</param>
        /// <param name="fire">Read for engaged, lit. Null tolerated.</param>
        /// <param name="signals">Bus the changes arrive on. Null makes this inert.</param>
        /// <param name="inventory">Read for the kit. Null means "the kit is in hand".</param>
        public ObjectiveKeeper(ProgressService progress, RadioService radio, FireService fire, SignalBus signals, InventoryService inventory = null)
        {
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _radio = radio;
            _fire = fire;
            _inventory = inventory;

            if (signals == null)
            {
                return;
            }

            _subscriptions.Add(signals.Subscribe<RadioChangedSignal>(_ => Refresh()));
            _subscriptions.Add(signals.Subscribe<FireChangedSignal>(_ => Refresh()));
            _subscriptions.Add(signals.Subscribe<GameStateChangedSignal>(_ => Refresh()));
            _subscriptions.Add(signals.Subscribe<InventoryChangedSignal>(_ => Refresh()));

            // Stated once at construction, so the line is right before any signal arrives.
            Refresh();
        }

        /// <summary>Restates the facts now. Cheap; a no-op when nothing changed.</summary>
        public void Refresh()
        {
            _progress.SetObjectiveFacts(Facts());
        }

        /// <inheritdoc />
        public void Dispose()
        {
            for (var i = 0; i < _subscriptions.Count; i++)
            {
                _subscriptions[i]?.Dispose();
            }

            _subscriptions.Clear();
        }

        private ObjectiveFacts Facts()
        {
            var engaged = false;
            if (_fire != null)
            {
                engaged = _fire.Sparked;
                for (var i = 0; !engaged && i < _fire.Sites.Count; i++)
                {
                    engaged = _fire.Sites[i].HasKit || _fire.Sites[i].PanelPlaced;
                }
            }

            return new ObjectiveFacts(
                _radio != null && _radio.IsFound,
                _radio != null && _radio.IsWorking,
                _radio != null && _radio.TransmissionReceived,
                engaged,
                _fire != null && _fire.IsLit,
                _inventory != null && !_inventory.Has(ItemIds.Multitool));
        }
    }
}

using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Items;
using ForgottenIsle.Core.Progress;
using ForgottenIsle.Core.Radio;
using ForgottenIsle.Game.Radio;

namespace ForgottenIsle.Game.Interaction
{
    /// <summary>
    /// The 1970s marine set on the crate inside the trawler hull. The prologue's spine.
    /// </summary>
    /// <remarks>
    /// A <see cref="Mechanism"/> with three faults instead of one part, and an instrument once it
    /// works. Its prompt says what reaching for it will do: EXAMINE while it is broken and the
    /// player holds nothing that fits, USE when they hold something that does, TUNE once it works.
    /// <para>
    /// It holds no state of its own. The <see cref="RadioService"/> does, because the radio's
    /// state is saved and a scene object is rebuilt on every entry. The set is the service's
    /// presence in the world, and nothing more.
    /// </para>
    /// </remarks>
    public sealed class RadioSet : Interactable
    {
        // The torch before the recorder when both are carried, so the prologue's unflagged
        // decision is made by the player choosing to use the recorder, not by a lookup order
        // spending it for them.
        private static readonly string[] Candidates =
        {
            ItemIds.DeadTorch, ItemIds.CopperSpring, ItemIds.Multitool, ItemIds.FieldRecorder
        };

        private RadioService _radio;
        private string _nameKey;
        private bool _hasPart;

        /// <summary>Configures the set. Called by zone building; there is no Inspector pass.</summary>
        /// <param name="radio">The service that owns the state.</param>
        /// <param name="nameKey">Localization key naming it in the prompt.</param>
        public void Configure(RadioService radio, string nameKey)
        {
            _radio = radio;
            _nameKey = nameKey;
        }

        /// <inheritdoc />
        public override string ContentId => ContentIds.RadioSet;

        /// <inheritdoc />
        public override string NameKey => _nameKey ?? string.Empty;

        /// <inheritdoc />
        public override string PromptKey
        {
            get
            {
                if (_radio != null && _radio.IsWorking)
                {
                    return "interact.tune";
                }

                return _hasPart ? "interact.use" : "interact.examine";
            }
        }

        /// <inheritdoc />
        public override bool CanInteract(IInteractionServices services)
        {
            // Latched here so PromptKey stays a pure property; the prompt is read every frame.
            _hasPart = _radio != null && !_radio.IsWorking && FittingItem(services) != null;
            return true;
        }

        /// <inheritdoc />
        public override ICommand BuildCommand(IInteractionServices services)
        {
            if (_radio != null && !_radio.IsWorking)
            {
                var item = FittingItem(services);
                if (item != null)
                {
                    return new UseItemCommand(item, ContentId);
                }
            }

            return new OpenRadioCommand();
        }

        /// <inheritdoc />
        public override UseOutcome Use(string itemId, IInteractionServices services)
        {
            return _radio != null ? _radio.Apply(itemId) : UseOutcome.Nothing;
        }

        /// <inheritdoc />
        public override void OnInteracted(IInteractionServices services)
        {
            // A fixture. It stays.
        }

        /// <inheritdoc />
        public override void ApplyRestoredState(IInteractionServices services)
        {
            // The service restored itself; the set has nothing of its own to restore.
        }

        /// <summary>The first carried item that fixes an outstanding fault, or null.</summary>
        private string FittingItem(IInteractionServices services)
        {
            if (services == null || _radio == null)
            {
                return null;
            }

            var outstanding = _radio.Repair.Outstanding;
            for (var i = 0; i < Candidates.Length; i++)
            {
                var fault = RadioRepair.FaultAddressedBy(Candidates[i]);
                if ((outstanding & fault) != 0 && services.HasItem(Candidates[i]))
                {
                    return Candidates[i];
                }
            }

            return null;
        }
    }
}

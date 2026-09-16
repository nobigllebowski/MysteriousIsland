using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Progress;
using UnityEngine;

namespace ForgottenIsle.Game.Interaction
{
    /// <summary>
    /// An observation made by standing in the right place and looking the right way. No prompt.
    /// </summary>
    /// <remarks>
    /// The game's foundational observation verb (<c>design/04-first-30-minutes.md</c> §2:40):
    /// the player walks to the bow of the nearest hull, turns, and looks down the beach. When the
    /// camera's yaw is within tolerance of the true axis a thin chalk line snaps in along it — a
    /// thought, not a UI element — and holds while the angle holds. The first time it holds, the
    /// observation is recorded. The camera is an instrument, and where you stand is a puzzle input.
    /// <para>
    /// Passive: it never becomes the prompt's target and never asks for a press. The interaction
    /// system observes it every tick and dispatches its command the first time it aligns. The
    /// line is a <see cref="LineRenderer"/> child named "Chalk", toggled here; the record is
    /// progression, made through the same inspect command a standing stone uses.
    /// </para>
    /// </remarks>
    public sealed class Sightline : Interactable
    {
        private string _contentId;
        private string _nameKey;
        private Vector3 _standAt;
        private float _axisYaw;
        private float _standRadius;
        private float _toleranceDegrees;
        private LineRenderer _chalk;
        private bool _aligned;

        /// <summary>Configures the sightline. Called by zone building; there is no Inspector pass.</summary>
        /// <param name="contentId">A <c>ContentIds</c> marker id.</param>
        /// <param name="nameKey">Localization key naming it (in the notebook; there is no prompt).</param>
        /// <param name="standAt">Where the player must stand: the bow of the nearest hull.</param>
        /// <param name="axis">Direction along the line, from the standing point.</param>
        /// <param name="standRadius">How far from the standing point still counts.</param>
        /// <param name="toleranceDegrees">Half-angle within which the line snaps in.</param>
        public void Configure(
            string contentId, string nameKey, Vector3 standAt, Vector3 axis, float standRadius, float toleranceDegrees)
        {
            _contentId = contentId;
            _nameKey = nameKey;
            _standAt = standAt;
            _axisYaw = SightlineMath.YawOf(axis.x, axis.z);
            _standRadius = standRadius;
            _toleranceDegrees = toleranceDegrees;
            _chalk = GetComponentInChildren<LineRenderer>(true);
            if (_chalk != null)
            {
                _chalk.enabled = false;
            }
        }

        /// <inheritdoc />
        public override string ContentId => _contentId ?? string.Empty;

        /// <inheritdoc />
        public override string NameKey => _nameKey ?? string.Empty;

        /// <inheritdoc />
        public override string PromptKey => string.Empty;

        /// <inheritdoc />
        public override bool IsPassive => true;

        /// <summary>True while the player is standing right and looking right.</summary>
        public bool IsAligned => _aligned;

        /// <inheritdoc />
        public override bool CanInteract(IInteractionServices services)
        {
            // Never offered as a prompt target.
            return false;
        }

        /// <inheritdoc />
        public override ICommand BuildCommand(IInteractionServices services)
        {
            return new InspectCommand(ContentId);
        }

        /// <inheritdoc />
        public override ICommand Observe(Vector3 playerPosition, float yawDegrees, IInteractionServices services)
        {
            var flat = playerPosition - _standAt;
            flat.y = 0f;
            var standing = flat.sqrMagnitude <= _standRadius * _standRadius;
            var aligned = standing && SightlineMath.IsAligned(yawDegrees, _axisYaw, _toleranceDegrees);

            if (aligned != _aligned)
            {
                _aligned = aligned;
                if (_chalk != null)
                {
                    _chalk.enabled = aligned;
                }
            }

            if (!aligned || (services != null && services.HasInspected(ContentId)))
            {
                return null;
            }

            return new InspectCommand(ContentId);
        }
    }
}

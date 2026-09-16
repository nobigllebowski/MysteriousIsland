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
    /// <para>
    /// A sightline can instead be a <b>hint</b> (<see cref="ConfigureAsHint"/>): aligned from the
    /// wrong hull, a shorter chalk snaps in through three hulls and Nadia says "try the far end".
    /// The chalk behaves exactly as the real one does, because the reward for the attempt is
    /// seeing the idea work; the words go through <see cref="RemarkCommand"/> so nothing is
    /// credited, once per zone visit, and never once the full line has been recorded.
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
        private bool _refused;
        private bool _hint;
        private string _fullLineId;
        private bool _remarked;

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

        /// <summary>
        /// Configures the sightline as a hint for another: the same stance and look, but the
        /// answer is a remark, not a record.
        /// </summary>
        /// <param name="remarkId">A <c>ContentIds</c> remark id; the line said when it aligns.</param>
        /// <param name="nameKey">Localization key naming it.</param>
        /// <param name="fullLineId">The marker this hints at. Once recorded, the hint is silent.</param>
        /// <param name="standAt">Where the player must stand.</param>
        /// <param name="axis">Direction along the partial line, from the standing point.</param>
        /// <param name="standRadius">How far from the standing point still counts.</param>
        /// <param name="toleranceDegrees">Half-angle within which the chalk snaps in.</param>
        public void ConfigureAsHint(
            string remarkId, string nameKey, string fullLineId, Vector3 standAt, Vector3 axis, float standRadius, float toleranceDegrees)
        {
            Configure(remarkId, nameKey, standAt, axis, standRadius, toleranceDegrees);
            _hint = true;
            _fullLineId = fullLineId;
        }

        /// <inheritdoc />
        public override string ContentId => _contentId ?? string.Empty;

        /// <summary>True when this sightline hints at another rather than recording itself.</summary>
        public bool IsHint => _hint;

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
            return _hint ? (ICommand)new RemarkCommand(ContentId) : new InspectCommand(ContentId);
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
                if (!aligned)
                {
                    _refused = false;
                }

                if (_chalk != null)
                {
                    _chalk.enabled = aligned;
                }
            }

            if (!aligned || _refused)
            {
                return null;
            }

            if (_hint)
            {
                // Said once per zone visit, and not at all once the real line is in the notebook:
                // "try the far end" to someone who has been there is nagging.
                if (_remarked || (services != null && services.HasInspected(_fullLineId)))
                {
                    return null;
                }

                return new RemarkCommand(ContentId);
            }

            if (services != null && services.HasInspected(ContentId))
            {
                return null;
            }

            return new InspectCommand(ContentId);
        }

        /// <inheritdoc />
        public override void OnInteracted(IInteractionServices services)
        {
            _remarked = true;
        }

        /// <inheritdoc />
        public override void OnObservationRefused(Core.Primitives.ResultCode code)
        {
            // Latched until the player looks away and back: a refusal is a state the game is in,
            // not a thing to retry sixty times a second.
            _refused = true;
        }
    }
}

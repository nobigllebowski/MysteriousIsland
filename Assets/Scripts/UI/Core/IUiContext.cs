using ForgottenIsle.Core.Localization;
using ForgottenIsle.Core.Logging;

namespace ForgottenIsle.UI.Core
{
    /// <summary>
    /// Everything a screen is allowed to see: text lookup and a diagnostics sink. Nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY this interface exists: ADR-0002 says the UI never mutates game state, and ADR-0014 says that
    /// guarantee should be structural rather than a rule people remember. Nation handed every screen its
    /// <c>GameContext</c>, which meant a screen could reach the session, the economy or the scene loader
    /// and write to them; only code review stood between that and a shipped build. A Vardholm screen is
    /// constructed with an <see cref="IUiContext"/>, so there is no reference to reach through — mutation
    /// is not forbidden, it is unavailable.
    /// </para>
    /// <para>
    /// WHY screens must not widen it: the moment a screen needs something else, the answer is a controller
    /// (see <c>ForgottenIsle.UI.Controllers</c>), which turns the interaction into a command. Adding a
    /// member here to save a controller is how the type-level guarantee is lost, one member at a time.
    /// </para>
    /// <para>
    /// The only implementation is private to <see cref="UIService"/>, so a screen cannot cast its way back
    /// out to the service that built it.
    /// </para>
    /// </remarks>
    public interface IUiContext
    {
        /// <summary>The only legitimate source of player-visible text. Never returns null or empty.</summary>
        ILocalizedText Loc { get; }

        /// <summary>Diagnostics sink for UI-side problems, e.g. a missing save slot a screen was asked to render.</summary>
        ICoreLog Log { get; }
    }
}

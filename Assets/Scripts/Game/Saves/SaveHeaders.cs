using ForgottenIsle.Core.Save;
using ForgottenIsle.Game.Scenes;
using ForgottenIsle.Game.Session;

namespace ForgottenIsle.Game.Saves
{
    /// <summary>
    /// The one place a save header is built from a session.
    /// </summary>
    /// <remarks>
    /// The manual save and the autosave used to build it separately, field by field, and the two
    /// would have drifted the first time a field was added. The slot service stamps the rest
    /// (slot, time, version) over a copy of what this returns.
    /// </remarks>
    public static class SaveHeaders
    {
        /// <summary>A header describing the run <paramref name="session"/> holds.</summary>
        public static SaveMetadata Build(SessionService session)
        {
            var header = new SaveMetadata();
            if (session == null)
            {
                return header;
            }

            header.ActId = session.ActId;
            header.ZoneId = session.ZoneId;
            header.ZoneDisplayKey = SceneKeys.ZoneDisplayKey(session.ZoneId);
            header.PlaytimeSeconds = session.PlaytimeSeconds;
            header.RecordedPercent = session.RecordedPercent;
            return header;
        }
    }
}

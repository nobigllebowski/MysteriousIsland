using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;

namespace ForgottenIsle.Core.Save
{
    /// <summary>
    /// One step in the migration chain: upgrades a document from <see cref="FromVersion"/> to
    /// <see cref="FromVersion"/> + 1, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Single-step by contract. A migration that jumps two versions cannot be composed with one
    /// written later, and the first time two features need migrating in the same release the chain
    /// stops being a chain. Each step transforms section payloads only — it must not touch
    /// <see cref="SaveDocument.SchemaVersion"/>, because <see cref="SaveMigrator"/> owns the bump
    /// and that ownership is what guarantees the loop terminates.
    /// </para>
    /// <para>
    /// Internal, not public: migrations are an implementation detail of Core's save layer, and
    /// exposing the interface would invite the Game layer to register its own out-of-order.
    /// </para>
    /// </remarks>
    internal interface IMigration
    {
        /// <summary>Schema version this migration reads. It produces <c>FromVersion + 1</c>.</summary>
        int FromVersion { get; }

        /// <summary>
        /// Rewrites <paramref name="doc"/>'s sections in place. Returns <see cref="ResultCode.Ok"/>
        /// on success, or <see cref="ResultCode.SaveCorrupt"/> when the document does not contain
        /// what this version is supposed to contain.
        /// </summary>
        ResultCode Apply(SaveDocument doc, ICoreLog log);
    }

    /// <summary>
    /// Brings a loaded <see cref="SaveDocument"/> up to
    /// <see cref="SaveDocument.CurrentSchemaVersion"/> by applying single-version migrations in
    /// order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 1 ships with an empty chain — there is only one schema version, so there is nothing
    /// to migrate. The machinery exists anyway, and is exercised on every single load, because a
    /// migration seam that is first written on the day it is first needed is a seam written under
    /// pressure against real player saves. Here it is written while the cost of getting it wrong
    /// is zero, and the version-guard path (too-new, unmigratable) is live from day one.
    /// </para>
    /// <para>
    /// The two failure modes are kept strictly apart. A save from a <em>newer</em> build is not
    /// corrupt; it is simply beyond us, and the only honest response is to refuse it and say so.
    /// Guessing — loading it anyway and hoping the unknown fields are additive — is how a player
    /// who downgraded a build loses a save that was perfectly intact.
    /// </para>
    /// </remarks>
    public static class SaveMigrator
    {
        /// <summary>
        /// The chain, ordered ascending by <see cref="IMigration.FromVersion"/> with no gaps and
        /// no duplicates. Empty in Phase 1. When adding an entry, append it and bump
        /// <see cref="SaveDocument.CurrentSchemaVersion"/> in the same change.
        /// </summary>
        private static readonly IMigration[] Chain = new IMigration[0];

        /// <summary>
        /// Migrates <paramref name="doc"/> in place to the current schema version.
        /// </summary>
        /// <param name="doc">Document just read from disk. Mutated on success.</param>
        /// <param name="log">Diagnostics sink; a null log is tolerated.</param>
        /// <returns>
        /// <see cref="ResultCode.Ok"/> when the document is at the current version (already, or
        /// after migrating); <see cref="ResultCode.SaveVersionTooNew"/> when it was written by a
        /// newer build; <see cref="ResultCode.SaveCorrupt"/> when the version is unreachable —
        /// no migration exists for it — or a migration rejected the contents.
        /// </returns>
        public static ResultCode Migrate(SaveDocument doc, ICoreLog log)
        {
            if (doc == null)
            {
                Warn(log, LogCode.SaveCorrupt, "null document");
                return ResultCode.SaveCorrupt;
            }

            if (doc.SchemaVersion > SaveDocument.CurrentSchemaVersion)
            {
                // Reported through the save-failure channel rather than swallowed: a downgraded
                // build silently refusing a save looks identical to a lost save from the outside.
                Warn(
                    log,
                    LogCode.SaveCorrupt,
                    "schemaVersion " + doc.SchemaVersion.ToString() +
                    " is newer than supported " + SaveDocument.CurrentSchemaVersion.ToString());
                return ResultCode.SaveVersionTooNew;
            }

            // Bounded independently of the chain's contents. The bump below guarantees progress,
            // but the guard means a future malformed chain degrades to a reported failure rather
            // than a hang on the loading screen.
            var remainingSteps = SaveDocument.CurrentSchemaVersion + 1;

            while (doc.SchemaVersion < SaveDocument.CurrentSchemaVersion)
            {
                if (remainingSteps <= 0)
                {
                    Warn(log, LogCode.SaveCorrupt, "migration chain did not converge");
                    return ResultCode.SaveCorrupt;
                }

                remainingSteps--;

                var migration = FindMigration(doc.SchemaVersion);
                if (migration == null)
                {
                    Warn(
                        log,
                        LogCode.SaveCorrupt,
                        "no migration from schemaVersion " + doc.SchemaVersion.ToString());
                    return ResultCode.SaveCorrupt;
                }

                var code = migration.Apply(doc, log);
                if (code != ResultCode.Ok)
                {
                    Warn(
                        log,
                        LogCode.SaveCorrupt,
                        "migration from schemaVersion " + doc.SchemaVersion.ToString() +
                        " failed with " + code.ToString());
                    return code;
                }

                doc.SchemaVersion = migration.FromVersion + 1;
                Warn(log, LogCode.SaveMigrationApplied, "now at schemaVersion " + doc.SchemaVersion.ToString());
            }

            // The header carries its own copy so the slot list can be drawn without a full read;
            // leaving it behind would make a migrated save look stale to the menu.
            if (doc.Metadata != null)
            {
                doc.Metadata.SchemaVersion = doc.SchemaVersion;
            }

            return ResultCode.Ok;
        }

        /// <summary>
        /// Linear scan for the step that reads <paramref name="fromVersion"/>. Linear because the
        /// chain is a handful of entries at most and is walked once per load; a dictionary here
        /// would buy nothing and lose the ordering the chain is defined by.
        /// </summary>
        private static IMigration FindMigration(int fromVersion)
        {
            for (var i = 0; i < Chain.Length; i++)
            {
                var candidate = Chain[i];
                if (candidate != null && candidate.FromVersion == fromVersion)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void Warn(ICoreLog log, LogCode code, string detail)
        {
            if (log == null)
            {
                return;
            }

            log.Warn(code, detail);
        }
    }
}

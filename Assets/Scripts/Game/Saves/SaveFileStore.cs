using System;
using System.IO;
using System.Text;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Primitives;
using UnityEngine;

namespace ForgottenIsle.Game.Saves
{
    /// <summary>
    /// The only code in Vardholm that touches the save directory. Owns the atomic write, the
    /// <c>.bak</c> rotation, and the platform quirks that go with them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store deals in text keyed by a file key, and knows nothing about documents, slots, rings or
    /// checksums. <c>SaveSlotService</c> owns all of that. The split matters because the failure modes here
    /// are filesystem failures — a full disk, a revoked permission, a process killed mid-write — and they
    /// are easier to reason about, and to test, when they are not tangled with serialization.
    /// </para>
    /// <para>
    /// THREE FILES PER KEY: <c>key.sav</c> (the save), <c>key.sav.bak</c> (the previous save, rotated in by
    /// the write), and <c>key.sav.tmp</c> (the write in progress, which should never be observed at rest).
    /// A stray <c>.tmp</c> means the app died mid-write; it is not promoted, because the one thing known
    /// about it is that nobody confirmed it was complete.
    /// </para>
    /// </remarks>
    public sealed class SaveFileStore
    {
        /// <summary>Extension for the live save.</summary>
        public const string SaveExtension = ".sav";

        /// <summary>Extension for the rotated previous save.</summary>
        public const string BackupExtension = ".sav.bak";

        /// <summary>Extension for a write in progress.</summary>
        public const string TempExtension = ".sav.tmp";

        /// <summary>Folder under the platform's persistent data path where saves live.</summary>
        public const string SaveFolderName = "saves";

        /// <summary>
        /// UTF-8 with no byte order mark. Pinned rather than defaulted because <c>Crc32.Compute(string)</c>
        /// encodes as UTF-8 to check the save's integrity: a BOM, or a platform-default encoding, would
        /// make a file written on one device fail verification on another.
        /// </summary>
        private static readonly UTF8Encoding FileEncoding = new UTF8Encoding(false, false);

        private readonly string _rootDirectory;
        private readonly ICoreLog _log;

        /// <summary>
        /// Creates a store rooted under <see cref="Application.persistentDataPath"/>.
        /// </summary>
        /// <remarks>
        /// <c>persistentDataPath</c> and nothing else: it is the only location that survives an app update
        /// on iOS and Android, is writable on every target, and is included in the platform's own backup
        /// story — which is exactly why <see cref="ExcludeFromCloudBackup"/> then opts back out of it.
        /// </remarks>
        public SaveFileStore(ICoreLog log)
            : this(Path.Combine(Application.persistentDataPath, SaveFolderName), log)
        {
        }

        /// <summary>
        /// Creates a store rooted at an explicit directory.
        /// </summary>
        /// <remarks>
        /// Exists so EditMode tests can point the store at a temporary folder, and so the composition root
        /// can name <c>Application.persistentDataPath</c> explicitly when it wants the save files at the
        /// root of it rather than in the <c>saves</c> subfolder. A hard-coded literal path in shipping code
        /// is a bug. The log is optional only so that call form stays a single argument; a store built
        /// without one reports nothing, which is a real loss when a write fails.
        /// </remarks>
        public SaveFileStore(string rootDirectory, ICoreLog log = null)
        {
            _rootDirectory = string.IsNullOrEmpty(rootDirectory)
                ? Path.Combine(Application.persistentDataPath, SaveFolderName)
                : rootDirectory;
            _log = log;
        }

        /// <summary>Absolute path of the directory holding every save file.</summary>
        public string RootDirectory => _rootDirectory;

        /// <summary>Absolute path of the live save for <paramref name="fileKey"/>.</summary>
        public string GetSavePath(string fileKey) => Path.Combine(_rootDirectory, Sanitize(fileKey) + SaveExtension);

        /// <summary>Absolute path of the rotated previous save for <paramref name="fileKey"/>.</summary>
        public string GetBackupPath(string fileKey) => Path.Combine(_rootDirectory, Sanitize(fileKey) + BackupExtension);

        /// <summary>Absolute path of the in-progress write for <paramref name="fileKey"/>.</summary>
        public string GetTempPath(string fileKey) => Path.Combine(_rootDirectory, Sanitize(fileKey) + TempExtension);

        /// <summary>True when a live save exists for this key. A backup alone does not count as existing.</summary>
        public bool Exists(string fileKey)
        {
            try
            {
                return File.Exists(GetSavePath(fileKey));
            }
            catch (Exception exception)
            {
                Warn(LogCode.SaveCorrupt, "exists " + fileKey + ": " + exception.GetType().Name);
                return false;
            }
        }

        /// <summary>True when a rotated backup exists for this key.</summary>
        public bool BackupExists(string fileKey)
        {
            try
            {
                return File.Exists(GetBackupPath(fileKey));
            }
            catch (Exception exception)
            {
                Warn(LogCode.SaveCorrupt, "bakExists " + fileKey + ": " + exception.GetType().Name);
                return false;
            }
        }

        /// <summary>
        /// Writes <paramref name="payload"/> for <paramref name="fileKey"/> atomically, rotating the
        /// previous contents into the backup.
        /// </summary>
        /// <remarks>
        /// <para>
        /// THE SEQUENCE IS: write the temp file, FLUSH IT TO PHYSICAL STORAGE, then rename it over the
        /// final name.
        /// </para>
        /// <para>
        /// WHY THE FLUSH MUST PRECEDE THE RENAME. Closing a <see cref="FileStream"/> only guarantees the
        /// bytes have reached the operating system's page cache, not the storage device. A rename is
        /// metadata work and is cheap, so on several filesystems it can be committed to the journal before
        /// the data blocks it points at are. If power is lost, or the OS kills the app, in that window, the
        /// rename WINS THE RACE: the final filename now exists, is the right size in the directory entry,
        /// and contains zeroes or garbage. The player's save is gone and nothing about the situation looks
        /// like a failure — which is worse than a crash, because the game will load it. <c>Flush(true)</c>
        /// issues the platform's fsync equivalent and blocks until the device acknowledges the data, so by
        /// the time the rename happens there is real, durable content behind the name.
        /// </para>
        /// <para>
        /// <c>File.Replace</c> is preferred over delete-then-move because it performs the swap and the
        /// backup rotation as one operation, so there is no instant at which neither the old nor the new
        /// save exists under the final name. It is not available on every filesystem Unity ships to
        /// (notably some Android external storage mounts), hence the explicit fallback, which orders its
        /// steps so the backup is in place before the live file is disturbed.
        /// </para>
        /// </remarks>
        /// <returns><see cref="ResultCode.Ok"/>, or <see cref="ResultCode.SaveWriteFailed"/> with the cause logged.</returns>
        public ResultCode Write(string fileKey, string payload)
        {
            if (string.IsNullOrEmpty(fileKey))
            {
                return ResultCode.InvalidArgument;
            }

            var savePath = GetSavePath(fileKey);
            var backupPath = GetBackupPath(fileKey);
            var tempPath = GetTempPath(fileKey);

            try
            {
                Directory.CreateDirectory(_rootDirectory);

                var bytes = FileEncoding.GetBytes(payload ?? string.Empty);

                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    ForceToStorage(stream, fileKey);
                }

                if (File.Exists(savePath))
                {
                    ReplaceWithRotation(tempPath, savePath, backupPath);
                }
                else
                {
                    // Nothing to rotate on a first write. Move is atomic within a volume, and both paths
                    // are in the same directory by construction.
                    File.Move(tempPath, savePath);
                }

                ExcludeFromCloudBackup(savePath);
                ExcludeFromCloudBackup(backupPath);
                return ResultCode.Ok;
            }
            catch (Exception exception)
            {
                Warn(LogCode.SaveCorrupt, "write " + fileKey + ": " + exception.GetType().Name + " " + exception.Message);
                TryDeleteQuietly(tempPath);
                return ResultCode.SaveWriteFailed;
            }
        }

        /// <summary>Reads the live save for <paramref name="fileKey"/>.</summary>
        /// <returns>
        /// <see cref="ResultCode.Ok"/>; <see cref="ResultCode.SlotEmpty"/> when there is no such file;
        /// <see cref="ResultCode.SaveCorrupt"/> when the file exists but cannot be read.
        /// </returns>
        public ResultCode Read(string fileKey, out string text) => ReadFile(GetSavePath(fileKey), fileKey, out text);

        /// <summary>Reads the rotated backup for <paramref name="fileKey"/>, for use after the live save fails to decode.</summary>
        public ResultCode ReadBackup(string fileKey, out string text) => ReadFile(GetBackupPath(fileKey), fileKey + ".bak", out text);

        /// <summary>
        /// Reads at most <paramref name="maxCharacters"/> characters from the start of the live save.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is what makes <c>SaveSlotService.ReadMetadata</c> cheap in the way that actually matters on
        /// a phone: not merely skipping the deserialization of the body, but never reading its bytes off
        /// storage at all. The header is the first member the writer emits, so a few kilobytes is always
        /// enough; a prefix that turns out to be too short is detected by the caller (the metadata object
        /// will not close) and costs one full read to recover from.
        /// </para>
        /// <para>
        /// A <see cref="StreamReader"/> is used rather than reading raw bytes and decoding them, because a
        /// fixed byte cap can fall in the middle of a multi-byte UTF-8 sequence and produce a replacement
        /// character. The reader owns the decoder state across buffer boundaries and never splits one.
        /// </para>
        /// </remarks>
        public ResultCode ReadPrefix(string fileKey, int maxCharacters, out string text)
        {
            text = string.Empty;

            if (maxCharacters <= 0)
            {
                return ResultCode.InvalidArgument;
            }

            var path = GetSavePath(fileKey);

            try
            {
                if (!File.Exists(path))
                {
                    return ResultCode.SlotEmpty;
                }

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new StreamReader(stream, FileEncoding, false))
                {
                    var buffer = new char[maxCharacters];
                    var read = reader.ReadBlock(buffer, 0, maxCharacters);
                    text = read <= 0 ? string.Empty : new string(buffer, 0, read);
                }

                return ResultCode.Ok;
            }
            catch (Exception exception)
            {
                Warn(LogCode.SaveCorrupt, "readPrefix " + fileKey + ": " + exception.GetType().Name);
                return ResultCode.SaveCorrupt;
            }
        }

        /// <summary>
        /// Deletes every file belonging to <paramref name="fileKey"/> — live, backup and any stray temp.
        /// </summary>
        /// <remarks>
        /// The backup goes too. A player who deletes a save has asked for it to be gone, and leaving a
        /// shadow copy that a later failure could resurrect is not a feature.
        /// </remarks>
        public ResultCode Delete(string fileKey)
        {
            if (string.IsNullOrEmpty(fileKey))
            {
                return ResultCode.InvalidArgument;
            }

            var anyFailure = false;
            anyFailure |= !TryDeleteQuietly(GetSavePath(fileKey));
            anyFailure |= !TryDeleteQuietly(GetBackupPath(fileKey));
            anyFailure |= !TryDeleteQuietly(GetTempPath(fileKey));

            return anyFailure ? ResultCode.SaveWriteFailed : ResultCode.Ok;
        }

        /// <summary>
        /// Performs the swap, preferring the one-call form and degrading to an ordered three-step when the
        /// filesystem does not support it.
        /// </summary>
        private void ReplaceWithRotation(string tempPath, string savePath, string backupPath)
        {
            try
            {
                // ignoreMetadataErrors: true — a failure to carry across ACLs or timestamps must not fail a
                // save. The bytes are what the player cares about.
                File.Replace(tempPath, savePath, backupPath, true);
                return;
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (NotSupportedException)
            {
            }
            catch (IOException)
            {
                // Some Android and network mounts report Replace as an ordinary IO failure. Fall through
                // rather than losing the save to a filesystem capability difference.
            }

            // Fallback, ordered so a failure at any step leaves a complete save somewhere: copy the current
            // save aside FIRST, then delete it, then promote the temp file.
            TryDeleteQuietly(backupPath);
            File.Copy(savePath, backupPath, true);
            File.Delete(savePath);
            File.Move(tempPath, savePath);
        }

        /// <summary>
        /// Blocks until the file's contents are on physical storage, not merely in the OS cache.
        /// </summary>
        /// <remarks>
        /// The boolean overload is not implemented on every scripting backend Unity offers. Where it is
        /// missing, the plain flush at least pushes the bytes out of the managed buffer, and the warning
        /// records that this write had weaker durability than the code intends — a fact worth having in a
        /// bug report about a save that vanished.
        /// </remarks>
        private void ForceToStorage(FileStream stream, string fileKey)
        {
            try
            {
                stream.Flush(true);
            }
            catch (NotSupportedException)
            {
                stream.Flush();
                Warn(LogCode.SaveCorrupt, "durable flush unsupported for " + fileKey);
            }
            catch (NotImplementedException)
            {
                stream.Flush();
                Warn(LogCode.SaveCorrupt, "durable flush unimplemented for " + fileKey);
            }
        }

        /// <summary>
        /// Marks a save file as excluded from iCloud and iTunes backup on iOS.
        /// </summary>
        /// <remarks>
        /// Apple rejects apps that back up regenerable data, and a save directory that grows with play time
        /// is exactly what the guideline is aimed at. The flag is per file and is lost when a file is
        /// replaced, so it is reapplied after every write rather than set once at first run. No other
        /// platform needs this, and the call does not exist off iOS.
        /// </remarks>
        private static void ExcludeFromCloudBackup(string path)
        {
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                if (File.Exists(path))
                {
                    UnityEngine.iOS.Device.SetNoBackupFlag(path);
                }
            }
            catch (Exception)
            {
                // Advisory only: a save that is backed up is a policy problem, not a player problem, and
                // must never be the reason a write reports failure.
            }
#endif
        }

        private ResultCode ReadFile(string path, string label, out string text)
        {
            text = string.Empty;

            try
            {
                if (!File.Exists(path))
                {
                    return ResultCode.SlotEmpty;
                }

                text = File.ReadAllText(path, FileEncoding);
                return ResultCode.Ok;
            }
            catch (Exception exception)
            {
                Warn(LogCode.SaveCorrupt, "read " + label + ": " + exception.GetType().Name);
                return ResultCode.SaveCorrupt;
            }
        }

        private bool TryDeleteQuietly(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return true;
            }
            catch (Exception exception)
            {
                Warn(LogCode.SaveCorrupt, "delete " + Path.GetFileName(path) + ": " + exception.GetType().Name);
                return false;
            }
        }

        /// <summary>
        /// Strips anything from a file key that could escape the save directory or be illegal on a target
        /// filesystem. Keys are produced in code, not by players, so this is a guard against a future
        /// mistake rather than against hostile input — but a key containing <c>..</c> would be a very
        /// expensive mistake to discover in the wild.
        /// </summary>
        private static string Sanitize(string fileKey)
        {
            if (string.IsNullOrEmpty(fileKey))
            {
                return "invalid";
            }

            var builder = new StringBuilder(fileKey.Length);
            for (var i = 0; i < fileKey.Length; i++)
            {
                var c = fileKey[i];
                var safe = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                builder.Append(safe ? c : '_');
            }

            return builder.ToString();
        }

        private void Warn(LogCode code, string detail)
        {
            if (_log == null)
            {
                return;
            }

            _log.Warn(code, detail);
        }
    }
}

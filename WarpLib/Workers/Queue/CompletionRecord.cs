using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Warp.Workers.Queue
{
    /// <summary>
    /// Per-task completion records that let a re-run skip work it has already done.
    ///
    /// A record is keyed by the task's <see cref="Fingerprint"/>: a hash over its stage and its
    /// serialized Init and Main commands. Those carry the item's path and the full processing
    /// options, so the key means "this exact work on this exact item". The record stores a hash
    /// of the item's metadata file as it stood when the task completed.
    ///
    /// A task counts as already done only if both hold:
    /// <list type="bullet">
    /// <item>a record exists for its fingerprint (same command, same item, same options), and</item>
    /// <item>the item's metadata is byte-identical to the recorded state, so nothing has changed it
    /// since (another command, or a re-run of an earlier step, rewrites it).</item>
    /// </list>
    /// Otherwise the task runs. Every way of being wrong therefore costs a re-run, never a skip.
    ///
    /// Each record is its own file, written to a temporary name and renamed, so concurrent result
    /// handlers and a crash mid-write leave no partial record.
    /// </summary>
    public static class CompletionRecord
    {
        private static readonly JsonSerializerOptions Opts = new() { WriteIndented = false };

        /// <summary>SHA-256 over the task's stage, Init and Main. Excludes TaskId (it carries the item's
        /// index in the input list, which changes when the list does) and all scheduling fields.</summary>
        public static string Fingerprint(TaskItem task)
        {
            string payload = task.Stage + "\n" +
                             JsonSerializer.Serialize(task.Init, Opts) + "\n" +
                             JsonSerializer.Serialize(task.Main, Opts);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        }

        /// <summary>SHA-256 of a metadata file's bytes, or null if it does not exist.</summary>
        public static string HashFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        public static string RecordPath(string recordDir, TaskItem task) =>
            Path.Combine(recordDir, Fingerprint(task) + ".json");

        /// <summary>Record that <paramref name="task"/> completed, with the item's metadata file as it
        /// is now. Call after every write the success path makes to that file.</summary>
        public static void MarkComplete(string recordDir, TaskItem task, string metadataPath)
        {
            string metaHash = HashFile(metadataPath);
            if (metaHash == null)
                return;   // nothing to compare against later, so a record would prove nothing

            Directory.CreateDirectory(recordDir);
            string path = RecordPath(recordDir, task);
            var record = new Record
            {
                TaskId = task.TaskId,
                Stage = task.Stage,
                MetadataPath = Path.GetFullPath(metadataPath),
                MetadataSha256 = metaHash,
                CompletedAt = DateTime.UtcNow.ToString("o")
            };

            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(record, Opts));
            File.Move(temp, path, overwrite: true);
        }

        /// <summary>Remove any record for <paramref name="task"/> (its latest attempt failed).</summary>
        public static void Clear(string recordDir, TaskItem task)
        {
            string path = RecordPath(recordDir, task);
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>True if <paramref name="task"/> completed earlier and the item's metadata file is
        /// unchanged since. Any unreadable or mismatching record answers false.</summary>
        public static bool IsComplete(string recordDir, TaskItem task, string metadataPath)
        {
            string path = RecordPath(recordDir, task);
            if (!File.Exists(path))
                return false;

            Record record;
            try
            {
                record = JsonSerializer.Deserialize<Record>(File.ReadAllText(path), Opts);
            }
            catch (Exception)
            {
                return false;
            }

            if (record == null || string.IsNullOrEmpty(record.MetadataSha256))
                return false;

            string current = HashFile(metadataPath);
            return current != null && string.Equals(current, record.MetadataSha256, StringComparison.OrdinalIgnoreCase);
        }

        public class Record
        {
            public string TaskId { get; set; }
            public string Stage { get; set; }
            public string MetadataPath { get; set; }
            public string MetadataSha256 { get; set; }
            public string CompletedAt { get; set; }
        }
    }
}

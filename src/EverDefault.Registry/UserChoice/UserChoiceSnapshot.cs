using System;

namespace EverDefault.Registry.UserChoice
{
    /// <summary>
    /// Immutable-ish snapshot of a single extension's UserChoice association.
    /// LastWriteTimeUtc is the raw FILETIME returned by RegQueryInfoKey and is part
    /// of the input Windows feeds into the UserChoice hash, so it must be preserved
    /// exactly when restoring.
    /// </summary>
    public sealed class UserChoiceSnapshot
    {
        public string Extension { get; set; }

        public bool Exists { get; set; }

        public string ProgId { get; set; }

        public string Hash { get; set; }

        /// <summary>Raw FILETIME (100ns since 1601-01-01, UTC) of the UserChoice key.</summary>
        public long LastWriteTimeUtc { get; set; }

        public DateTime CapturedUtc { get; set; }

        public override string ToString()
        {
            if (!Exists)
                return Extension + ": <no UserChoice key>";

            return string.Format(
                "{0}: ProgId={1}, Hash={2}, LastWriteTimeUtc={3} ({4:o})",
                Extension,
                ProgId ?? "<null>",
                Hash ?? "<null>",
                LastWriteTimeUtc,
                DateTime.FromFileTimeUtc(LastWriteTimeUtc));
        }
    }
}

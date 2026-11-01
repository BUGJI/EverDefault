using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EverDefault.Core.Model;
using EverDefault.Core.Storage;
using EverDefault.Persistence.Json;
using Newtonsoft.Json;

namespace EverDefault.Persistence
{
    /// <summary>
    /// Append-only change log. Entries are stored one JSON object per line, so appending is
    /// O(1) and never rewrites the whole file. Reads come from an in-memory cache that is
    /// loaded once; trimming rewrites the file only occasionally.
    /// </summary>
    public sealed class JsonChangeLogStore : IChangeLogStore
    {
        private const int HardMaxEntries = 10000;
        private const int CompactionBatch = 256;

        private static readonly JsonSerializerSettings LineSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.DateTime,
            Formatting = Formatting.None
        };

        private readonly object _sync = new object();
        private readonly string _path;
        private List<ChangeLogEntry> _cache;
        private long _lastId;
        private FileStream _stream;
        private StreamWriter _writer;

        public JsonChangeLogStore(string path)
        {
            _path = path;
        }

        public JsonChangeLogStore()
            : this(DataPaths.ChangeLogFile)
        {
        }

        public event Action<ChangeLogEntry> Appended;

        public long LastId
        {
            get
            {
                lock (_sync)
                {
                    Load();
                    return _lastId;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return Load().Count;
                }
            }
        }

        public void Append(ChangeLogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            lock (_sync)
            {
                var list = Load();
                var id = _lastId + 1;
                entry.Id = id;
                if (entry.Utc == default(DateTime))
                    entry.Utc = DateTime.UtcNow;

                AppendLine(entry);
                _lastId = id;
                list.Add(entry);

                if (list.Count > HardMaxEntries + CompactionBatch)
                {
                    list.RemoveRange(0, list.Count - HardMaxEntries);
                    Rewrite(list);
                }
            }

            var handler = Appended;
            if (handler != null)
                handler(entry);
        }

        public IReadOnlyList<ChangeLogEntry> Query(int max = 500, Guid? ruleId = null)
        {
            if (max <= 0)
                return new List<ChangeLogEntry>();

            lock (_sync)
            {
                var list = Load();
                var result = new List<ChangeLogEntry>(Math.Min(max, list.Count));
                for (var i = list.Count - 1; i >= 0 && result.Count < max; i--)
                {
                    var entry = list[i];
                    if (ruleId.HasValue && entry.RuleId != ruleId.Value)
                        continue;

                    result.Add(entry);
                }

                return result;
            }
        }

        public IReadOnlyList<ChangeLogEntry> QueryAfter(long afterId, int max)
        {
            if (max <= 0)
                return new List<ChangeLogEntry>();

            lock (_sync)
            {
                var list = Load();
                var start = LowerBound(list, afterId);
                var count = Math.Min(max, list.Count - start);
                if (count <= 0)
                    return new List<ChangeLogEntry>();

                return list.GetRange(start, count);
            }
        }

        public void Trim(int keep)
        {
            if (keep < 0)
                keep = 0;

            lock (_sync)
            {
                var list = Load();
                if (list.Count <= keep)
                    return;

                list.RemoveRange(0, list.Count - keep);
                Rewrite(list);
            }
        }

        public int Maintenance(DateTime cutoffUtc, int maxKeep)
        {
            lock (_sync)
            {
                var list = Load();
                var removed = list.RemoveAll(e => e.Utc < cutoffUtc);

                if (maxKeep > 0 && list.Count > maxKeep)
                {
                    removed += list.Count - maxKeep;
                    list.RemoveRange(0, list.Count - maxKeep);
                }

                if (removed > 0)
                    Rewrite(list);

                return removed;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                CloseWriter();
                _cache = new List<ChangeLogEntry>();
                _lastId = 0;

                try
                {
                    if (File.Exists(_path))
                        File.Delete(_path);
                }
                catch (Exception)
                {
                    // The in-memory log is authoritative; a failed delete is retried on next trim.
                }
            }
        }

        public void Flush()
        {
            lock (_sync)
            {
                CloseWriter();
            }
        }

        private List<ChangeLogEntry> Load()
        {
            if (_cache != null)
                return _cache;

            var list = new List<ChangeLogEntry>();
            var legacy = false;

            if (File.Exists(_path))
            {
                try
                {
                    var content = File.ReadAllText(_path);
                    var trimmed = content.TrimStart();
                    if (trimmed.StartsWith("[", StringComparison.Ordinal))
                    {
                        legacy = true;
                        var array = JsonConvert.DeserializeObject<List<ChangeLogEntry>>(
                            content, Serializer.Settings);
                        if (array != null)
                            list = array;
                    }
                    else
                    {
                        ReadLines(content, list);
                    }
                }
                catch (Exception)
                {
                    // A corrupt file must not stop monitoring; start from an empty log.
                    list = new List<ChangeLogEntry>();
                }
            }

            Normalize(list);
            _cache = list;
            _lastId = list.Count == 0 ? 0 : list[list.Count - 1].Id;

            if (legacy && list.Count > 0)
                Rewrite(list);

            return _cache;
        }

        private static void ReadLines(string content, List<ChangeLogEntry> output)
        {
            var lines = content.Split('\n');
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0)
                    continue;

                try
                {
                    var entry = JsonConvert.DeserializeObject<ChangeLogEntry>(line, LineSettings);
                    if (entry != null)
                        output.Add(entry);
                }
                catch (Exception)
                {
                    // Skip a malformed or partially written line.
                }
            }
        }

        private static void Normalize(List<ChangeLogEntry> list)
        {
            list.Sort((a, b) => a.Id.CompareTo(b.Id));

            long previous = 0;
            foreach (var entry in list)
            {
                if (entry.Id <= previous)
                    entry.Id = previous + 1;

                previous = entry.Id;
                if (entry.Utc == default(DateTime))
                    entry.Utc = DateTime.UtcNow;
            }
        }

        private static int LowerBound(List<ChangeLogEntry> list, long afterId)
        {
            var low = 0;
            var high = list.Count;
            while (low < high)
            {
                var mid = low + ((high - low) / 2);
                if (list[mid].Id <= afterId)
                    low = mid + 1;
                else
                    high = mid;
            }

            return low;
        }

        private void AppendLine(ChangeLogEntry entry)
        {
            EnsureWriter();
            var json = JsonConvert.SerializeObject(entry, Formatting.None, LineSettings);
            _writer.WriteLine(json);
        }

        private void EnsureWriter()
        {
            if (_writer != null)
                return;

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            _stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(_stream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
        }

        private void CloseWriter()
        {
            if (_writer != null)
            {
                try
                {
                    _writer.Flush();
                }
                catch (Exception)
                {
                    // Best-effort flush; the file is re-read on next startup.
                }

                _writer.Dispose();
                _writer = null;
            }

            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
        }

        private void Rewrite(List<ChangeLogEntry> list)
        {
            CloseWriter();

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temp = _path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                foreach (var entry in list)
                    writer.WriteLine(JsonConvert.SerializeObject(entry, Formatting.None, LineSettings));
            }

            if (File.Exists(_path))
                File.Replace(temp, _path, null);
            else
                File.Move(temp, _path);
        }
    }
}

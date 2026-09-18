using System;
using System.Collections.Generic;
using System.Linq;
using EverDefault.Core.Model;
using EverDefault.Core.Storage;
using EverDefault.Persistence.Json;

namespace EverDefault.Persistence
{
    public sealed class JsonChangeLogStore : IChangeLogStore
    {
        private readonly object _sync = new object();
        private readonly string _path;
        private List<ChangeLogEntry> _cache;

        public JsonChangeLogStore(string path)
        {
            _path = path;
        }

        public JsonChangeLogStore()
            : this(DataPaths.ChangeLogFile)
        {
        }

        public void Append(ChangeLogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            lock (_sync)
            {
                var list = Load();
                entry.Id = list.Count == 0 ? 1 : list.Max(e => e.Id) + 1;
                list.Add(entry);
                Save(list);
            }
        }

        public IReadOnlyList<ChangeLogEntry> Query(int max = 500, Guid? ruleId = null)
        {
            lock (_sync)
            {
                var query = Load().AsEnumerable();
                if (ruleId.HasValue)
                    query = query.Where(e => e.RuleId == ruleId.Value);
                return query.OrderByDescending(e => e.Id).Take(max).ToList();
            }
        }

        public void Trim(int keep)
        {
            lock (_sync)
            {
                var list = Load();
                if (list.Count <= keep)
                    return;

                var trimmed = list.OrderByDescending(e => e.Id).Take(keep).OrderBy(e => e.Id).ToList();
                Save(trimmed);
            }
        }

        private List<ChangeLogEntry> Load()
        {
            if (_cache == null)
                _cache = JsonFile.Read<List<ChangeLogEntry>>(_path) ?? new List<ChangeLogEntry>();
            return _cache;
        }

        private void Save(List<ChangeLogEntry> list)
        {
            JsonFile.Write(_path, list);
            _cache = list;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EverDefault.Core.Model;
using EverDefault.Core.Storage;
using EverDefault.Persistence.Json;

namespace EverDefault.Persistence
{
    public sealed class JsonBaselineStore : IBaselineStore
    {
        private readonly object _sync = new object();
        private readonly string _path;
        private List<BaselineRecord> _cache;

        public JsonBaselineStore(string path)
        {
            _path = path;
        }

        public JsonBaselineStore()
            : this(DataPaths.BaselinesFile)
        {
        }

        public IReadOnlyList<BaselineRecord> GetForRule(Guid ruleId)
        {
            lock (_sync)
            {
                return Load().Where(b => b.RuleId == ruleId).ToList();
            }
        }

        public void ReplaceForRule(Guid ruleId, IEnumerable<BaselineRecord> records)
        {
            lock (_sync)
            {
                var list = Load();
                list.RemoveAll(b => b.RuleId == ruleId);
                foreach (var record in records)
                {
                    record.RuleId = ruleId;
                    list.Add(record);
                }
                Save(list);
            }
        }

        public void DeleteForRule(Guid ruleId)
        {
            lock (_sync)
            {
                var list = Load();
                if (list.RemoveAll(b => b.RuleId == ruleId) > 0)
                    Save(list);
            }
        }

        private List<BaselineRecord> Load()
        {
            if (_cache == null)
                _cache = JsonFile.Read<List<BaselineRecord>>(_path) ?? new List<BaselineRecord>();
            return _cache;
        }

        private void Save(List<BaselineRecord> list)
        {
            JsonFile.Write(_path, list);
            _cache = list;
        }
    }
}

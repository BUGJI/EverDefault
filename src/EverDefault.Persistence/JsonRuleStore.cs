using System;
using System.Collections.Generic;
using System.Linq;
using EverDefault.Core.Model;
using EverDefault.Core.Storage;
using EverDefault.Persistence.Json;

namespace EverDefault.Persistence
{
    public sealed class JsonRuleStore : IRuleStore
    {
        private readonly object _sync = new object();
        private readonly string _path;
        private List<RuleBase> _cache;

        public JsonRuleStore(string path)
        {
            _path = path;
        }

        public JsonRuleStore()
            : this(DataPaths.RulesFile)
        {
        }

        public IReadOnlyList<RuleBase> GetAll()
        {
            lock (_sync)
            {
                return Load().OrderBy(r => r.Priority).ThenBy(r => r.Name).ToList();
            }
        }

        public RuleBase Get(Guid id)
        {
            lock (_sync)
            {
                return Load().FirstOrDefault(r => r.Id == id);
            }
        }

        public void Upsert(RuleBase rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));

            lock (_sync)
            {
                var list = Load();
                var index = list.FindIndex(r => r.Id == rule.Id);
                rule.UpdatedUtc = DateTime.UtcNow;
                if (index >= 0)
                    list[index] = rule;
                else
                    list.Add(rule);

                Save(list);
            }
        }

        public void Delete(Guid id)
        {
            lock (_sync)
            {
                var list = Load();
                if (list.RemoveAll(r => r.Id == id) > 0)
                    Save(list);
            }
        }

        private List<RuleBase> Load()
        {
            if (_cache == null)
                _cache = JsonFile.Read<List<RuleBase>>(_path) ?? new List<RuleBase>();
            return _cache;
        }

        private void Save(List<RuleBase> list)
        {
            JsonFile.Write(_path, list);
            _cache = list;
        }
    }
}

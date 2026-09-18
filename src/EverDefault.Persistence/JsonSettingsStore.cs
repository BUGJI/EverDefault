using EverDefault.Core.Model;
using EverDefault.Core.Storage;
using EverDefault.Persistence.Json;

namespace EverDefault.Persistence
{
    public sealed class JsonSettingsStore : ISettingsStore
    {
        private readonly object _sync = new object();
        private readonly string _path;
        private AppSettings _cache;

        public JsonSettingsStore(string path)
        {
            _path = path;
        }

        public JsonSettingsStore()
            : this(DataPaths.SettingsFile)
        {
        }

        public AppSettings Load()
        {
            lock (_sync)
            {
                if (_cache == null)
                    _cache = JsonFile.Read<AppSettings>(_path) ?? new AppSettings();
                return _cache;
            }
        }

        public void Save(AppSettings settings)
        {
            lock (_sync)
            {
                _cache = settings ?? new AppSettings();
                JsonFile.Write(_path, _cache);
            }
        }
    }
}

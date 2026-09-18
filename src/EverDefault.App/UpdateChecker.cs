using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using EverDefault.Core.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EverDefault.App
{
    internal sealed class UpdateInfo
    {
        public bool Available;

        public string LatestTag;

        public string ReleaseUrl;
    }

    /// <summary>Checks GitHub Releases for a newer version and remembers when it last ran.</summary>
    internal static class UpdateChecker
    {
        private const string LatestApi =
            "https://api.github.com/repos/BUGJI/EverDefault/releases/latest";

        private static string StatePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EverDefault",
                    "update-state.json");
            }
        }

        public static TimeSpan Interval(UpdateInterval interval)
        {
            switch (interval)
            {
                case UpdateInterval.Daily: return TimeSpan.FromDays(1);
                case UpdateInterval.Monthly: return TimeSpan.FromDays(30);
                default: return TimeSpan.FromDays(7);
            }
        }

        public static bool IsDue(UpdateInterval interval)
        {
            var state = LoadState();
            return state.LastCheckUtc == default(DateTime)
                   || DateTime.UtcNow - state.LastCheckUtc >= Interval(interval);
        }

        public static async Task<UpdateInfo> CheckAsync()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (Exception)
            {
                // Older frameworks ignore unknown flags.
            }

            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("EverDefault");
                http.Timeout = TimeSpan.FromSeconds(15);

                var json = await http.GetStringAsync(LatestApi).ConfigureAwait(false);
                var obj = JObject.Parse(json);
                var tag = (string)obj["tag_name"];
                var url = (string)obj["html_url"];

                var state = LoadState();
                state.LastCheckUtc = DateTime.UtcNow;
                SaveState(state);

                return new UpdateInfo
                {
                    LatestTag = tag,
                    ReleaseUrl = url,
                    Available = IsNewer(tag, CurrentVersion())
                };
            }
        }

        public static Version CurrentVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version ?? new Version(0, 0);
        }

        private static bool IsNewer(string tag, Version current)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return false;

            Version latest;
            var text = tag.Trim().TrimStart('v', 'V');
            return Version.TryParse(text, out latest) && latest > current;
        }

        private static UpdateState LoadState()
        {
            try
            {
                if (File.Exists(StatePath))
                    return JsonConvert.DeserializeObject<UpdateState>(File.ReadAllText(StatePath))
                           ?? new UpdateState();
            }
            catch (Exception)
            {
                // ignored
            }

            return new UpdateState();
        }

        private static void SaveState(UpdateState state)
        {
            try
            {
                var directory = Path.GetDirectoryName(StatePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(StatePath, JsonConvert.SerializeObject(state));
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private sealed class UpdateState
        {
            public DateTime LastCheckUtc { get; set; }
        }
    }
}

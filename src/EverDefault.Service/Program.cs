using System;
using System.Linq;
using System.Threading;
using EverDefault.Core.Os;
using EverDefault.Ipc;
using EverDefault.Persistence;
using EverDefault.Registry.Access;
using EverDefault.Registry.Users;
using EverDefault.Service.Hosting;

namespace EverDefault.Service
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            if (args.Any(a => string.Equals(a, "--ping", StringComparison.OrdinalIgnoreCase)))
            {
                RunPing();
                return;
            }

            if (args.Any(a => string.Equals(a, "--users", StringComparison.OrdinalIgnoreCase)))
            {
                RunUsers();
                return;
            }

            if (args.Any(a => string.Equals(a, "--saverule", StringComparison.OrdinalIgnoreCase)))
            {
                RunSaveRule();
                return;
            }

            if (args.Any(a => string.Equals(a, "--getlog", StringComparison.OrdinalIgnoreCase)))
            {
                RunGetLog();
                return;
            }

            if (args.Any(a => string.Equals(a, "--import", StringComparison.OrdinalIgnoreCase)))
            {
                var index = Array.FindIndex(args, a => string.Equals(a, "--import", StringComparison.OrdinalIgnoreCase));
                RunImport(index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
                return;
            }

            var host = Build();
            var console = Environment.UserInteractive;

            foreach (var arg in args)
            {
                if (string.Equals(arg, "--console", StringComparison.OrdinalIgnoreCase))
                    console = true;
            }

            if (console)
            {
                RunConsole(host);
            }
            else
            {
                System.ServiceProcess.ServiceBase.Run(new EverDefaultWindowsService(host));
            }
        }

        private static void RunConsole(EngineHost host)
        {
            host.Trace += message => Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
            host.Start();

            Console.WriteLine("EverDefault service running (console mode). Press Ctrl+C to stop.");
            var stop = new ManualResetEvent(false);
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                stop.Set();
            };
            stop.WaitOne();

            host.Dispose();
        }

        private static void RunImport(string file)
        {
            if (string.IsNullOrEmpty(file) || !System.IO.File.Exists(file))
            {
                Console.WriteLine("usage: --import <rules.json>");
                return;
            }

            var document = PipeJson.Deserialize<Core.Model.RuleDocument>(System.IO.File.ReadAllText(file));
            if (document == null || document.Rules == null)
            {
                Console.WriteLine("invalid document");
                return;
            }

            var client = new PipeClient();
            var ok = 0;
            foreach (var dto in document.Rules)
            {
                string error;
                var rule = Core.Model.RuleDocumentMapper.ToRule(dto, out error);
                if (rule == null)
                {
                    Console.WriteLine("skip: " + error);
                    continue;
                }

                var response = client.Send(IpcProtocol.CommandSaveRule, RuleCodec.Serialize(rule));
                Console.WriteLine((response.Success ? "ok  " : "FAIL") + " " + rule.Name
                                  + (response.Success ? string.Empty : " -> " + response.Error));
                if (response.Success) ok++;
            }

            Console.WriteLine("imported " + ok + "/" + document.Rules.Count);
        }

        private static void RunGetLog()
        {
            var client = new PipeClient();
            var response = client.Send(IpcProtocol.CommandGetLog, new LogQuery { Max = 50 });
            Console.WriteLine(response.Success ? response.Payload : "FAIL: " + response.Error);
        }

        private static void RunSaveRule()
        {
            var client = new PipeClient();
            var rule = new Core.Model.CustomRegistryRule
            {
                Name = "cli-test",
                Mode = Core.Model.RuleMode.Monitor,
                Action = Core.Model.RuleAction.LogOnly,
                OnMismatch = Core.Model.RuleAction.LogOnly,
                MatchType = Core.Model.ValueMatchType.AnyChange,
                KeyPatterns = new System.Collections.Generic.List<string>
                {
                    @"HKEY_CURRENT_USER\Software\EverDefaultTest"
                }
            };

            var response = client.Send(IpcProtocol.CommandSaveRule, RuleCodec.Serialize(rule));
            Console.WriteLine("saveRule success=" + response.Success + " error=" + (response.Error ?? "-"));

            var list = client.Send(IpcProtocol.CommandGetRules);
            Console.WriteLine("getRules success=" + list.Success);
            Console.WriteLine(list.Payload);

            try
            {
                var parsed = RuleCodec.DeserializeObject<Core.Engine.RuleListResult>(list.Payload);
                Console.WriteLine("parsed rules=" + (parsed == null ? -1 : parsed.Rules.Count)
                                  + " modules=" + string.Join(",", parsed.Rules.ConvertAll(r => r.Module.ToString())));
            }
            catch (Exception ex)
            {
                Console.WriteLine("PARSE FAILED: " + ex.Message);
            }
        }

        private static void RunUsers()
        {
            var settings = new JsonSettingsStore().Load();
            var scope = new UserScope(settings.TargetUserScope);
            Console.WriteLine("TargetUserScope=" + settings.TargetUserScope + " IsSystem=" + scope.IsSystem);

            foreach (var sid in scope.Sids)
            {
                var expanded = string.Join("; ", scope.ExpandCurrentUser(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoice"));
                Console.WriteLine("  " + sid + " -> " + expanded);
            }
        }

        private static void RunPing()
        {
            var client = new PipeClient();
            var response = client.Send(IpcProtocol.CommandGetStatus);
            if (!response.Success)
            {
                Console.WriteLine("FAIL: " + response.Error);
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine(response.Payload);
        }

        private static EngineHost Build()
        {
            var ruleStore = new JsonRuleStore();
            var baselineStore = new JsonBaselineStore();
            var logStore = new JsonChangeLogStore();
            var settingsStore = new JsonSettingsStore();
            var registry = new RegistryAccess();
            var os = OsInfo.Detect();

            return new EngineHost(ruleStore, baselineStore, logStore, settingsStore, registry, os);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;
using EverDefault.Registry.UserChoice;
using EverDefault.Registry.Watching;

namespace EverDefault.Poc.UserChoice
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return 1;
            }

            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "list":
                        return List();
                    case "snapshot":
                        return Snapshot(args);
                    case "verify":
                        return Verify(args);
                    case "watch":
                        return Watch(args);
                    case "tamper":
                        return Tamper(args);
                    case "restore":
                        return Restore(args);
                    case "selftest":
                        return SelfTest(args);
                    case "hashfind":
                        return HashFind(args);
                    case "hashcheck":
                        return HashCheck(args);
                    case "setdefault":
                        return SetDefault(args);
                    case "help":
                    case "-h":
                    case "--help":
                        PrintHelp();
                        return 0;
                    default:
                        Console.WriteLine("Unknown command: " + args[0]);
                        PrintHelp();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
                Console.WriteLine(ex);
                return 2;
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("UserChoice PoC - verifies that a captured (ProgId, Hash, LastWriteTime)");
            Console.WriteLine("triple can be replayed so Windows accepts the association again.");
            Console.WriteLine();
            Console.WriteLine("  list                          List extensions that have a UserChoice key");
            Console.WriteLine("  snapshot <ext> [--out <f>]    Read and print the current UserChoice snapshot");
            Console.WriteLine("  verify <ext>                  Resolve the association with ASSOCF_VERIFY");
            Console.WriteLine("  watch <ext>                   Watch for changes (Ctrl+C / Enter to stop)");
            Console.WriteLine("  tamper <ext> [--out <f>]      Save snapshot then delete UserChoice (simulated attack)");
            Console.WriteLine("  restore <file>                Restore a previously saved snapshot JSON");
            Console.WriteLine("  selftest <ext> [--negative]   Full capture -> tamper -> restore -> verify cycle");
            Console.WriteLine("  hashfind <ext>                Reverse-engineer the hash formula from a live sample");
            Console.WriteLine("  hashcheck <ext>               Compute the hash from a live sample and compare");
            Console.WriteLine("  setdefault <ext> <progId>     Actively set the default association and verify");
            Console.WriteLine();
        }

        private static string GetArg(string[] args, int index, string name)
        {
            if (args.Length <= index)
                throw new ArgumentException("Missing argument: " + name);
            return args[index];
        }

        private static string GetOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        private static int List()
        {
            var manager = new UserChoiceManager();
            var extensions = manager.ListExtensionsWithUserChoice();

            Console.WriteLine("Extensions with a UserChoice key: " + extensions.Count);
            foreach (var ext in extensions)
            {
                var progId = manager.ReadCurrentProgId(ext);
                Console.WriteLine("  {0,-16} -> {1}", ext, progId);
            }

            return 0;
        }

        private static int Snapshot(string[] args)
        {
            var extension = GetArg(args, 1, "<ext>");
            var manager = new UserChoiceManager();
            var snapshot = manager.Capture(extension);

            PrintSnapshot(snapshot);

            var outFile = GetOption(args, "--out");
            if (outFile != null)
            {
                SaveSnapshot(outFile, snapshot);
                Console.WriteLine("Saved to " + Path.GetFullPath(outFile));
            }

            return snapshot.Exists ? 0 : 3;
        }

        private static int Verify(string[] args)
        {
            var extension = UserChoiceManager.NormalizeExtension(GetArg(args, 1, "<ext>"));
            var manager = new UserChoiceManager();
            var snapshot = manager.Capture(extension);

            PrintSnapshot(snapshot);

            if (!snapshot.Exists)
            {
                Console.WriteLine("No UserChoice key. Nothing to verify.");
                return 3;
            }

            var exeNoVerify = AssociationInfo.ResolveExecutable(extension, false);
            var exeVerify = AssociationInfo.ResolveExecutable(extension, true);
            var progIdExe = AssociationInfo.ResolveExecutable(snapshot.ProgId, false);

            Console.WriteLine("  exe (no verify)      : " + (exeNoVerify ?? "<null>"));
            Console.WriteLine("  exe (ASSOCF_VERIFY)  : " + (exeVerify ?? "<null>"));
            Console.WriteLine("  exe of ProgId        : " + (progIdExe ?? "<null>"));
            Console.WriteLine("  verify matches ProgId: " + EqualsPath(exeVerify, progIdExe));

            return EqualsPath(exeVerify, progIdExe) ? 0 : 4;
        }

        private static int Watch(string[] args)
        {
            var extension = UserChoiceManager.NormalizeExtension(GetArg(args, 1, "<ext>"));
            var manager = new UserChoiceManager();
            var baseline = manager.Capture(extension);

            Console.WriteLine("Baseline:");
            PrintSnapshot(baseline);
            Console.WriteLine();
            Console.WriteLine("Watching " + manager.GetExtensionKeyPath(extension) + " (Enter to stop)...");

            using (var watcher = new RegistryWatcher(Microsoft.Win32.Registry.CurrentUser, manager.GetExtensionKeyPath(extension)))
            {
                watcher.Changed += (s, e) =>
                {
                    Thread.Sleep(400); // debounce: writers often do several sets in a row
                    var current = manager.Capture(extension);
                    Console.WriteLine();
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] change detected");
                    PrintDiff(baseline, current);
                    baseline = current;
                };

                watcher.Start();
                Console.ReadLine();
            }

            return 0;
        }

        private static int Tamper(string[] args)
        {
            var extension = UserChoiceManager.NormalizeExtension(GetArg(args, 1, "<ext>"));
            var manager = new UserChoiceManager();
            var original = manager.Capture(extension);

            if (!original.Exists)
            {
                Console.WriteLine("Nothing to tamper with: no UserChoice key for " + extension);
                return 3;
            }

            var outFile = GetOption(args, "--out") ?? ("userchoice-backup" + extension + ".json");
            SaveSnapshot(outFile, original);
            Console.WriteLine("Saved backup -> " + Path.GetFullPath(outFile));

            var deleted = manager.Delete(extension);
            Console.WriteLine("Delete UserChoice: " + (deleted ? "done" : "no-op"));

            var after = manager.Capture(extension);
            PrintSnapshot(after);

            return after.Exists ? 5 : 0;
        }

        private static int Restore(string[] args)
        {
            var file = GetArg(args, 1, "<file>");
            var snapshot = LoadSnapshot(file);
            var manager = new UserChoiceManager();

            var result = manager.Restore(snapshot);
            Console.WriteLine("Restore: " + result);

            var after = manager.Capture(snapshot.Extension);
            PrintSnapshot(after);

            return result.Success && ValuesEqual(snapshot, after) ? 0 : 6;
        }

        private static int SelfTest(string[] args)
        {
            var extension = UserChoiceManager.NormalizeExtension(GetArg(args, 1, "<ext>"));
            var negative = GetOption(args, "--negative") != null || Array.IndexOf(args, "--negative") >= 0;
            var manager = new UserChoiceManager();

            var original = manager.Capture(extension);
            if (!original.Exists)
            {
                Console.WriteLine("Cannot self-test: no UserChoice key for " + extension);
                Console.WriteLine("Open the file type in Windows Settings first to give it a UserChoice.");
                return 3;
            }

            var backupFile = "userchoice-backup" + extension + ".json";
            SaveSnapshot(backupFile, original);
            Console.WriteLine("Backup saved -> " + Path.GetFullPath(backupFile));
            Console.WriteLine();
            Console.WriteLine("Original:");
            PrintSnapshot(original);

            var pass = true;

            try
            {
                // 1. Simulated attack: delete the key.
                Console.WriteLine();
                Console.WriteLine("[1] Tampering (delete UserChoice)...");
                if (!manager.Delete(extension))
                {
                    Console.WriteLine("    FAIL: could not delete UserChoice");
                    return 5;
                }

                var afterTamper = manager.Capture(extension);
                var tamperDetected = !afterTamper.Exists;
                Console.WriteLine("    key gone: " + tamperDetected);
                pass &= tamperDetected;

                // 2. Restore from the snapshot.
                Console.WriteLine();
                Console.WriteLine("[2] Restoring from snapshot...");
                var result = manager.Restore(original);
                Console.WriteLine("    " + result);
                if (!result.Success)
                    pass = false;

                var afterRestore = manager.Capture(extension);
                Console.WriteLine("    values match : " + ValuesEqual(original, afterRestore));
                Console.WriteLine("    time match   : " + (original.LastWriteTimeUtc == afterRestore.LastWriteTimeUtc));
                pass &= ValuesEqual(original, afterRestore);
                pass &= original.LastWriteTimeUtc == afterRestore.LastWriteTimeUtc;

                // 3. Does Windows actually accept it (hash valid)?
                Console.WriteLine();
                Console.WriteLine("[3] Validating with ASSOCF_VERIFY...");
                var progIdExe = AssociationInfo.ResolveExecutable(original.ProgId, false);
                var verifiedExe = AssociationInfo.ResolveExecutable(extension, true);
                var accepted = EqualsPath(progIdExe, verifiedExe);
                Console.WriteLine("    ProgId exe          : " + (progIdExe ?? "<null>"));
                Console.WriteLine("    verified exe        : " + (verifiedExe ?? "<null>"));
                Console.WriteLine("    Windows accepts it  : " + accepted);
                pass &= accepted;

                // 4. Negative control: break the last write time, VERIFY should fail.
                if (negative)
                {
                    Console.WriteLine();
                    Console.WriteLine("[4] Negative control (break LastWriteTime)...");
                    var badTime = original.LastWriteTimeUtc + TimeSpan.TicksPerHour;
                    var timeSet = manager.SetLastWriteTime(extension, badTime);
                    var badCaptured = manager.Capture(extension);
                    var badExe = AssociationInfo.ResolveExecutable(extension, true);
                    var brokenDetected = !EqualsPath(progIdExe, badExe) || !badCaptured.Exists;

                    Console.WriteLine("    set bad time        : " + timeSet);
                    Console.WriteLine("    captured time now   : " + (badCaptured.Exists ? badCaptured.LastWriteTimeUtc.ToString() : "<gone>"));
                    Console.WriteLine("    verified exe        : " + (badExe ?? "<null>"));
                    Console.WriteLine("    VERIFY now rejects  : " + brokenDetected);
                    pass &= brokenDetected;

                    // restore the good state again
                    var fix = manager.Restore(original);
                    Console.WriteLine("    re-restored         : " + fix);
                    pass &= fix.Success;
                }
            }
            finally
            {
                // Safety net: never leave the user's association broken.
                var current = manager.Capture(extension);
                if (!ValuesEqual(original, current) || original.LastWriteTimeUtc != current.LastWriteTimeUtc)
                {
                    Console.WriteLine();
                    Console.WriteLine("Safety restore (finally)...");
                    var final = manager.Restore(original);
                    Console.WriteLine("  " + final);
                }
            }

            Console.WriteLine();
            Console.WriteLine(pass ? "RESULT: PASS" : "RESULT: FAIL");
            Console.WriteLine("Backup kept at " + Path.GetFullPath(backupFile));
            return pass ? 0 : 7;
        }

        private static int HashCheck(string[] args)
        {
            var extension = UserChoiceManager.NormalizeExtension(GetArg(args, 1, "<ext>"));
            var manager = new UserChoiceManager();
            var snapshot = manager.Capture(extension);

            if (!snapshot.Exists || snapshot.Hash == null)
            {
                Console.WriteLine("No UserChoice/Hash for " + extension);
                return 3;
            }

            var sid = WindowsIdentity.GetCurrent().User.Value;
            var computed = UserChoiceHashAlgorithm.Compute(extension, sid, snapshot.ProgId, snapshot.LastWriteTimeUtc);

            Console.WriteLine("  writeTimeStr : " + UserChoiceHashAlgorithm.FormatWriteTime(snapshot.LastWriteTimeUtc));
            Console.WriteLine("  sample hash  : " + snapshot.Hash);
            Console.WriteLine("  computed hash: " + computed);
            Console.WriteLine(computed == snapshot.Hash ? "  MATCH" : "  NO MATCH");

            return computed == snapshot.Hash ? 0 : 4;
        }

        private static int SetDefault(string[] args)
        {
            var extension = UserChoiceManager.NormalizeExtension(GetArg(args, 1, "<ext>"));
            var progId = GetArg(args, 2, "<progId>");
            var manager = new UserChoiceManager();

            Console.WriteLine("Before:");
            PrintSnapshot(manager.Capture(extension));

            var sid = WindowsIdentity.GetCurrent().User.Value;
            var now = DateTime.UtcNow;
            var fileTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc).ToFileTimeUtc();

            var hash = UserChoiceHashAlgorithm.Compute(extension, sid, progId, fileTime);
            Console.WriteLine("Computed hash: " + hash);

            var result = manager.WriteRaw(extension, progId, hash, fileTime);
            Console.WriteLine("Write: " + result);

            Console.WriteLine("After:");
            PrintSnapshot(manager.Capture(extension));

            var verifiedExe = AssociationInfo.ResolveExecutable(extension, true);
            var progIdExe = AssociationInfo.ResolveExecutable(progId, false);
            Console.WriteLine("  ASSOCF_VERIFY exe : " + (verifiedExe ?? "<null>"));
            Console.WriteLine("  ProgId exe        : " + (progIdExe ?? "<null>"));

            var ok = EqualsPath(verifiedExe, progIdExe);
            Console.WriteLine(ok ? "RESULT: default set and verified" : "RESULT: verification failed");
            return ok ? 0 : 4;
        }

        private static int HashFind(string[] args)
        {
            var extension = UserChoiceManager.NormalizeExtension(GetArg(args, 1, "<ext>"));
            var manager = new UserChoiceManager();
            var snapshot = manager.Capture(extension);

            if (!snapshot.Exists || snapshot.Hash == null)
            {
                Console.WriteLine("No UserChoice/Hash for " + extension + "; cannot reverse-engineer.");
                return 3;
            }

            var sid = WindowsIdentity.GetCurrent().User.Value;
            Console.WriteLine("Reverse-engineering UserChoice hash");
            Console.WriteLine("  ext        : " + extension);
            Console.WriteLine("  progId     : " + snapshot.ProgId);
            Console.WriteLine("  sid        : " + sid);
            Console.WriteLine("  fileTime   : " + snapshot.LastWriteTimeUtc);
            Console.WriteLine("  expected   : " + snapshot.Hash);
            Console.WriteLine();

            var sw = Stopwatch.StartNew();
            var matches = UserChoiceHash.BruteForce(snapshot.Hash, extension, sid, snapshot.ProgId, snapshot.LastWriteTimeUtc);
            sw.Stop();

            Console.WriteLine("Searched in " + sw.ElapsedMilliseconds + " ms, matches=" + matches.Count);
            foreach (var match in matches.Take(20))
                Console.WriteLine("  MATCH: " + match.Description);

            return matches.Count > 0 ? 0 : 4;
        }

        private static bool ValuesEqual(UserChoiceSnapshot a, UserChoiceSnapshot b)
        {
            if (a == null || b == null)
                return false;

            return a.Exists == b.Exists
                   && string.Equals(a.ProgId, b.ProgId, StringComparison.Ordinal)
                   && string.Equals(a.Hash, b.Hash, StringComparison.Ordinal);
        }

        private static bool EqualsPath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static void PrintSnapshot(UserChoiceSnapshot snapshot)
        {
            if (!snapshot.Exists)
            {
                Console.WriteLine("  " + snapshot.Extension + ": <no UserChoice key>");
                return;
            }

            Console.WriteLine("  Extension     : " + snapshot.Extension);
            Console.WriteLine("  ProgId        : " + (snapshot.ProgId ?? "<null>"));
            Console.WriteLine("  Hash          : " + (snapshot.Hash ?? "<null>"));
            Console.WriteLine("  LastWriteTime : " + snapshot.LastWriteTimeUtc
                              + " (" + DateTime.FromFileTimeUtc(snapshot.LastWriteTimeUtc).ToString("o") + " UTC)");
        }

        private static void PrintDiff(UserChoiceSnapshot before, UserChoiceSnapshot after)
        {
            if (!after.Exists)
            {
                Console.WriteLine("  key removed (was " + (before.ProgId ?? "<null>") + ")");
                return;
            }

            if (!before.Exists)
            {
                Console.WriteLine("  key added: " + after.ProgId);
                return;
            }

            Console.WriteLine("  ProgId : {0} -> {1}", before.ProgId, after.ProgId);
            Console.WriteLine("  Hash   : {0} -> {1}", Short(before.Hash), Short(after.Hash));
            Console.WriteLine("  Time   : {0} -> {1}", before.LastWriteTimeUtc, after.LastWriteTimeUtc);
        }

        private static string Short(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "<null>";
            return value.Length <= 12 ? value : value.Substring(0, 12) + "...";
        }

        private static void SaveSnapshot(string file, UserChoiceSnapshot snapshot)
        {
            var serializer = new JavaScriptSerializer();
            File.WriteAllText(file, serializer.Serialize(snapshot));
        }

        private static UserChoiceSnapshot LoadSnapshot(string file)
        {
            var serializer = new JavaScriptSerializer();
            return serializer.Deserialize<UserChoiceSnapshot>(File.ReadAllText(file));
        }
    }
}

using System;
using System.IO;

namespace EverDefault.App
{
    /// <summary>
    /// A per-user marker so the wizard is not shown again when the shared settings file
    /// is not writable (for example a portable build run without administrator rights).
    /// </summary>
    internal static class OnboardingState
    {
        private static string FilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EverDefault",
                    "onboarding.flag");
            }
        }

        public static bool IsCompleted
        {
            get
            {
                try
                {
                    return File.Exists(FilePath);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static void MarkCompleted()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, "1");
            }
            catch (Exception)
            {
                // Best-effort only.
            }
        }
    }
}

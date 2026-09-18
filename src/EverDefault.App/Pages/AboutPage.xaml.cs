using System;
using System.Reflection;
using System.Windows.Controls;

namespace EverDefault.App
{
    public partial class AboutPage : UserControl, IAppPage
    {
        public AboutPage()
        {
            InitializeComponent();
            VersionText.Text = "界面版本 " + AppVersion();
        }

        public void ApplySnapshot(RefreshSnapshot snapshot)
        {
            if (snapshot.ServiceRunning && snapshot.Status != null
                && !string.IsNullOrEmpty(snapshot.Status.Version))
            {
                VersionText.Text = "界面版本 " + AppVersion()
                    + "   |   服务版本 " + snapshot.Status.Version
                    + "   |   " + snapshot.Status.OsDescription;
            }
        }

        private static string AppVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version == null ? "未知" : version.ToString();
        }
    }
}

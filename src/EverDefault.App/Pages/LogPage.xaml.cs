using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EverDefault.App
{
    public partial class LogPage : UserControl, IAppPage
    {
        private bool _serviceRunning;

        public LogPage()
        {
            InitializeComponent();
        }

        public void ApplySnapshot(RefreshSnapshot snapshot)
        {
            _serviceRunning = snapshot.ServiceRunning;

            if (!snapshot.ServiceRunning)
            {
                Hint.Text = "后台服务未运行，暂时没有日志。请到“主页”启动或安装服务。";
            }
            else
            {
                Hint.Text = "记录每条规则的触发与处理结果，最新的在最上面。";
            }

            if (snapshot.Logs == null)
                return;

            var scroll = CaptureScroll();
            LogGrid.ItemsSource = snapshot.Logs;
            RestoreScroll(scroll);
        }

        private ScrollState CaptureScroll()
        {
            var viewer = FindScrollViewer(LogGrid);
            return viewer == null
                ? null
                : new ScrollState { Vertical = viewer.VerticalOffset, Horizontal = viewer.HorizontalOffset };
        }

        private void RestoreScroll(ScrollState state)
        {
            if (state == null)
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                var viewer = FindScrollViewer(LogGrid);
                if (viewer == null)
                    return;

                viewer.ScrollToVerticalOffset(state.Vertical);
                viewer.ScrollToHorizontalOffset(state.Horizontal);
            }));
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer)
                return (ScrollViewer)root;

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}

using System;
using System.Security.AccessControl;
using System.Threading;
using EverDefault.Registry.Native;
using Microsoft.Win32;

namespace EverDefault.Registry.Watching
{
    /// <summary>
    /// Event-driven registry watcher built on RegNotifyChangeKeyValue.
    /// No polling: the OS signals a manual-reset event when the watched key (or its
    /// subtree) changes. The API does not report *what* changed, so callers must
    /// re-read the key and diff against their baseline after every notification.
    /// </summary>
    public sealed class RegistryWatcher : IDisposable
    {
        private readonly RegistryKey _root;
        private readonly string _displayPath;
        private readonly ManualResetEvent _cancel = new ManualResetEvent(false);

        private Thread _thread;
        private volatile bool _running;

        public event EventHandler Changed;

        public RegistryWatcher(RegistryKey hive, string subKeyPath, int notifyFilter)
        {
            if (hive == null) throw new ArgumentNullException(nameof(hive));

            _root = hive.OpenSubKey(
                subKeyPath,
                RegistryKeyPermissionCheck.ReadSubTree,
                RegistryRights.Notify | RegistryRights.ReadKey);

            if (_root == null)
                throw new InvalidOperationException("Registry key not found: " + subKeyPath);

            _displayPath = subKeyPath;
            NotifyFilter = notifyFilter;
        }

        public RegistryWatcher(RegistryKey hive, string subKeyPath)
            : this(
                hive,
                subKeyPath,
                NativeMethods.REG_NOTIFY_CHANGE_LAST_SET | NativeMethods.REG_NOTIFY_CHANGE_NAME)
        {
        }

        public int NotifyFilter { get; private set; }

        public string DisplayPath
        {
            get { return _displayPath; }
        }

        public void Start()
        {
            if (_running)
                return;

            _running = true;
            _thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "RegWatch:" + _displayPath
            };
            _thread.Start();
        }

        private void Loop()
        {
            using (var changeEvent = new ManualResetEvent(false))
            {
                var handles = new WaitHandle[] { changeEvent, _cancel };

                while (_running)
                {
                    changeEvent.Reset();

                    int rc = NativeMethods.RegNotifyChangeKeyValue(
                        _root.Handle,
                        true,
                        NotifyFilter,
                        changeEvent.SafeWaitHandle.DangerousGetHandle(),
                        true);

                    if (!_running)
                        break;

                    if (rc != NativeMethods.ERROR_SUCCESS)
                    {
                        // Key may have been deleted; back off and retry.
                        if (WaitHandle.WaitAny(handles, 1000) == 1)
                            break;
                        continue;
                    }

                    if (WaitHandle.WaitAny(handles) == 1)
                        break;

                    OnChanged();
                }
            }
        }

        private void OnChanged()
        {
            var handler = Changed;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _running = false;
            _cancel.Set();

            try
            {
                if (_thread != null && !_thread.Join(2000))
                {
                    // Detach so a stuck native wait does not keep the process alive.
                }
            }
            catch (ThreadStateException)
            {
            }

            _root?.Close();
            _cancel.Dispose();
        }
    }
}

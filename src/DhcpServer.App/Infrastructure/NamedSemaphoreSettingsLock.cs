using System;
using System.Threading;
using DhcpServer.Core.Abstractions;

namespace DhcpServer.App.Infrastructure
{
    /// <summary>
    /// Lets one program instance at a time change adapter settings (FR-015). A named semaphore is used
    /// instead of a mutex because applying runs on a worker thread and restoring on another one — a mutex
    /// must be released by the thread that took it. The OS drops the object when the owning process dies,
    /// so a crashed instance never leaves the lock behind.
    /// </summary>
    internal sealed class NamedSemaphoreSettingsLock : ISettingsLock, IDisposable
    {
        private const string Name = "DhcpServer.AddressSettings";
        private readonly Semaphore _semaphore;
        private readonly object _sync = new object();
        private bool _held;

        public NamedSemaphoreSettingsLock()
        {
            try
            {
                _semaphore = new Semaphore(1, 1, @"Global\" + Name);
            }
            catch (UnauthorizedAccessException)
            {
                _semaphore = new Semaphore(1, 1, @"Local\" + Name);
            }
        }

        public bool TryAcquire()
        {
            lock (_sync)
            {
                if (_held)
                {
                    return false;
                }

                _held = _semaphore.WaitOne(0);
                return _held;
            }
        }

        public void Release()
        {
            lock (_sync)
            {
                if (_held)
                {
                    _held = false;
                    _semaphore.Release();
                }
            }
        }

        public void Dispose()
        {
            Release();
            _semaphore.Dispose();
        }
    }
}

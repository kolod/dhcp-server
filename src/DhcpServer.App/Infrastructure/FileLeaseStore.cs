using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Leases;

namespace DhcpServer.App.Infrastructure
{
    /// <summary>
    /// Leases in <c>DhcpServer.leases</c> next to the executable (contracts/lease-file.md). Saves go to
    /// a temporary file that replaces the real one. IO errors are thrown to the caller, which treats
    /// them as non-fatal.
    /// </summary>
    internal sealed class FileLeaseStore : ILeaseStore
    {
        public const string FileName = "DhcpServer.leases";

        private readonly string _path;

        public FileLeaseStore(string path)
        {
            _path = path;
        }

        public static FileLeaseStore NextToExecutable()
        {
            return new FileLeaseStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName));
        }

        public IReadOnlyList<Lease> Load()
        {
            if (!File.Exists(_path))
            {
                return new Lease[0];
            }

            return LeaseFile.Parse(File.ReadAllText(_path, Encoding.UTF8));
        }

        public void Save(IReadOnlyList<Lease> leases)
        {
            var temp = _path + ".tmp";
            try
            {
                File.WriteAllText(temp, LeaseFile.Serialize(leases, DateTime.UtcNow), new UTF8Encoding(false));
                if (File.Exists(_path))
                {
                    File.Replace(temp, _path, null);
                }
                else
                {
                    File.Move(temp, _path);
                }
            }
            catch (Exception)
            {
                TryDelete(temp);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

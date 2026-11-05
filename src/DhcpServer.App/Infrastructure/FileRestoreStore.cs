using System;
using System.IO;
using System.Text;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Addressing;

namespace DhcpServer.App.Infrastructure
{
    /// <summary>
    /// <c>DhcpServer.restore</c> next to the executable (contracts/restore-file.md). Unlike settings and
    /// leases, a failed save here is fatal for applying: it throws, and the configurator then changes
    /// nothing.
    /// </summary>
    internal sealed class FileRestoreStore : IRestoreStore
    {
        public const string FileName = "DhcpServer.restore";

        private readonly string _path;

        public FileRestoreStore(string path)
        {
            _path = path;
        }

        public static FileRestoreStore NextToExecutable()
        {
            return new FileRestoreStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName));
        }

        public SavedConfiguration Load()
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return RestoreFile.Parse(File.ReadAllText(_path, Encoding.UTF8));
        }

        public void Save(SavedConfiguration record)
        {
            var temp = _path + ".tmp";
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(RestoreFile.Serialize(record));
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true); // on disk before the first change (power loss)
                }

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

        public void Delete()
        {
            TryDelete(_path + ".tmp");
            if (File.Exists(_path))
            {
                File.Delete(_path);
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

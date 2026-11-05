using System;
using System.IO;
using System.Linq;

namespace DhcpServer.Tests
{
    internal static class TestPaths
    {
        /// <summary>Repository root: the first parent folder that holds global.json.</summary>
        public static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (dir != null && !dir.GetFiles("global.json").Any())
                {
                    dir = dir.Parent;
                }

                if (dir == null)
                {
                    throw new InvalidOperationException("Repository root (global.json) not found.");
                }

                return dir.FullName;
            }
        }

        public static string AppProject => Path.Combine(RepoRoot, "src", "DhcpServer.App");
    }
}

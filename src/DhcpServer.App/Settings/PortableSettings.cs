using System;
using System.IO;
using System.Text;
using DhcpServer.Core.Settings;

namespace DhcpServer.App.Settings
{
    /// <summary>
    /// Settings stored in DhcpServer.ini next to the executable (portable: no registry, no
    /// AppData). Failing to read or write the file is never fatal.
    /// </summary>
    internal sealed class PortableSettings
    {
        private const string LanguageKey = "Language";
        private const string LastInterfaceKey = "LastInterface";
        private readonly string _path;
        private readonly IniDocument _doc;

        private PortableSettings(string path, IniDocument doc)
        {
            _path = path;
            _doc = doc;
        }

        public string Language
        {
            get { return _doc.Get(LanguageKey); }
            set { _doc.Set(LanguageKey, value); }
        }

        /// <summary>Id of the interface chosen last time (preselected, never auto-started).</summary>
        public string LastInterface
        {
            get { return _doc.Get(LastInterfaceKey); }
            set { _doc.Set(LastInterfaceKey, value); }
        }

        public static PortableSettings Load()
        {
            return Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DhcpServer.ini"));
        }

        public static PortableSettings Load(string path)
        {
            string text = null;
            try
            {
                if (File.Exists(path))
                {
                    text = File.ReadAllText(path, Encoding.UTF8);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return new PortableSettings(path, IniDocument.Parse(text));
        }

        /// <summary>Returns false when the file cannot be written (e.g. read-only media).</summary>
        public bool TrySave()
        {
            try
            {
                File.WriteAllText(_path, _doc.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}

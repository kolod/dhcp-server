using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;

namespace DhcpServer.App.Localization
{
    /// <summary>
    /// Loads the string tables embedded in the executable (DhcpServer.Strings.&lt;code&gt;.resources)
    /// and tracks the current UI language. English is the fallback for missing entries.
    /// </summary>
    internal static class LanguageService
    {
        public const string DefaultCode = "en";
        private const string ResourcePrefix = "DhcpServer.Strings.";

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, Dictionary<string, string>> Tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Supported UI languages (two-letter codes); English first.</summary>
        public static IReadOnlyList<string> Supported { get; } = new[] { "en", "uk", "de", "fr", "es", "ja" };

        public static event EventHandler LanguageChanged;

        public static string Current { get; private set; } = DefaultCode;

        public static CultureInfo Culture => CultureInfo.GetCultureInfo(Current);

        public static bool IsSupported(string code)
        {
            return Supported.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Language name in that language, e.g. "Deutsch", "日本語".</summary>
        public static string NativeName(string code)
        {
            var culture = CultureInfo.GetCultureInfo(code);
            var name = culture.NativeName;
            return name.Length == 0 ? code : char.ToUpper(name[0], culture) + name.Substring(1);
        }

        /// <summary>The OS UI language if supported, otherwise English.</summary>
        public static string DetectOsLanguage()
        {
            foreach (var culture in new[] { CultureInfo.CurrentUICulture, CultureInfo.InstalledUICulture })
            {
                var code = culture.TwoLetterISOLanguageName;
                if (IsSupported(code))
                {
                    return code.ToLowerInvariant();
                }
            }

            return DefaultCode;
        }

        public static void SetLanguage(string code)
        {
            if (!IsSupported(code))
            {
                code = DefaultCode;
            }

            code = code.ToLowerInvariant();
            var culture = CultureInfo.GetCultureInfo(code);
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            if (string.Equals(Current, code, StringComparison.Ordinal))
            {
                return;
            }

            Current = code;
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        public static string Get(string key)
        {
            string value;
            if (Table(Current).TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }

            if (Table(DefaultCode).TryGetValue(key, out value))
            {
                return value;
            }

            // Visible marker instead of a crash: a missing key is a bug the tests should catch.
            return "[" + key + "]";
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(Culture, Get(key), args);
        }

        private static Dictionary<string, string> Table(string code)
        {
            lock (Sync)
            {
                Dictionary<string, string> table;
                if (Tables.TryGetValue(code, out table))
                {
                    return table;
                }

                table = new Dictionary<string, string>(StringComparer.Ordinal);
                var stream = typeof(LanguageService).Assembly.GetManifestResourceStream(ResourcePrefix + code + ".resources");
                if (stream != null)
                {
                    using (var reader = new ResourceReader(stream))
                    {
                        foreach (DictionaryEntry entry in reader)
                        {
                            table[(string)entry.Key] = entry.Value as string;
                        }
                    }
                }

                Tables[code] = table;
                return table;
            }
        }
    }
}

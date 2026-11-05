using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace DhcpServer.Tests.Localization
{
    /// <summary>
    /// Guards the localization rules of the constitution: every language has every string, no
    /// string is empty, format placeholders match English, and XAML has no hard-coded text.
    /// </summary>
    public class LocalizationTests
    {
        private static readonly string[] Languages = { "en", "uk", "de", "fr", "es", "ja" };

        private static string ResxPath(string code)
        {
            var name = code == "en" ? "Strings.resx" : "Strings." + code + ".resx";
            return Path.Combine(TestPaths.AppProject, "Resources", name);
        }

        private static Dictionary<string, string> Load(string code)
        {
            return XDocument.Load(ResxPath(code)).Root
                .Elements("data")
                .ToDictionary(d => (string)d.Attribute("name"), d => (string)d.Element("value") ?? string.Empty);
        }

        public static IEnumerable<object[]> NonEnglish()
        {
            return Languages.Where(c => c != "en").Select(c => new object[] { c });
        }

        [Fact]
        public void EveryLanguage_HasAStringTable()
        {
            foreach (var code in Languages)
            {
                Assert.True(File.Exists(ResxPath(code)), "Missing " + ResxPath(code));
            }
        }

        [Theory]
        [MemberData(nameof(NonEnglish))]
        public void Language_HasExactlyTheEnglishKeys(string code)
        {
            var en = Load("en");
            var other = Load(code);
            Assert.Empty(en.Keys.Except(other.Keys));
            Assert.Empty(other.Keys.Except(en.Keys));
        }

        [Fact]
        public void NoString_IsEmpty()
        {
            foreach (var code in Languages)
            {
                var empty = Load(code).Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ToList();
                Assert.True(empty.Count == 0, code + ": empty " + string.Join(", ", empty));
            }
        }

        [Theory]
        [MemberData(nameof(NonEnglish))]
        public void FormatPlaceholders_MatchEnglish(string code)
        {
            var en = Load("en");
            foreach (var pair in Load(code))
            {
                string english;
                if (en.TryGetValue(pair.Key, out english))
                {
                    Assert.True(Placeholders(english).SetEquals(Placeholders(pair.Value)), code + ": placeholders differ in " + pair.Key);
                }
            }
        }

        [Fact]
        public void Xaml_HasNoHardCodedText()
        {
            var attribute = new Regex(@"\b(Content|Text|Title|Header|ToolTip|Watermark)=""(?<v>[^""]*)""");
            var problems = new List<string>();
            foreach (var file in Directory.GetFiles(TestPaths.AppProject, "*.xaml", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                    || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                {
                    continue;
                }

                foreach (Match m in attribute.Matches(File.ReadAllText(file)))
                {
                    var value = m.Groups["v"].Value;
                    if (value.Length > 0 && !value.StartsWith("{"))
                    {
                        problems.Add(Path.GetFileName(file) + ": " + m.Value);
                    }
                }
            }

            Assert.True(problems.Count == 0, "Use {l:Loc Key}: " + string.Join("; ", problems));
        }

        private static HashSet<string> Placeholders(string text)
        {
            return new HashSet<string>(Regex.Matches(text, @"\{(\d+)[^}]*\}").Cast<Match>().Select(m => m.Groups[1].Value));
        }
    }
}

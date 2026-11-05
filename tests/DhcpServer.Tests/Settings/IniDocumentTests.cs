using DhcpServer.Core.Settings;
using Xunit;

namespace DhcpServer.Tests.Settings
{
    public class IniDocumentTests
    {
        [Fact]
        public void Get_ReturnsNull_ForMissingKey()
        {
            Assert.Null(IniDocument.Parse("A=1").Get("B"));
        }

        [Fact]
        public void Get_IsCaseInsensitive_AndTrims()
        {
            Assert.Equal("de", IniDocument.Parse("  language = de ").Get("Language"));
        }

        [Fact]
        public void Set_ReplacesExistingKey_AndKeepsOtherLines()
        {
            var doc = IniDocument.Parse("; comment\r\nLanguage=en\r\nOther=x");
            doc.Set("Language", "uk");
            Assert.Equal("; comment\r\nLanguage=uk\r\nOther=x\r\n", doc.ToString());
        }

        [Fact]
        public void Set_AppendsNewKey()
        {
            var doc = IniDocument.Parse(null);
            doc.Set("Language", "ja");
            Assert.Equal("Language=ja\r\n", doc.ToString());
        }

        [Fact]
        public void Set_StripsLineBreaksFromValue()
        {
            var doc = IniDocument.Parse(string.Empty);
            doc.Set("K", "a\r\nInjected=1");
            Assert.Null(IniDocument.Parse(doc.ToString()).Get("Injected"));
        }

        [Fact]
        public void Comments_AndSections_AreNotKeys()
        {
            var doc = IniDocument.Parse("#A=1\r\n;B=2\r\n[C=3]");
            Assert.Null(doc.Get("#A"));
            Assert.Null(doc.Get("A"));
            Assert.Null(doc.Get("B"));
        }
    }
}

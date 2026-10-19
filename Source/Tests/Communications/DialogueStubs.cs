using System;
using System.Collections.Generic;

// Test the production dialogue tree and deferred choices without a Unity window.
namespace Verse
{
    public sealed class DiaNode
    {
        public readonly string text;
        public readonly List<DiaOption> options = new List<DiaOption>();
        public DiaNode(string text) { this.text = text; }
    }

    public sealed class DiaOption
    {
        public readonly string text;
        public DiaNode link;
        public Func<DiaNode> linkLateBind;
        public bool resolveTree;
        public DiaOption(string text) { this.text = text; }
    }

    public static class TranslationStub
    {
        public static string Language = "English";
        public static string Translate(this string key, params object[] arguments)
        {
            if (key.StartsWith("HD_Company_Name_"))
                return (string)System.Xml.Linq.XDocument.Load("Languages/" + Language + "/Keyed/TelegraphCompanies.xml").Root.Element(key) ?? key;
            return key + (arguments.Length == 0 ? "" : ": " + string.Join(", ", arguments));
        }
    }
}

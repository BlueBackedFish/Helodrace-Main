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
        public static string Translate(this string key, params object[] arguments)
            => key + (arguments.Length == 0 ? "" : ": " + string.Join(", ", arguments));
    }
}

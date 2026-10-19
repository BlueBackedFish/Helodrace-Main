using System;
using Verse;

namespace Helodrace
{
    public static class HamRadioDialog
    {
        public static DiaNode Create(string factionName, Func<string> chatter)
        {
            var root = new DiaNode("HD_HAM_Greeting".Translate(factionName));
            root.options.Add(new DiaOption("HD_HAM_Chat".Translate())
            {
                linkLateBind = () => Reply(root, chatter())
            });
            root.options.Add(Disconnect());
            return root;
        }

        private static DiaNode Reply(DiaNode root, string text)
        {
            var reply = new DiaNode(text);
            reply.options.Add(new DiaOption("GoBack".Translate()) { link = root });
            reply.options.Add(Disconnect());
            return reply;
        }

        private static DiaOption Disconnect()
            => new DiaOption("(" + "Disconnect".Translate() + ")") { resolveTree = true };
    }
}

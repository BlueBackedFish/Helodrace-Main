using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Helodrace.ModernWar
{
    internal static class ModularWeaponChildTransfer
    {
        private sealed class Attachment
        {
            public Thing Thing;
            public string Socket, Mount;
            public float Offset;
        }

        // The caller has already installed the replacement. Return every unmatched
        // child for storage; never leave half a transferred rail on the new part.
        public static List<Thing> Move(CompModularWeaponNode previous, CompModularWeaponNode replacement)
        {
            var removed = new List<Thing>();
            if (previous == null || replacement == null) return removed;
            var attachments = new List<Attachment>();
            while (previous.ChildCount > 0)
            {
                var record = new Attachment { Socket = previous.SocketIdAt(0),
                    Mount = previous.MountIdAt(0), Offset = previous.RailOffsetAt(0) };
                record.Thing = previous.DetachChildAt(0);
                attachments.Add(record);
            }
            foreach (var group in attachments.GroupBy(record => record.Socket))
            {
                ModularAttachmentSocket socket = replacement.Props.SocketNamed(group.Key);
                var moved = new List<Thing>();
                bool complete = socket != null;
                foreach (Attachment record in group)
                {
                    if (!complete || !TryMove(replacement, socket, record))
                    {
                        complete = false;
                        continue;
                    }
                    moved.Add(record.Thing);
                }
                if (complete) continue;
                foreach (Thing thing in moved) replacement.DetachChild(thing);
                removed.AddRange(group.Select(record => record.Thing));
            }
            return removed;
        }

        private static bool TryMove(CompModularWeaponNode replacement,
            ModularAttachmentSocket socket, Attachment record)
        {
            CompModularWeaponNode child = record.Thing.TryGetComp<CompModularWeaponNode>();
            ModularAttachmentMount mount = child?.Props.MountNamed(record.Mount);
            if (mount?.Accepts(socket) != true)
                mount = child?.Props.mounts.FirstOrDefault(candidate => candidate.Accepts(socket));
            if (mount == null) return false;
            string reason;
            if (replacement.TryAttach(record.Thing, socket.id, mount.id,
                socket.isRail ? record.Offset : 0f, out reason)) return true;
            float offset;
            return socket.isRail
                && replacement.TryFindAttachOffset(record.Thing, socket.id, mount.id, out offset, out reason)
                && replacement.TryAttach(record.Thing, socket.id, mount.id, offset, out reason);
        }
    }
}

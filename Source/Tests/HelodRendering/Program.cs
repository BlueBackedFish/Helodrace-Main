using System;
using System.IO;
using System.Xml.Linq;
using Helodrace;
using Verse;

internal static class Program
{
    private static void Main()
    {
        var pawn = new Pawn();
        var graphic = new Graphic();
        var node = new PawnRenderNode_HelodStump(pawn,
            new PawnRenderNodeProperties { graphic = graphic }, new PawnRenderTree());
        if (node.GraphicFor(pawn) != null)
            throw new Exception("An intact head must hide the stump.");
        pawn.health.hediffSet.HasHead = false;
        if (!ReferenceEquals(node.GraphicFor(pawn), graphic))
            throw new Exception("A missing head must use the configured stump graphic.");
        pawn.health.hediffSet.HasHead = true;
        if (node.GraphicFor(pawn) != null)
            throw new Exception("Restoring the head must hide the stump again.");

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Defs")))
            directory = directory.Parent;
        if (directory == null) throw new Exception("Repository root not found.");
        var tree = XDocument.Load(Path.Combine(directory.FullName,
            "Defs/Helod/Appearance/PawnRenderTree.xml"));
        var stump = tree.Root.Element("PawnRenderTreeDef").Element("root")
            .Element("children").Elements("li");
        bool bound = false;
        foreach (var entry in stump)
            if ((string)entry.Element("debugLabel") == "Head stump")
                bound = (string)entry.Element("nodeClass") == typeof(PawnRenderNode_HelodStump).FullName
                    && (string)entry.Element("workerClass") == "PawnRenderNodeWorker_Stump"
                    && !string.IsNullOrEmpty((string)entry.Element("texPath"));
        if (!bound) throw new Exception("Render tree must bind the standalone stump and retain its worker and texture.");
        Console.WriteLine("PASS: head loss/restoration and standalone stump render-tree binding.");
    }
}

// Exercise the production node without Unity. The engine base returns the
// configured graphic; the HAR-patched stump base is deliberately unavailable.
namespace Verse
{
    public class Graphic { }
    public class Pawn { public Pawn_HealthTracker health = new Pawn_HealthTracker(); }
    public class Pawn_HealthTracker { public HediffSet hediffSet = new HediffSet(); }
    public class HediffSet { public bool HasHead = true; }
    public class PawnRenderTree { }
    public class PawnRenderNodeProperties { public Graphic graphic; }
    public class PawnRenderNode
    {
        private readonly PawnRenderNodeProperties props;
        public PawnRenderNode(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
        { this.props = props; }
        public virtual Graphic GraphicFor(Pawn pawn) => props.graphic;
    }
}

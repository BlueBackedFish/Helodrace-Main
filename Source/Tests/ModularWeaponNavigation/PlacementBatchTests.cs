using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Helodrace.ModernWar;

internal static class PlacementBatchTests
{
    private static void Check(bool value,string message) { if (!value) throw new Exception(message); }
    internal static void Run()
    {
        var batch=new ModularPlacementBatch();
        string common="<graphicOffset>(0,0,0)</graphicOffset>", rifle="<graphicLayer>1</graphicLayer>";
        batch.ObservePart("Shared",()=>common);batch.ObservePart("Rifle",()=>rifle);
        batch.ObserveAssembly("1","<li><weaponDef>Rifle</weaponDef><railOffset>0.1</railOffset></li>");
        batch.ObservePart("Shared",()=>common);batch.ObservePart("Pistol",()=>"<graphicLayer>2</graphicLayer>");
        batch.ObserveAssembly("2","<li><weaponDef>Pistol</weaponDef><railOffset>-0.2</railOffset></li>");
        Check(batch.WeaponCount==2 && batch.PartCount==3,"Visit multiple guns and deduplicate shared parts");
        common="<graphicOffset>(0.1,0,-0.2)</graphicOffset>";
        Check(batch.RefreshParts() && !batch.RefreshParts(),"Observe shared edits once without resetting their history");
        var export=batch.Capture();var xml=XDocument.Parse(export.Xml);
        Check(xml.Root.Element("parts").Elements().Count()==3,"One batch entry per definition");
        Check(xml.Descendants("graphicOffset").Single().Value=="(0.1,0,-0.2)","Read latest shared values at export, not at first visit");
        Check(xml.Root.Element("assemblies").Elements().Count()==2,"Keep every weapon's layout separately");
        Check(xml.Descendants("railOffset").Select(e=>e.Value).SequenceEqual(new[]{"0.1","-0.2"}),"Never collapse per-instance rail offsets");
        Check(batch.HasUnsavedChanges,"A newly queued batch is unsaved");
        batch.MarkSaved(export);Check(!batch.HasUnsavedChanges,"Successful export acknowledges its exact captured values");
        rifle="<graphicLayer>3</graphicLayer>";Check(batch.HasUnsavedChanges,"Editing after saving dirties the batch");
        var captured=batch.Capture();rifle="<graphicLayer>4</graphicLayer>";batch.MarkSaved(captured);
        Check(batch.HasUnsavedChanges,"Acknowledging an older snapshot cannot hide newer edits");
        Check(!captured.Xml.Contains("<graphicLayer>4</graphicLayer>"),"Captured exports are immutable snapshots");
        batch.MarkSaved(batch.Capture());
        batch.ObserveAssembly("2","<li><weaponDef>Pistol</weaponDef><railOffset>-0.3</railOffset></li>");
        Check(batch.HasUnsavedChanges,"Rail-only editing dirties the batch");
        var separateGame=new ModularPlacementBatch();Check(separateGame.PartCount==0 && separateGame.WeaponCount==0,"New game/session has no stale batch");

        string directory=Path.Combine(Path.GetTempPath(),"Helodrace-placement-tests-"+Guid.NewGuid().ToString("N"));
        var files=new List<string>();
        try
        {
            string first=batch.Save(directory);files.Add(first);
            Check(File.Exists(first) && !batch.HasUnsavedChanges,"Write one complete XML and mark saved only after success");
            XDocument.Load(first);
            string second=batch.Save(directory);files.Add(second);
            Check(first!=second && Directory.GetFiles(directory).Length==2,"Same-tick saves never overwrite previous exports or leave temporary files");
            rifle="<graphicLayer>5</graphicLayer>";
            string blockingFile=Path.Combine(directory,"not-a-directory");File.WriteAllText(blockingFile,"fixture");files.Add(blockingFile);
            bool failed=false;try {batch.Save(blockingFile);} catch(IOException) {failed=true;}
            Check(failed && batch.HasUnsavedChanges && batch.PartCount==3,"I/O failure retains the complete pending batch");
            common="<broken>";failed=false;try {batch.Capture();} catch(System.Xml.XmlException) {failed=true;}
            Check(failed && batch.HasUnsavedChanges,"Malformed XML cannot silently mark edits saved");
        }
        finally
        {
            foreach(string file in files) if(File.Exists(file))File.Delete(file);
            if(Directory.Exists(directory))Directory.Delete(directory); // only our empty, unique test directory
        }
        Console.WriteLine("PASS: cross-weapon placement batching, live shared values, per-instance rails, dirty/save snapshots and failure safety.");
    }
}

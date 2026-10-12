using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerShapeBridge
{
    // Deliberately fixed qualification recipe, not a general transform endpoint.
    public static class TranslationTest
    {
        const double Tolerance = 0.000001;
        public static double[] Bounds(object raw)
        {
            object[] a = Program.ArrayValues(raw);
            if (a.Length != 7 || Program.StrictInt(a[0]) != 1) throw new FormatException("Bounding box failed.");
            double[] b = a.Skip(1).Select(Program.StrictDouble).ToArray();
            if (b[0] > b[1] || b[2] > b[3] || b[4] > b[5]) throw new FormatException("Invalid bounds.");
            return b;
        }
        public static bool Same(double[] a, double[] b)
        {
            return a.Length == b.Length && a.Zip(b, (x,y) => Math.Abs(x-y) <= Tolerance).All(x => x);
        }
        public static void ValidateSandbox(Config c)
        {
            string intended = Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Test_Projects","Test_Block_P2.psmodel"));
            if (!c.translationTestPanel || !Path.GetFullPath(c.expectedProject).Equals(intended,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only the fixed P2 sandbox project is allowed.");
            if (string.IsNullOrWhiteSpace(c.protectedProject) || !File.Exists(c.protectedProject) ||
                Path.GetFullPath(c.protectedProject).Equals(intended,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Protected original is missing or equals sandbox.");
            if (Program.Hash(c.protectedProject) != c.protectedProjectSha256 || Program.Hash(c.expectedProject) != c.testProjectSha256)
                throw new InvalidOperationException("Project checkpoint hashes changed.");
        }
        internal static void Journal(string path, Dictionary<string,object> result, string phase)
        {
            result["phase"] = phase;
            result["updatedUtc"] = DateTime.UtcNow.ToString("o");
            string temp = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(Program.Json.Serialize(result));
            using (var stream = new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) { stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
        }
        public static Dictionary<string,object> Run()
        {
            var r = new Dictionary<string,object> { {"schemaVersion",1},{"recipe","fixed_translate_x5_then_inverse_v1"},
                {"state","rejected_before_mutation"},{"mutationAttempted",false},{"recovery","disk_checkpoint_available; not yet tested by reopening"},
                {"verificationToleranceMm",Tolerance} };
            object app=null, doc=null;
            string journal=null;
            bool attempted=false;
            try
            {
                Config c=Program.ReadConfig(); ValidateSandbox(c);
                string reports=Path.Combine(Program.Root,"reports"); Directory.CreateDirectory(reports);
                string gatePath=Path.Combine(reports,"translation-test.once");
                if(File.Exists(gatePath)) throw new InvalidOperationException("Previous mutation may have occurred; repeat is blocked. Inspect existing journal.");
                var candidates=Process.GetProcessesByName("powershape").Where(p=>p.SessionId==Process.GetCurrentProcess().SessionId).ToArray();
                r["candidateProcessIds"]=candidates.Select(p=>p.Id).ToArray();
                if(candidates.Length==0) throw new InvalidOperationException("PowerShape is absent in this session.");
                app=Marshal.GetActiveObject("PowerShape.Application"); dynamic ps=app;
                int pid=Program.StrictInt(ps.Evaluate("app.pid"));
                r["connectedProcessId"]=pid;
                if(!candidates.Any(p=>p.Id==pid) || Convert.ToBoolean(ps.Busy())) throw new InvalidOperationException("Unexpected or busy instance.");
                string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});
                if(version!=c.expectedVersion) throw new InvalidOperationException("Version changed.");
                string name=Convert.ToString(ps.Evaluate("MODEL.SELECTED"),CultureInfo.InvariantCulture);
                if(string.IsNullOrWhiteSpace(name)||name.Contains("'")||name.Contains("\n")||name.Contains("\r")) throw new InvalidOperationException("Invalid model identity.");
                int modelId=Program.StrictInt(ps.Evaluate("MODEL['"+name+"'].ID"));
                string activePath=Convert.ToString(ps.Evaluate("MODEL[ID "+modelId+"].PATH"),CultureInfo.InvariantCulture);
                r["connectedModelPath"]=activePath;
                if(!Path.GetFullPath(activePath).Equals(Path.GetFullPath(c.expectedProject),StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Open Test_Block_P2.psmodel first. Original model is forbidden.");
                if(Convert.ToString(ps.Evaluate("unit[length].name"),CultureInfo.InvariantCulture)!="mm") throw new InvalidOperationException("Only mm accepted.");
                doc=ps.ActiveDocument; dynamic document=doc;
                if(Program.StrictInt(document.ActiveWorkplane)!=0) throw new InvalidOperationException("Only world workplane accepted.");
                object[] items=Program.ArrayValues(document.GetModelItems());
                if(items.Length!=1) throw new InvalidOperationException("Expected exactly one test solid.");
                int composite=Program.StrictInt(items[0]);
                object types=null,ids=null,names=null,descriptions=null,levels=null;
                document.GetItemData(items,out types,out ids,out names,out descriptions,out levels);
                if(Program.StrictInt(Program.ArrayValues(types)[0])!=40) throw new InvalidOperationException("Expected solid.");
                int nativeId=Program.StrictInt(Program.ArrayValues(ids)[0]);
                string prefix="SOLID[ID "+nativeId+"]";
                double volume=Program.StrictDouble(ps.Evaluate(prefix+".VOLUME"));
                double area=Program.StrictDouble(ps.Evaluate(prefix+".AREA"));
                double[] before=Bounds(document.GetItemBoundingBox(composite,false));
                double[] cog=Program.Vector3(ps.Evaluate(prefix+".COG"));
                if(!Same(before,new[]{-10.0,10.0,-10.0,10.0,0.0,20.0})||!Same(cog,new[]{0.0,0.0,10.0})) throw new InvalidOperationException("Test baseline differs.");
                r["before"]=new {compositeId=composite,nativeId=nativeId,boundsWorldMm=before,cogWorldMm=cog,volumeRaw=volume,areaRaw=area};
                r["checkpointPath"]=c.expectedProject;
                r["protectedProject"]=c.protectedProject;
                // No selection-changing commands; direct COM item addressing.
                int selectionCount=Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"));
                ValidateSandbox(c);
                // Persisted gate begins only after all preflight checks. No automatic replay after this point.
                using(var gate=new FileStream(gatePath,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {byte[] b=Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("o"));gate.Write(b,0,b.Length);gate.Flush(true);}
                journal=Path.Combine(reports,"translation-test-journal.json");
                attempted=true; r["mutationAttempted"]=true; r["state"]="outcome_unknown";
                Journal(journal,r,"before_forward_call");
                int forward=Program.StrictInt(document.TranslateItem(composite,5.0,0.0,0.0));
                r["forwardReturnCode"]=forward;
                if(forward!=1) throw new InvalidOperationException("Forward call failed; automatic inverse suppressed.");
                Journal(journal,r,"forward_returned_success");
                double[] moved=Bounds(document.GetItemBoundingBox(composite,false));
                double[] movedCog=Program.Vector3(ps.Evaluate(prefix+".COG"));
                r["moved"]=new {boundsWorldMm=moved,cogWorldMm=movedCog};
                double[] expected=(double[])before.Clone(); expected[0]+=5; expected[1]+=5;
                // If forward succeeded definitively, perform inverse even if validation failed.
                bool forwardVerified=Same(moved,expected)&&Same(movedCog,new[]{5.0,0.0,10.0});
                r["forwardVerified"]=forwardVerified;
                Journal(journal,r,"before_inverse_call");
                int inverse=Program.StrictInt(document.TranslateItem(composite,-5.0,0.0,0.0));
                r["inverseReturnCode"]=inverse;
                if(inverse!=1) throw new InvalidOperationException("Inverse call failed. Stop and reopen disk checkpoint without saving.");
                double[] restored=Bounds(document.GetItemBoundingBox(composite,false));
                double[] restoredCog=Program.Vector3(ps.Evaluate(prefix+".COG"));
                double afterVolume=Program.StrictDouble(ps.Evaluate(prefix+".VOLUME"));
                double afterArea=Program.StrictDouble(ps.Evaluate(prefix+".AREA"));
                bool geometryVerified=Same(before,restored)&&Same(cog,restoredCog)&&Same(new[]{volume,area},new[]{afterVolume,afterArea});
                r["restored"]=new {boundsWorldMm=restored,cogWorldMm=restoredCog,volumeRaw=afterVolume,areaRaw=afterArea};
                bool contextVerified=Program.StrictInt(document.ActiveWorkplane)==0&&Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))==selectionCount;
                r["contextCheck"]=new {activeWorkplaneAndSelectionCountUnchanged=contextVerified,qualification="selection identity and full UI state not verified"};
                ValidateSandbox(c);
                r["originalAndCheckpointHashesUnchanged"]=true;
                r["state"]=forwardVerified&&geometryVerified&&contextVerified ? "succeeded_restored" : "verification_failed";
                r["recovery"]="inverse checked on bounds, COG, volume and area; full topology equivalence not proven; disk reopen not yet tested";
                Journal(journal,r,"completed");
            }
            catch(Exception ex)
            {
                r["state"]=attempted ? "outcome_unknown_requires_checkpoint_reopen" : "rejected_before_mutation";
                r["error"]=ex.Message;
                if(journal!=null) try {Journal(journal,r,"stopped");} catch { }
            }
            finally
            {
                if(doc!=null&&Marshal.IsComObject(doc)) Marshal.FinalReleaseComObject(doc);
                if(app!=null&&Marshal.IsComObject(app)) Marshal.FinalReleaseComObject(app);
            }
            return r;
        }
    }
}

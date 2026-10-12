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
    public static class CheckpointTest
    {
        public static void ValidatePath(string path)
        {
            if(!Path.IsPathRooted(path)||path.IndexOfAny(new[]{' ','"','\'','\r','\n'})>=0||Path.GetExtension(path)!=".psmodel")
                throw new ArgumentException("Fixed test commands require an absolute .psmodel path without spaces or quotes.");
        }
        internal static string ModelPath(dynamic ps)
        {
            string name=Convert.ToString(ps.Evaluate("MODEL.SELECTED"),CultureInfo.InvariantCulture);
            if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(new[]{'\'','\r','\n'})>=0) throw new InvalidOperationException("Invalid model identity.");
            int id=Program.StrictInt(ps.Evaluate("MODEL['"+name+"'].ID"));
            return Path.GetFullPath(Convert.ToString(ps.Evaluate("MODEL[ID "+id+"].PATH"),CultureInfo.InvariantCulture));
        }
        internal sealed class Measurement
        {
            public int compositeId, nativeId, surfaceCount;
            public double[] boundsWorldMm, cogWorldMm;
            public double volumeRaw, areaRaw;
            public string volumeUnitsRaw;
            public bool closed, connected, watertight, trimmingValid;
        }
        internal static Measurement Measure(dynamic ps,bool allowSingleWorkplane=false,bool allowSingleCompCurve=false,int expectedCurveCount=0)
        {
            object reference=ps.ActiveDocument;
            try
            {
                dynamic doc=reference;
                if(Program.StrictInt(doc.ActiveWorkplane)!=0 || Convert.ToString(ps.Evaluate("unit[length].name"),CultureInfo.InvariantCulture)!="mm")
                    throw new InvalidOperationException("Only world coordinates in mm accepted.");
                object[] items=Program.ArrayValues(doc.GetModelItems());
                int permittedCurves=allowSingleCompCurve?1:expectedCurveCount;
                if(permittedCurves<0||permittedCurves>3||allowSingleWorkplane&&permittedCurves>0)throw new ArgumentException("Invalid permitted companions.");
                if(items.Length!=1+permittedCurves&&!(allowSingleWorkplane&&items.Length==2)) throw new InvalidOperationException("Expected exactly one test solid and the explicitly permitted companions.");
                object types=null,ids=null,names=null,descriptions=null,levels=null;
                doc.GetItemData(items,out types,out ids,out names,out descriptions,out levels);
                var typeList=Program.ArrayValues(types);var idList=Program.ArrayValues(ids);
                if(typeList.Length!=items.Length||idList.Length!=items.Length)throw new InvalidOperationException("Incomplete item data.");
                int solidIndex=-1,workplaneCount=0,curveCount=0;
                for(int index=0;index<items.Length;index++)
                {
                    int type=Program.StrictInt(typeList[index]);
                    if(type==40&&solidIndex<0)solidIndex=index;
                    else if(type==54&&allowSingleWorkplane)workplaneCount++;
                    else if(type==59&&permittedCurves>0)curveCount++;
                    else throw new InvalidOperationException("Unexpected entity inventory.");
                }
                if(solidIndex<0||curveCount!=permittedCurves||(allowSingleWorkplane&&items.Length==2&&workplaneCount!=1))throw new InvalidOperationException("Qualified companion inventory differs.");
                int composite=Program.StrictInt(items[solidIndex]), native=Program.StrictInt(idList[solidIndex]);
                string prefix="SOLID[ID "+native+"]";
                return new Measurement { compositeId=composite,nativeId=native,
                    boundsWorldMm=TranslationTest.Bounds(doc.GetItemBoundingBox(composite,false)),cogWorldMm=Program.Vector3(ps.Evaluate(prefix+".COG")),
                    volumeRaw=Program.StrictDouble(ps.Evaluate(prefix+".VOLUME")),areaRaw=Program.StrictDouble(ps.Evaluate(prefix+".AREA")),
                    volumeUnitsRaw=Convert.ToString(ps.Evaluate("unit[volume].name"),CultureInfo.InvariantCulture),surfaceCount=Program.StrictInt(ps.Evaluate(prefix+".SURFACES")),
                    closed=Program.StrictInt(ps.Evaluate(prefix+".CLOSED"))==1,connected=Program.StrictInt(ps.Evaluate(prefix+".CONNECTED"))==1,
                    watertight=Program.StrictInt(ps.Evaluate(prefix+".WATERTIGHT"))==1,trimmingValid=Program.StrictInt(ps.Evaluate(prefix+".TRIMMING_VALID"))==1 };
            }
            finally {if(Marshal.IsComObject(reference)) Marshal.FinalReleaseComObject(reference);}
        }
        static void Verify(Measurement a, Measurement b, bool moved)
        {
            double[] bounds=(double[])a.boundsWorldMm.Clone(), cog=(double[])a.cogWorldMm.Clone();
            if(moved){bounds[0]+=5;bounds[1]+=5;cog[0]+=5;}
            if(!TranslationTest.Same(bounds,b.boundsWorldMm)||!TranslationTest.Same(cog,b.cogWorldMm)||
                !TranslationTest.Same(new[]{a.volumeRaw,a.areaRaw},new[]{b.volumeRaw,b.areaRaw})||a.volumeUnitsRaw!=b.volumeUnitsRaw||
                a.surfaceCount!=b.surfaceCount||a.closed!=b.closed||a.connected!=b.connected||a.watertight!=b.watertight||a.trimmingValid!=b.trimmingValid)
                throw new InvalidOperationException("Geometric postcondition mismatch.");
        }
        internal static void Open(dynamic ps,string path)
        {
            ValidatePath(path);
            if(!File.Exists(path)) throw new FileNotFoundException("Test project missing.",path);
            int windows=Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"));
            // Exact sequence from Autodesk PSModel constructor, single attempt.
            // No broad CANCEL and no modification of global dialog settings.
            ps.Exec("FILE OPENPATH"); ps.Exec("PATHNAME "+path); ps.Exec("ACCEPT");
            for(int attempt=0;attempt<30;attempt++)
            {
                if(ModelPath(ps).Equals(Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)&&Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"))==windows+1)return;
                System.Threading.Thread.Sleep(100);
            }
            throw new InvalidOperationException("A new disk model was not opened as expected.");
        }
        public static Dictionary<string,object> Run()
        {
            var r=new Dictionary<string,object>{{"schemaVersion",1},{"recipe","fixed_save_reopen_checkpoint_v1"},{"state","rejected_before_mutation"},
                {"mutationAttempted",false},{"verificationScope","bounds, COG, raw volume/area with volume units, surface count, solid validity flags; not full topology or full UI state"}};
            object app=null,doc=null;
            bool attempted=false;
            string journal=null;
            try
            {
                Config c=Program.ReadConfig();TranslationTest.ValidateSandbox(c);
                if(!c.checkpointTestPanel) throw new InvalidOperationException("Checkpoint recipe is disabled.");
                ValidatePath(c.expectedProject);
                string prior=Path.GetFullPath(Path.Combine(Program.Root,"..","P2.1","reports","translation-test-result.json"));
                var receipt=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(prior,Encoding.UTF8));
                if(!Equals(receipt["state"],"succeeded_restored")) throw new InvalidOperationException("Prior translation qualification is missing.");
                string reports=Path.Combine(Program.Root,"reports");Directory.CreateDirectory(reports);
                string gatePath=Path.Combine(reports,"checkpoint-test.once");
                if(File.Exists(gatePath)) throw new InvalidOperationException("Repeat blocked; inspect previous checkpoint journal.");
                var processes=Process.GetProcessesByName("powershape").Where(p=>p.SessionId==Process.GetCurrentProcess().SessionId).ToArray();
                app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;
                int pid=Program.StrictInt(ps.Evaluate("app.pid"));r["connectedProcessId"]=pid;
                if(!processes.Any(p=>p.Id==pid)||Convert.ToBoolean(ps.Busy())) throw new InvalidOperationException("Unexpected or busy instance.");
                string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});
                if(version!=c.expectedVersion) throw new InvalidOperationException("Version mismatch.");
                if(!ModelPath(ps).Equals(Path.GetFullPath(c.expectedProject),StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Activate the baseline Test_Block_P2 model first.");
                Measurement before=Measure(ps);r["before"]=before;
                if(!TranslationTest.Same(before.boundsWorldMm,new[]{-10.0,10.0,-10.0,10.0,0.0,20.0})||!TranslationTest.Same(before.cogWorldMm,new[]{0.0,0.0,10.0}))
                    throw new InvalidOperationException("Baseline differs.");
                string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Saved_Projects"));Directory.CreateDirectory(folder);
                string token=Guid.NewGuid().ToString("N");
                string saved=Path.Combine(folder,"Test_Block_Moved_"+token+".psmodel");
                string reloaded=Path.Combine(folder,"Test_Block_Reload_"+token+".psmodel");
                ValidatePath(saved);ValidatePath(reloaded);
                if(File.Exists(saved)||File.Exists(reloaded)) throw new IOException("Output already exists.");
                r["savedProjectPath"]=saved;r["reloadProjectPath"]=reloaded;r["checkpointPath"]=c.expectedProject;
                TranslationTest.ValidateSandbox(c);
                using(var gate=new FileStream(gatePath,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {byte[] bytes=Encoding.UTF8.GetBytes(token);gate.Write(bytes,0,bytes.Length);gate.Flush(true);}
                journal=Path.Combine(reports,"checkpoint-test-journal.json");
                attempted=true;r["mutationAttempted"]=true;r["state"]="outcome_unknown";
                TranslationTest.Journal(journal,r,"before_translate");
                doc=ps.ActiveDocument;dynamic document=doc;
                int status=Program.StrictInt(document.TranslateItem(before.compositeId,5.0,0.0,0.0));r["translateReturnCode"]=status;
                Marshal.FinalReleaseComObject(doc);doc=null;
                if(status!=1) throw new InvalidOperationException("Translate failed. Stop without saving.");
                Measurement moved=Measure(ps);Verify(before,moved,true);r["moved"]=moved;
                if(!ModelPath(ps).Equals(Path.GetFullPath(c.expectedProject),StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Active project changed.");
                TranslationTest.Journal(journal,r,"before_save_as");
                if(File.Exists(saved)) throw new IOException("Save destination collision.");
                ps.Exec("FILE SAVEASPATH "+saved);
                if(!File.Exists(saved)||new FileInfo(saved).Length==0||!ModelPath(ps).Equals(saved,StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Save did not produce the expected new project.");
                r["savedProjectSha256"]=Program.Hash(saved);
                TranslationTest.ValidateSandbox(c);
                File.Copy(saved,reloaded,false);
                if(Program.Hash(reloaded)!=(string)r["savedProjectSha256"]) throw new IOException("Reload copy hash mismatch.");
                TranslationTest.Journal(journal,r,"before_open_saved_copy");
                Open(ps,reloaded);
                Measurement diskSaved=Measure(ps);Verify(before,diskSaved,true);r["savedReloaded"]=diskSaved;
                r["savedReloadVerified"]=true;
                TranslationTest.Journal(journal,r,"before_open_checkpoint");
                Open(ps,c.expectedProject);
                Measurement restored=Measure(ps);Verify(before,restored,false);r["checkpointReloaded"]=restored;
                TranslationTest.ValidateSandbox(c);
                if(Program.Hash(saved)!=(string)r["savedProjectSha256"]||Program.Hash(reloaded)!=(string)r["savedProjectSha256"])
                    throw new IOException("Saved test output changed unexpectedly.");
                r["originalAndCheckpointHashesUnchanged"]=true;r["checkpointReopenVerified"]=true;
                r["state"]="succeeded_saved_and_reopened";
                r["activeProjectAfter"]=ModelPath(ps);
                r["contextQualification"]="Active checkpoint restored; selection and prior window arrangement not restored. Saved and reloaded test models remain open.";
                TranslationTest.Journal(journal,r,"completed");
            }
            catch(Exception ex)
            {
                r["state"]=attempted?"outcome_unknown_requires_manual_check":"rejected_before_mutation";
                r["error"]=ex.Message;
                r["recoveryInstructions"]="Do not save over the checkpoint or retry. Inspect journal. If necessary reopen only the test checkpoint without saving edited copies.";
                if(journal!=null) try{TranslationTest.Journal(journal,r,"stopped");}catch{}
            }
            finally
            {
                if(doc!=null&&Marshal.IsComObject(doc))Marshal.FinalReleaseComObject(doc);
                if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);
            }
            return r;
        }
    }
}

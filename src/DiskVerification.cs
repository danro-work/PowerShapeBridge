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
    public static class DiskVerification
    {
        internal static void CloseAudit(dynamic ps,string path)
        {
            string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification"))+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(path).StartsWith(folder,StringComparison.OrdinalIgnoreCase)||!CheckpointTest.ModelPath(ps).Equals(path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Audit close target differs.");
            int count=Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"));
            ps.Exec("FILE CLOSE SELECTED YES");
            for(int wait=0;wait<30;wait++)
            {
                if(Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"))==count-1)return;
                System.Threading.Thread.Sleep(100);
            }
            throw new InvalidOperationException("Audit window closure not verified; do not repeat automatically.");
        }
        internal static bool Equivalent(CheckpointTest.Measurement a,CheckpointTest.Measurement b)
        {
            // IDs are deliberately not compared across separate documents.
            return TranslationTest.Same(a.boundsWorldMm,b.boundsWorldMm)&&TranslationTest.Same(a.cogWorldMm,b.cogWorldMm)&&
                TranslationTest.Same(new[]{a.volumeRaw,a.areaRaw},new[]{b.volumeRaw,b.areaRaw})&&a.volumeUnitsRaw==b.volumeUnitsRaw&&
                a.surfaceCount==b.surfaceCount&&a.closed==b.closed&&a.connected==b.connected&&a.watertight==b.watertight&&a.trimmingValid==b.trimmingValid;
        }
        public static Dictionary<string,object> Run()
        {
            var r=new Dictionary<string,object>{{"schemaVersion",1},{"recipe","detached_checkpoint_result_disk_verification_v1"},{"state","rejected_before_open"},
                {"geometryMutationAttempted",false},{"openAttempted",false},{"verificationScope","single solid bounds/COG/raw volume/area/volume units/surface count/validity flags; not complete topology or user context"}};
            object app=null;string journal=null;bool opened=false;
            try
            {
                Config c=Program.ReadConfig();TranslationTest.ValidateSandbox(c);
                if(!c.diskVerificationPanel)throw new InvalidOperationException("Disk verification is disabled.");
                Guid id;if(!Guid.TryParseExact(c.qualificationOperationId,"N",out id))throw new InvalidOperationException("Invalid qualification ID.");
                string receiptRelease=c.qualificationReceiptRelease??"P2.3";
                if(receiptRelease!="P2.3"&&receiptRelease!="P2.5"&&receiptRelease!="P2.6")throw new InvalidOperationException("Unsupported qualification receipt location.");
                string receiptPath=Path.GetFullPath(Path.Combine(Program.Root,"..",receiptRelease,"operations",c.qualificationOperationId+".json"));
                if(Program.Hash(receiptPath)!=c.qualificationReceiptSha256)throw new InvalidOperationException("Qualification receipt changed.");
                var receipt=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(receiptPath,Encoding.UTF8));
                if(!Equals(receipt["state"],"succeeded_saved")||!Equals(receipt["phase"],"completed")||!Equals(receipt["operationId"],c.qualificationOperationId))throw new InvalidOperationException("Operation is not successful.");
                string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Operations"));
                string checkpoint=Path.Combine(folder,"Checkpoint_"+c.qualificationOperationId+".psmodel"),result=Path.Combine(folder,"Result_"+c.qualificationOperationId+".psmodel");
                if(!Equals(receipt["checkpointPath"],checkpoint)||!Equals(receipt["resultPath"],result))throw new InvalidOperationException("Receipt paths do not match fixed operation paths.");
                string checkpointHash=(string)receipt["checkpointSha256"],resultHash=(string)receipt["resultSha256"];
                if(Program.Hash(checkpoint)!=checkpointHash||Program.Hash(result)!=resultHash)throw new IOException("Operation file hash changed; disk verification suppressed.");
                var before=Program.Json.Deserialize<CheckpointTest.Measurement>(Program.Json.Serialize(receipt["before"]));
                bool surfaceResult=receipt.ContainsKey("surfaceResult");
                var after=surfaceResult?null:Program.Json.Deserialize<CheckpointTest.Measurement>(Program.Json.Serialize(receipt["after"]));
                var candidates=Process.GetProcessesByName("powershape").Where(p=>p.SessionId==Process.GetCurrentProcess().SessionId).ToArray();
                app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;
                int pid=Program.StrictInt(ps.Evaluate("app.pid"));r["connectedProcessId"]=pid;
                if(!candidates.Any(p=>p.Id==pid)||Convert.ToBoolean(ps.Busy()))throw new InvalidOperationException("Unexpected or busy instance.");
                string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});
                if(version!=c.expectedVersion)throw new InvalidOperationException("Version mismatch.");
                string initialPath=CheckpointTest.ModelPath(ps);r["activeProjectBefore"]=initialPath;
                if(!initialPath.Equals(result,StringComparison.OrdinalIgnoreCase)&&!initialPath.Equals(Path.GetFullPath(c.expectedProject),StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Activate the successful Result model or baseline Test_Block_P2 first.");
                string auditFolder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification"));Directory.CreateDirectory(auditFolder);
                string token=Guid.NewGuid().ToString("N");
                string checkCopy=Path.Combine(auditFolder,"CheckCheckpoint_"+token+".psmodel"),resultCopy=Path.Combine(auditFolder,"CheckResult_"+token+".psmodel");
                CheckpointTest.ValidatePath(checkCopy);CheckpointTest.ValidatePath(resultCopy);
                string reports=Path.Combine(Program.Root,"reports");Directory.CreateDirectory(reports);
                using(var execution=new FileStream(Path.Combine(reports,"disk-verification.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                {
                    r["operationId"]=c.qualificationOperationId;r["auditId"]=token;r["checkpointSourcePath"]=checkpoint;r["resultSourcePath"]=result;
                    r["checkpointAuditPath"]=checkCopy;r["resultAuditPath"]=resultCopy;
                    journal=Path.Combine(reports,"disk-verification-"+token+".json");TranslationTest.Journal(journal,r,"before_copy");
                    if(PlannedTranslation.CopyFrozen(checkpoint,checkCopy)!=checkpointHash||PlannedTranslation.CopyFrozen(result,resultCopy)!=resultHash)
                        throw new IOException("Audit copy did not match recorded disk bytes.");
                    // First read saved result, then checkpoint, leaving baseline geometry active.
                    opened=true;r["openAttempted"]=true;TranslationTest.Journal(journal,r,"before_open_result_copy");
                    CheckpointTest.Open(ps,resultCopy);CheckpointTest.Measurement saved=null;
                    if(surfaceResult)
                    {
                        bool offset=Equals(receipt["recipe"],"planned_offset_test_top_surface_1mm_v1");
                        if(!offset&&!Equals(receipt["recipe"],"planned_convert_test_solid_to_surfaces_v1"))throw new InvalidOperationException("Unexpected surface receipt recipe.");
                        var surfaces=SurfaceRecipes.Inspect(ps,offset?21:20);
                        int level=Program.StrictInt(receipt["beforeLevelNumber"]);
                        if(((Dictionary<string,object>[])surfaces["faces"]).Any(x=>Program.StrictInt(x["levelNumber"])!=level))throw new InvalidOperationException("Saved surface level differs.");
                        r["resultReloaded"]=surfaces;r["resultSurfaceVerified"]=true;
                        r["surfaceParameters"]=SurfaceRecipes.InspectParameters(ps,surfaces);r["surfaceCentreNormalsVerified"]=true;
                        r["verificationScope"]="Six cube face bounds/raw areas/levels, centres and normals at centres, plus source solid checkpoint measurements; entire surface domain and full topology not certified.";
                    }
                    else{saved=CheckpointTest.Measure(ps,receipt.ContainsKey("workplaneWorldInfo"));r["resultReloaded"]=saved;}
                    if(receipt.ContainsKey("workplaneWorldInfo"))
                    {
                        double[] info=Program.Json.Deserialize<double[]>(Program.Json.Serialize(receipt["workplaneWorldInfo"]));
                        r["workplaneReloaded"]=PlannedTranslation.InspectWorkplane(ps,info);r["resultWorkplaneVerified"]=true;
                    }
                    if(!surfaceResult&&!Equivalent(after,saved))throw new InvalidOperationException("Result geometry did not match operation receipt.");
                    if(receipt.ContainsKey("afterLevelNumber"))
                    {
                        int level=Program.StrictInt(receipt["afterLevelNumber"]);
                        if(PlannedTranslation.SolidLevel(ps,saved.nativeId)!=level||Convert.ToString(ps.Evaluate("LEVEL["+level+"].NAME"))!=(string)receipt["afterLevelName"])throw new InvalidOperationException("Saved result level metadata differs.");
                        r["resultLevelMetadataVerified"]=true;
                    }
                    r["resultDiskVerified"]=true;TranslationTest.Journal(journal,r,"before_close_result_audit");CloseAudit(ps,resultCopy);r["resultAuditClosed"]=true;TranslationTest.Journal(journal,r,"before_open_checkpoint_copy");
                    CheckpointTest.Open(ps,checkCopy);CheckpointTest.Measurement restored=CheckpointTest.Measure(ps);r["checkpointReloaded"]=restored;
                    if(!Equivalent(before,restored))throw new InvalidOperationException("Checkpoint geometry did not match pre-operation receipt.");
                    if(receipt.ContainsKey("beforeLevelNumber"))
                    {
                        if(PlannedTranslation.SolidLevel(ps,restored.nativeId)!=Program.StrictInt(receipt["beforeLevelNumber"]))throw new InvalidOperationException("Checkpoint solid level differs.");
                        r["checkpointLevelVerified"]=true;
                    }
                    r["checkpointDiskVerified"]=true;
                    TranslationTest.ValidateSandbox(c);
                    if(Program.Hash(checkpoint)!=checkpointHash||Program.Hash(result)!=resultHash)throw new IOException("Protected operation sources changed.");
                    r["operationSourceHashesUnchanged"]=true;r["originalAndBaselineHashesUnchanged"]=true;
                    TranslationTest.Journal(journal,r,"before_close_checkpoint_audit");CloseAudit(ps,checkCopy);r["checkpointAuditClosed"]=true;
                    r["activeProjectAfter"]=Convert.ToBoolean(ps.IsDocumentActive())?CheckpointTest.ModelPath(ps):null;r["state"]="succeeded_disk_verified";
                    r["contextQualification"]="Both verified audit windows were closed through the official fixed close command. Other models remain open; prior UI layout not certified.";
                    TranslationTest.Journal(journal,r,"completed");
                }
            }
            catch(Exception ex)
            {
                r["state"]=opened?"disk_verification_failed_after_open":"rejected_before_open";r["error"]=ex.Message;
                if(journal!=null)try{TranslationTest.Journal(journal,r,"stopped");}catch{}
            }
            finally{if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}
            return r;
        }
    }
}

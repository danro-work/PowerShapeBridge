using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
namespace PowerShapeBridge {
 public static class SectionQualification {
 internal static Dictionary<string,object> Inspect(dynamic ps,bool curveOnly=false,double sectionZ=10) {
 object reference=ps.ActiveDocument;
 try {
 dynamic doc=reference;
 if(Program.StrictInt(doc.ActiveWorkplane)!=0||Convert.ToString(ps.Evaluate("unit[length].name"))!="mm")throw new InvalidOperationException("World mm required.");
 var items=Program.ArrayValues((object)doc.GetModelItems());object types=null,ids=null,names=null,descriptions=null,levels=null;
 doc.GetItemData(items,out types,out ids,out names,out descriptions,out levels);var tt=Program.ArrayValues(types);var ii=Program.ArrayValues(ids);var nn=Program.ArrayValues(names);
 int expected=curveOnly?1:2;
 if(items.Length!=expected||tt.Length!=expected||ii.Length!=expected||nn.Length!=expected||tt.Count(x=>Program.StrictInt(x)==40)!=(curveOnly?0:1)||tt.Count(x=>Program.StrictInt(x)==59)!=1)throw new InvalidOperationException("Unexpected section entity inventory.");
 int n=Array.FindIndex(tt,x=>Program.StrictInt(x)==59),native=Program.StrictInt(ii[n]);string prefix="COMPCURVE[ID "+native+"]";
 return InspectCurve(ps,native,Program.StrictInt(items[n]),Convert.ToString(nn[n]),sectionZ);
 }finally{if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
 }
 internal static Dictionary<string,object> InspectCurve(dynamic ps,int native,int composite,string name,double z) {
 string prefix="COMPCURVE[ID "+native+"]";
 double[] min=Program.Vector3(ps.Evaluate(prefix+".MIN_RANGE")),max=Program.Vector3(ps.Evaluate(prefix+".MAX_RANGE"));var bounds=new[]{min[0],max[0],min[1],max[1],min[2],max[2]};
 double length=Program.StrictDouble(ps.Evaluate(prefix+".LENGTH")),area=Program.StrictDouble(ps.Evaluate(prefix+".AREA"));int closed=Program.StrictInt(ps.Evaluate(prefix+".CLOSED")),points=Program.StrictInt(ps.Evaluate(prefix+".POINT.NUMBER"));
 if(!TranslationTest.Same(bounds,new[]{-10.0,10.0,-10.0,10.0,z,z})||Math.Abs(length-80)>1e-6||Math.Abs(Math.Abs(area)-400)>1e-6||closed!=1||points<4||points>16)throw new InvalidOperationException("Fixed square section geometry differs.");
 var keypoints=new List<double[]>();var corners=new HashSet<string>();
 for(int p=1;p<=points;p++){var v=Program.Vector3(ps.Evaluate(prefix+".POINT["+p+"]"));if(Math.Abs(Math.Abs(v[0])-10)>1e-6||Math.Abs(Math.Abs(v[1])-10)>1e-6||Math.Abs(v[2]-z)>1e-6)throw new InvalidOperationException("Section keypoint differs.");corners.Add((v[0]>0?"+":"-")+(v[1]>0?"+":"-"));keypoints.Add(v);}
 if(corners.Count!=4)throw new InvalidOperationException("Not all four corners represented.");
 return new Dictionary<string,object>{{"nativeId",native},{"compositeId",composite},{"name",name},{"zWorldMm",z},{"boundsWorldMm",bounds},{"lengthMm",length},{"areaRaw",area},{"closed",true},{"pointCount",points},{"keypointsWorldMm",keypoints.ToArray()},{"qualification","Fixed planar square: exact bounds, total length, closed flag and four corner keypoints; arbitrary spline deviation and slicing topology not certified."}};
 }
 public static Dictionary<string,object> Run(string id,double sectionZ=10) {
 var r=new Dictionary<string,object>{{"schemaVersion",1},{"recipe",sectionZ==0?"fixed_cube_section_z0_v1":"fixed_cube_section_z10_v1"},{"sectionZMm",sectionZ},{"state","rejected_before_mutation"},{"geometryMutationAttempted",false}};object app=null;bool opened=false;string journal=null;
 try {
 Guid g;if(!Guid.TryParseExact(id,"N",out g)||sectionZ!=0&&sectionZ!=10)throw new ArgumentException("Invalid identity or fixed section height.");Config c=Program.ReadConfig();TranslationTest.ValidateSandbox(c);
 using(var lane=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
 app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;int pid=Program.StrictInt(ps.Evaluate("app.pid"));
 if(!System.Diagnostics.Process.GetProcessesByName("powershape").Any(x=>x.Id==pid&&x.SessionId==System.Diagnostics.Process.GetCurrentProcess().SessionId)||Convert.ToBoolean(ps.Busy()))throw new InvalidOperationException("Unexpected/busy instance.");
 string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});if(version!=c.expectedVersion)throw new InvalidOperationException("Version differs.");
 if(CheckpointTest.ModelPath(ps)!=Path.GetFullPath(c.expectedProject)||Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Baseline with empty selection required.");
 var before=CheckpointTest.Measure(ps);if(!TranslationTest.Same(before.boundsWorldMm,new[]{-10.0,10.0,-10.0,10.0,0.0,20.0}))throw new InvalidOperationException("Not the qualified block.");
 string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Disk_Verification"));
 string working=Path.Combine(folder,"SectionWorking_"+id+".psmodel"),checkpoint=Path.Combine(folder,"SectionCheckpoint_"+id+".psmodel"),saved=Path.Combine(folder,"SectionResult_"+id+".psmodel"),audit=Path.Combine(folder,"SectionReopen_"+id+".psmodel");
 string dgk=Path.Combine(folder,"Section_"+id+".dgk"),imported=Path.Combine(folder,"SectionImported_"+id+".psmodel");
 foreach(var p in new[]{working,checkpoint,saved,audit,dgk,imported})if(File.Exists(p))throw new IOException("Section output collision.");
 PlannedTranslation.CopyFrozen(c.expectedProject,checkpoint);PlannedTranslation.CopyFrozen(checkpoint,working);
 r["qualificationId"]=id;r["processId"]=pid;r["checkpointPath"]=checkpoint;r["checkpointSha256"]=Program.Hash(checkpoint);r["workingPath"]=working;r["resultPath"]=saved;r["auditPath"]=audit;r["before"]=before;
 journal=Path.Combine(Program.Root,"reports","section-"+id+".json");opened=true;r["state"]="qualification_in_progress";TranslationTest.Journal(journal,r,"before_open_working");CheckpointTest.Open(ps,working);
 if(!DiskVerification.Equivalent(before,CheckpointTest.Measure(ps)))throw new InvalidOperationException("Working copy differs.");
 string name=Convert.ToString(ps.Evaluate("SOLID[ID "+before.nativeId+"].NAME"));if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(new[]{'\'','\r','\n'})>=0)throw new InvalidOperationException("Unsafe solid name.");
 ps.Exec("PRINCIPALPLANE XY");ps.Exec("SELECT CLEARLIST ADD SOLID '"+name+"'");
 if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=1||CheckpointTest.ModelPath(ps)!=working)throw new InvalidOperationException("Section target differs.");
 r["geometryMutationAttempted"]=true;TranslationTest.Journal(journal,r,"before_create_section");
 ps.Exec("CREATE CURVE OBLIQUE");ps.Exec("DISTANCE "+sectionZ.ToString("0.######",System.Globalization.CultureInfo.InvariantCulture));ps.Exec("ACCEPT");ps.Exec("SELECT CLEARLIST");
 Dictionary<string,object> curve=Inspect(ps,false,sectionZ);r["curve"]=curve;if(!DiskVerification.Equivalent(before,CheckpointTest.Measure(ps,false,true)))throw new InvalidOperationException("Solid changed during section creation.");
 string curveName=(string)curve["name"];if(string.IsNullOrWhiteSpace(curveName)||curveName.IndexOfAny(new[]{'\'','\r','\n'})>=0)throw new InvalidOperationException("Unsafe curve name.");
 r["dgkPath"]=dgk;r["importedProjectPath"]=imported;TranslationTest.Journal(journal,r,"before_export_curve");
 ps.Exec("SELECT CLEARLIST ADD COMPCURVE '"+curveName+"'");if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=1||CheckpointTest.ModelPath(ps)!=working)throw new InvalidOperationException("Curve export selection differs.");
 ps.Exec("FILE EXPORT '"+dgk+"'");ps.Exec("SELECT CLEARLIST");if(!File.Exists(dgk)||new FileInfo(dgk).Length==0)throw new IOException("Curve DGK absent.");r["dgkSha256"]=Program.Hash(dgk);r["dgkBytes"]=new FileInfo(dgk).Length;
 TranslationTest.Journal(journal,r,"before_save_result");ps.Exec("FILE SAVEASPATH "+saved);
 if(!File.Exists(saved)||CheckpointTest.ModelPath(ps)!=saved)throw new InvalidOperationException("Section SaveAs differs.");
 Inspect(ps,false,sectionZ);TranslationTest.Journal(journal,r,"before_close_result");DiskVerification.CloseAudit(ps,saved);r["resultWindowClosed"]=true;
 string digest=Program.Hash(saved);for(int n=0;n<4;n++){System.Threading.Thread.Sleep(250);if(Program.Hash(saved)!=digest)throw new IOException("Section result still changing after closure.");}
 r["resultSha256"]=digest;PlannedTranslation.CopyFrozen(saved,audit);TranslationTest.Journal(journal,r,"before_reopen_result");CheckpointTest.Open(ps,audit);
 r["reopenedCurve"]=Inspect(ps,false,sectionZ);if(!DiskVerification.Equivalent(before,CheckpointTest.Measure(ps,false,true)))throw new InvalidOperationException("Saved solid differs.");
 DiskVerification.CloseAudit(ps,audit);r["auditWindowClosed"]=true;
 TranslationTest.Journal(journal,r,"before_import_curve");int windows=Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"));ps.Exec("FILE NEW");
 if(Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"))!=windows+1)throw new InvalidOperationException("Curve import window differs.");
 object reference=ps.ActiveDocument;try{dynamic doc=reference;if(Program.ArrayValues((object)doc.GetModelItems()).Length!=0)throw new InvalidOperationException("Curve import document is not empty.");}finally{if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
 ps.Exec("FILE IMPORT \""+dgk+"\"");ps.Exec("DISMISS");r["importedCurve"]=Inspect(ps,true,sectionZ);
 ps.Exec("FILE SAVEASPATH "+imported);if(!File.Exists(imported)||CheckpointTest.ModelPath(ps)!=imported)throw new InvalidOperationException("Imported curve save differs.");
 DiskVerification.CloseAudit(ps,imported);r["importedWindowClosed"]=true;System.Threading.Thread.Sleep(1000);r["importedProjectSha256"]=Program.Hash(imported);
 TranslationTest.ValidateSandbox(c);if(Program.Hash(saved)!=digest||Program.Hash(checkpoint)!=(string)r["checkpointSha256"]||CheckpointTest.ModelPath(ps)!=Path.GetFullPath(c.expectedProject))throw new InvalidOperationException("Section sources or active baseline changed.");
 if(Program.Hash(dgk)!=(string)r["dgkSha256"])throw new IOException("Curve DGK changed.");
 r["originalAndBaselineHashesUnchanged"]=true;r["state"]="succeeded_section_saved_reopened_dgk_verified";r["contextQualification"]="Baseline active, selection empty; principal plane XY. Previous principal plane/window arrangement not certified. Imported curve psmodel disk reopen not performed.";TranslationTest.Journal(journal,r,"completed");
 }
 }catch(Exception e){r["state"]=opened?"section_stopped_requires_inspection":"rejected_before_mutation";r["error"]=e.Message;if(journal!=null)try{TranslationTest.Journal(journal,r,"stopped");}catch{}}
 finally{if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}
 return r;
 }
 }
}

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
    public static class SurfaceRecipes
    {
        internal static Dictionary<string,object> Inspect(dynamic ps,double topZ=20)
        {
            object reference=ps.ActiveDocument;
            try
            {
                dynamic document=reference;
                if(Program.StrictInt(document.ActiveWorkplane)!=0||Convert.ToString(ps.Evaluate("unit[length].name"))!="mm")throw new InvalidOperationException("Surface inspection requires world mm.");
                object[] items=Program.ArrayValues((object)document.GetModelItems());
                if(items.Length!=6)throw new InvalidOperationException("Expected exactly six surface entities.");
                object types=null,ids=null,names=null,descriptions=null,levels=null;
                document.GetItemData(items,out types,out ids,out names,out descriptions,out levels);
                object[][] data=new[]{types,ids,names,levels}.Select(Program.ArrayValues).ToArray();
                if(data.Any(x=>x.Length!=6))throw new InvalidOperationException("Incomplete surface data.");
                var faces=new List<Dictionary<string,object>>();var sides=new HashSet<int>();
                for(int index=0;index<6;index++)
                {
                    if(Program.StrictInt(data[0][index])!=21)throw new InvalidOperationException("Unexpected entity type after conversion.");
                    int composite=Program.StrictInt(items[index]),native=Program.StrictInt(data[1][index]);
                    string faceName=Convert.ToString(data[2][index]);
                    if(string.IsNullOrWhiteSpace(faceName)||faceName.IndexOfAny(new[]{'\'','\r','\n'})>=0)throw new InvalidOperationException("Invalid surface name.");
                    double[] bounds;
                    ps.Exec("SELECT CLEARLIST ADD SURFACE '"+faceName+"'");
                    try
                    {
                        if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=1||Convert.ToString(ps.Evaluate("SELECTION.NAME[0]"))!=faceName)throw new InvalidOperationException("Surface bounds selection differs.");
                        double[] min=Program.ArrayValues((object)ps.Evaluate("SELECTION.MIN_RANGE_EXACT")).Select(Program.StrictDouble).ToArray();
                        double[] max=Program.ArrayValues((object)ps.Evaluate("SELECTION.MAX_RANGE_EXACT")).Select(Program.StrictDouble).ToArray();
                        if(min.Length!=3||max.Length!=3)throw new InvalidOperationException("Invalid exact surface ranges.");
                        bounds=new[]{min[0],max[0],min[1],max[1],min[2],max[2]};
                    }
                    finally{ps.Exec("SELECT CLEARLIST");}
                    double area=Program.StrictDouble(ps.Evaluate("SURFACE[ID "+native+"].AREA"));
                    int side=-1;
                    for(int axis=0;axis<3;axis++)
                    {
                        if(Math.Abs(bounds[2*axis+1]-bounds[2*axis])>1e-6)continue;
                        double lower=axis==2?0:-10,upper=axis==2?topZ:10;
                        if(Math.Abs(bounds[2*axis]-lower)<1e-6)side=2*axis;
                        else if(Math.Abs(bounds[2*axis]-upper)<1e-6)side=2*axis+1;
                        if(side<0)throw new InvalidOperationException("Face plane does not match qualified cube.");
                    }
                    if(side<0||!sides.Add(side)||Math.Abs(area-400)>1e-6)throw new InvalidOperationException("Missing/duplicate cube face or unexpected area.");
                    for(int axis=0;axis<3;axis++)if(axis!=side/2)
                    {
                        double lower=axis==2?0:-10,upper=axis==2?20:10;
                        if(Math.Abs(bounds[2*axis]-lower)>1e-6||Math.Abs(bounds[2*axis+1]-upper)>1e-6)throw new InvalidOperationException("Face bounds do not cover the qualified side.");
                    }
                    faces.Add(new Dictionary<string,object>{{"side",side},{"compositeId",composite},{"nativeId",native},{"name",Convert.ToString(data[2][index])},{"levelNumber",Program.StrictInt(data[3][index])},{"boundsWorldMm",bounds},{"areaRaw",area}});
                }
                return new Dictionary<string,object>{{"surfaceCount",6},{"faces",faces.OrderBy(x=>(int)x["side"]).ToArray()},{"areaRawSum",faces.Sum(x=>(double)x["areaRaw"])},{"unionBoundsWorldMm",new[]{-10.0,10.0,-10.0,10.0,0.0,topZ}},{"qualification","Six axis-aligned face bounds and raw areas of qualified cube; normals, trimming/topology and closed-shell equivalence not certified."}};
            }
            finally{if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
        }
        internal static Dictionary<string,object> InspectParameters(dynamic ps,Dictionary<string,object> summary)
        {
            if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Parameter inspection requires empty selection.");
            var readings=new List<Dictionary<string,object>>();
            foreach(var face in (Dictionary<string,object>[])summary["faces"])
            {
                int side=Program.StrictInt(face["side"]),native=Program.StrictInt(face["nativeId"]);
                double[] bounds=(double[])face["boundsWorldMm"];
                double[] centre=new[]{(bounds[0]+bounds[1])/2,(bounds[2]+bounds[3])/2,(bounds[4]+bounds[5])/2};
                string entity="SURFACE[ID "+native+"]";
                string xyz=string.Join(";",centre.Select(x=>x.ToString("R",CultureInfo.InvariantCulture)));
                double[] near=Program.ArrayValues((object)ps.Evaluate(entity+".NEAR("+xyz+")")).Select(Program.StrictDouble).ToArray();
                if(near.Length!=3||near.Any(x=>double.IsNaN(x)||double.IsInfinity(x)))throw new InvalidOperationException("Unexpected NEAR parameter result.");
                string tu=near[0].ToString("R",CultureInfo.InvariantCulture)+";"+near[1].ToString("R",CultureInfo.InvariantCulture);
                string evaluate=entity+".EVALUATE("+tu+")";
                double[] point=Program.ArrayValues((object)ps.Evaluate(evaluate+".POSITION")).Select(Program.StrictDouble).ToArray();
                double[] normal=Program.ArrayValues((object)ps.Evaluate(evaluate+".NORMAL")).Select(Program.StrictDouble).ToArray();
                if(point.Length!=3||normal.Length!=3||!TranslationTest.Same(point,centre)||normal.Any(x=>double.IsNaN(x)||double.IsInfinity(x)))throw new InvalidOperationException("Surface centre evaluation differs.");
                double norm=Math.Sqrt(normal.Sum(x=>x*x));
                if(Math.Abs(norm-1)>1e-6||Math.Abs(Math.Abs(normal[side/2])-1)>1e-6||normal.Where((x,index)=>index!=side/2).Any(x=>Math.Abs(x)>1e-6))throw new InvalidOperationException("Unexpected face centre normal.");
                double outward=normal[side/2]*(side%2==0?-1:1);
                var properties=new Dictionary<string,object>();
                foreach(string property in new[]{"NLATS","NLONS","BOUNDARIES","PCURVES","TRIMMED","TRIMMING_VALID"})
                {
                    try
                    {
                        int value=Program.StrictInt(ps.Evaluate(entity+"."+property));
                        if(value<0||((property=="TRIMMED"||property=="TRIMMING_VALID")&&value>1))throw new InvalidOperationException("Unexpected property domain.");
                        properties[property]=new Dictionary<string,object>{{"status","read_verified_type"},{"value",value}};
                    }
                    catch(Exception error){properties[property]=new Dictionary<string,object>{{"status","requires_prototype_verification"},{"error",error.Message}};}
                }

                readings.Add(new Dictionary<string,object>{{"side",side},{"nativeId",native},{"properties",properties},{"centreWorldMm",centre},{"nearestParametersRaw",near},{"evaluatedPositionWorldMm",point},{"normalWorld",normal},{"normalLength",norm},{"outwardDot",outward},{"orientationAtCentre",outward>0?"outward":"inward"}});
            }
            return new Dictionary<string,object>{{"state","succeeded_centre_parameters_verified"},{"surfaceCount",readings.Count},{"readings",readings},{"selectionCountAfter",Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))},{"qualification","NEAR parameters, evaluated centre and unit normal at one point per qualified planar cube face, with world workplane active. Entire parametric domain, trimming and global orientation not certified."}};
        }
        public static Dictionary<string,object> InspectFailed(string id)
        {
            Guid guid;if(!Guid.TryParseExact(id,"N",out guid))throw new ArgumentException("Invalid ID.");
            Config c=Program.ReadConfig();TranslationTest.ValidateSandbox(c);
            var receipt=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Program.Root,"operations",id+".json"),Encoding.UTF8));
            if(!Equals(receipt["recipe"],"planned_convert_test_solid_to_surfaces_v1")||!Equals(receipt["state"],"outcome_unknown_requires_manual_check"))throw new InvalidOperationException("Unexpected receipt.");
            object app=null,reference=null;
            try{
                app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;
                if(Convert.ToBoolean(ps.Busy())||Program.StrictInt(ps.Evaluate("app.pid"))!=Program.StrictInt(receipt["connectedProcessId"])||CheckpointTest.ModelPath(ps)!=(string)receipt["workingProjectPath"])throw new InvalidOperationException("Failed working copy is not active.");
                if(Program.Hash((string)receipt["checkpointPath"])!=(string)receipt["checkpointSha256"])throw new InvalidOperationException("Checkpoint differs.");
                reference=ps.ActiveDocument;dynamic doc=reference;object[] items=Program.ArrayValues((object)doc.GetModelItems());
                object types=null,ids=null,names=null,descriptions=null,levels=null;doc.GetItemData(items,out types,out ids,out names,out descriptions,out levels);
                var r=new Dictionary<string,object>{{"state","inspected_failed_conversion"},{"geometryMutationAttempted",false},{"items",items},{"types",Program.ArrayValues(types)},{"ids",Program.ArrayValues(ids)},{"names",Program.ArrayValues(names)},{"selectionCount",Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))}};
                r["rawBounds"]=items.Select(x=>Program.ArrayValues((object)doc.GetItemBoundingBox(Program.StrictInt(x),false))).ToArray();
                r["rawAreas"]=Program.ArrayValues(ids).Select(x=>Convert.ToString(ps.Evaluate("SURFACE[ID "+Program.StrictInt(x)+"].AREA"))).ToArray();
                PlannedTranslation.SaveJson(Path.Combine(Program.Root,"operations",id+".inspection.json"),r);return r;
            }finally{if(reference!=null&&Marshal.IsComObject(reference))Marshal.FinalReleaseComObject(reference);if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}
        }
        public static Dictionary<string,object> Execute(string id,string digest)
        {
            var result=new Dictionary<string,object>{{"schemaVersion",1},{"recipe","planned_convert_test_solid_to_surfaces_v1"},{"state","rejected_before_mutation"},{"mutationAttempted",false}};
            object app=null,reference=null;string journal=null;bool attempted=false;
            try
            {
                Guid guid;if(!Guid.TryParseExact(id,"N",out guid))throw new ArgumentException("Invalid operation ID.");
                Config config=Program.ReadConfig();TranslationTest.ValidateSandbox(config);
                if(!config.recipeOperationsPanel)throw new InvalidOperationException("Recipes disabled.");
                string operations=Path.Combine(Program.Root,"operations");Directory.CreateDirectory(operations);
                using(var execution=new FileStream(Path.Combine(operations,"executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                {
                    string text=File.ReadAllText(Path.Combine(Program.Root,"plans",id+".json"),Encoding.UTF8);
                    if(text.Length>65536||PlannedTranslation.Digest(text)!=digest)throw new InvalidOperationException("Plan digest differs.");
                    var plan=Program.Json.Deserialize<PlannedTranslation.Plan>(text);
                    if(plan.operationId!=id||(plan.recipeId!="convert_test_solid_to_surfaces"&&plan.recipeId!="offset_test_top_surface_1mm"))throw new InvalidOperationException("Conversion plan binding differs.");
                    bool offset=plan.recipeId=="offset_test_top_surface_1mm";if(offset)result["recipe"]="planned_offset_test_top_surface_1mm_v1";
                    if(File.Exists(Path.Combine(operations,id+".once")))throw new InvalidOperationException("Operation ID already claimed; replay forbidden.");
                    app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;
                    int pid=Program.StrictInt(ps.Evaluate("app.pid"));
                    if(!Process.GetProcessesByName("powershape").Any(p=>p.Id==pid&&p.SessionId==Process.GetCurrentProcess().SessionId)||Convert.ToBoolean(ps.Busy()))throw new InvalidOperationException("Unexpected/busy instance.");
                    string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});
                    if(version!=config.expectedVersion)throw new InvalidOperationException("Version mismatch.");
                    string path=CheckpointTest.ModelPath(ps);
                    if(!path.Equals(Path.GetFullPath(config.expectedProject),StringComparison.OrdinalIgnoreCase)||Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Conversion requires active baseline and empty selection.");
                    var before=CheckpointTest.Measure(ps);
                    PlannedTranslation.ValidatePreparedPlan(plan,pid,path,PlannedTranslation.Fingerprint(pid,path,before),PlannedTranslation.Geometry(before),DateTime.UtcNow);
                    if(plan.beforeLevelNumber!=PlannedTranslation.SolidLevel(ps,before.nativeId)||!TranslationTest.Same(before.boundsWorldMm,new[]{-10.0,10.0,-10.0,10.0,0.0,20.0})||!TranslationTest.Same(plan.expectedBoundsWorldMm,before.boundsWorldMm))throw new InvalidOperationException("Cube or level differs from plan.");
                    string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Operations"));Directory.CreateDirectory(folder);
                    string working=Path.Combine(folder,"Working_"+id+".psmodel"),checkpoint=Path.Combine(folder,"Checkpoint_"+id+".psmodel"),saved=Path.Combine(folder,"Result_"+id+".psmodel");
                    foreach(string file in new[]{working,checkpoint,saved}){CheckpointTest.ValidatePath(file);if(File.Exists(file))throw new IOException("Output collision; overwrite forbidden.");}
                    using(var gate=new FileStream(Path.Combine(operations,id+".once"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] bytes=Encoding.UTF8.GetBytes(digest);gate.Write(bytes,0,bytes.Length);gate.Flush(true);}
                    result["operationId"]=id;result["plan"]=plan;result["before"]=before;result["beforeLevelNumber"]=plan.beforeLevelNumber;
                    result["connectedProcessId"]=pid;result["connectedModelPath"]=path;result["workingProjectPath"]=working;result["checkpointPath"]=checkpoint;result["resultPath"]=saved;
                    journal=Path.Combine(operations,id+".json");attempted=true;result["mutationAttempted"]=true;result["state"]="outcome_unknown";
                    TranslationTest.Journal(journal,result,"before_working_save_as");ps.Exec("FILE SAVEASPATH "+working);
                    if(!File.Exists(working)||CheckpointTest.ModelPath(ps)!=working||PlannedTranslation.Geometry(CheckpointTest.Measure(ps))!=plan.geometryFingerprint)throw new InvalidOperationException("Working save not verified.");
                    result["checkpointSha256"]=PlannedTranslation.CopyFrozen(working,checkpoint);TranslationTest.ValidateSandbox(config);
                    reference=ps.ActiveDocument;dynamic document=reference;
                    if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Selection changed before conversion.");
                    string name=Convert.ToString(document.GetItemName(before.compositeId));
                    if(string.IsNullOrWhiteSpace(name)||name.Length>128||name.IndexOfAny(new[]{'\'','\r','\n'})>=0)throw new InvalidOperationException("Invalid solid name.");
                    TranslationTest.Journal(journal,result,"before_convert_solid");
                    ps.Exec("SELECT CLEARLIST ADD SOLID '"+name+"'");
                    if(CheckpointTest.ModelPath(ps)!=working||Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=1||Convert.ToString(ps.Evaluate("SELECTION.NAME[0]"))!=name)throw new InvalidOperationException("Conversion selection differs.");
                    ps.Exec("CONVERT SOLID");
                    if(CheckpointTest.ModelPath(ps)!=working)throw new InvalidOperationException("Active model changed during conversion.");
                    // Exec returns before the converted inventory is necessarily visible.
                    // Poll reads only; never repeat CONVERT or delete an unexpected object.
                    bool inventoryReady=false;
                    for(int attempt=0;attempt<30;attempt++)
                    {
                        object[] items=Program.ArrayValues((object)document.GetModelItems());
                        if(items.Length==6)
                        {
                            object types=null,ids=null,names=null,descriptions=null,levels=null;
                            document.GetItemData(items,out types,out ids,out names,out descriptions,out levels);
                            object[] codes=Program.ArrayValues(types);
                            if(codes.Length==6&&codes.All(x=>Program.StrictInt(x)==21)){inventoryReady=true;break;}
                        }
                        System.Threading.Thread.Sleep(100);
                    }
                    if(!inventoryReady)throw new InvalidOperationException("Converted surface inventory not verified; no extra deletion performed.");
                    ps.Exec("SELECT CLEARLIST");
                    var surfaces=Inspect(ps);
                    if(offset)
                    {
                        var parameters=InspectParameters(ps,surfaces);result["surfaceParametersBeforeOffset"]=parameters;
                        var top=((Dictionary<string,object>[])surfaces["faces"]).Single(x=>Program.StrictInt(x["side"])==5);
                        var normal=((List<Dictionary<string,object>>)parameters["readings"]).Single(x=>Program.StrictInt(x["side"])==5);
                        if(!TranslationTest.Same((double[])normal["normalWorld"],new[]{0.0,0.0,1.0}))throw new InvalidOperationException("Top normal is not qualified for positive offset.");
                        if(CheckpointTest.ModelPath(ps)!=working||Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Offset context differs.");
                        string topName=(string)top["name"];
                        TranslationTest.Journal(journal,result,"before_top_surface_offset");
                        ps.Exec("SELECT CLEARLIST ADD SURFACE '"+topName+"'");
                        if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=1||Convert.ToString(ps.Evaluate("SELECTION.NAME[0]"))!=topName)throw new InvalidOperationException("Offset selection differs.");
                        ps.Exec("EDIT OFFSET");ps.Exec("NOKEEP");ps.Exec("SHARP");ps.Exec("SPLITOFF");ps.Exec("DISTANCE 1");ps.Exec("EDIT OFFSET OFF");
                        ps.Exec("SELECT CLEARLIST");
                        if(CheckpointTest.ModelPath(ps)!=working)throw new InvalidOperationException("Offset active path changed.");
                        surfaces=Inspect(ps,21);result["offsetDistanceWorldMm"]=1;result["offsetTargetSide"]=5;result["expectedTopPlaneWorldZMm"]=21;
                        result["offsetCommandSequenceCompleted"]=true;result["offsetQualification"]="One top plane offset outward by 1 mm; five other faces unchanged. This creates an open surface set and does not establish a watertight enlarged cube.";
                    }
                    if(((Dictionary<string,object>[])surfaces["faces"]).Any(x=>Program.StrictInt(x["levelNumber"])!=plan.beforeLevelNumber.Value))throw new InvalidOperationException("Surface levels differ from source.");result["after"]=surfaces;result["surfaceResult"]=surfaces;
                    if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Selection was not cleared.");
                    TranslationTest.Journal(journal,result,"before_result_save_as");ps.Exec("FILE SAVEASPATH "+saved);
                    if(!File.Exists(saved)||CheckpointTest.ModelPath(ps)!=saved)throw new InvalidOperationException("Result save failed.");
                    Inspect(ps,offset?21:20);
                    if(Program.Hash(checkpoint)!=(string)result["checkpointSha256"])throw new IOException("Checkpoint changed.");
                    TranslationTest.ValidateSandbox(config);result["resultSha256"]=Program.Hash(saved);result["originalAndBaselineHashesUnchanged"]=true;
                    result["state"]="succeeded_saved";result["verificationScope"]="Six qualified cube faces, world bounds and raw areas, inventory, empty selection and protected hashes; full topology and normals not certified.";
                    TranslationTest.Journal(journal,result,"completed");
                }
            }
            catch(Exception ex){result["state"]=attempted?"outcome_unknown_requires_manual_check":"rejected_before_mutation";result["error"]=ex.Message;if(journal!=null)try{TranslationTest.Journal(journal,result,"stopped");}catch{};}
            finally{if(reference!=null&&Marshal.IsComObject(reference))Marshal.FinalReleaseComObject(reference);if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PowerShapeBridge
{
    public static class PlannedTranslation
    {
        public sealed class Plan
        {
            public string operationId {get;set;}
            public string createdUtc {get;set;}
            public int processId {get;set;}
            public string modelPath {get;set;}
            public string fingerprint {get;set;}
            public string geometryFingerprint {get;set;}
            public double[] deltaWorldMm {get;set;}
            public double[] expectedBoundsWorldMm {get;set;}
            public double[] expectedCogWorldMm {get;set;}
            public string qualification {get;set;}
            public string recipeId {get;set;}
            public int? angleDegrees {get;set;}
            public int? targetLevelNumber {get;set;}
            public int? beforeLevelNumber {get;set;}
            public double[] workplaneWorldInfo {get;set;}
            public double? scaleFactor {get;set;}
            public double? offsetDistanceMm {get;set;}
            public int? targetSide {get;set;}
            public double? expectedTopZMm {get;set;}
            public double[] expectedSurfaceUnionBoundsWorldMm {get;set;}
            public double[] sliceParametersMm {get;set;}
            public double[] sliceHeightsWorldMm {get;set;}
        }
        public static void ValidateVector(double[] delta)
        {
            if(delta==null||delta.Length!=3||delta.Any(x=>double.IsNaN(x)||double.IsInfinity(x)||Math.Abs(x)>100)||delta.All(x=>x==0))
                throw new ArgumentException("Specify a finite nonzero world vector; each component must be within +/-100 mm.");
        }
        public static double[] ShiftBounds(double[] b,double[] d)
        { return new[]{b[0]+d[0],b[1]+d[0],b[2]+d[1],b[3]+d[1],b[4]+d[2],b[5]+d[2]}; }
        public static void ValidateAngle(int angle){if(angle==0||Math.Abs((long)angle)>180)throw new ArgumentException("Nonzero integer angle within +/-180 degrees required.");}
        public static void ValidateScale(double factor){if(double.IsNaN(factor)||double.IsInfinity(factor)||factor<0.5||factor>2||factor==1)throw new ArgumentException("Uniform scale must be within 0.5..2 and differ from 1; origin is world zero.");}
        public static void ValidateTestLevel(int number){if(number<900||number>909)throw new ArgumentException("Only test levels 900..909 are qualified.");}
        internal static int SolidLevel(dynamic ps,int nativeId){return Program.StrictInt(ps.Evaluate("SOLID[ID "+nativeId+"].LEVEL"));}
        public static void ValidateWorkplane(double[] info)
        {
            if(info==null||info.Length!=9||info.Any(x=>double.IsNaN(x)||double.IsInfinity(x))||info.Take(3).Any(x=>Math.Abs(x)>100))throw new ArgumentException("Workplane requires nine finite world values; origin within +/-100 mm.");
            double zz=0,yy=0,yz=0;
            for(int axis=0;axis<3;axis++){zz+=info[3+axis]*info[3+axis];yy+=info[6+axis]*info[6+axis];yz+=info[3+axis]*info[6+axis];}
            if(Math.Abs(zz-1)>1e-9||Math.Abs(yy-1)>1e-9||Math.Abs(yz)>1e-9)throw new ArgumentException("Z and Y must be orthogonal unit vectors; normalization is not implicit.");
        }
        public static double[] ParseWorkplane(Dictionary<string,object> parameters)
        {
            if(parameters.Count!=3||!parameters.ContainsKey("originWorldMm")||!parameters.ContainsKey("zAxis")||!parameters.ContainsKey("yAxis"))throw new ArgumentException("Only originWorldMm, zAxis and yAxis accepted.");
            var values=new List<double>();
            foreach(string key in new[]{"originWorldMm","zAxis","yAxis"})
            {
                var array=parameters[key] as System.Collections.IList;
                if(array==null||array.Count!=3)throw new ArgumentException("Each workplane vector needs three numbers.");
                values.AddRange(array.Cast<object>().Select(Program.StrictDouble));
            }
            double[] info=values.ToArray();ValidateWorkplane(info);return info;
        }
        internal static Dictionary<string,object> InspectWorkplane(dynamic ps,double[] expected)
        {
            object reference=ps.ActiveDocument;
            try
            {
                dynamic document=reference;var items=Program.ArrayValues(document.GetModelItems());
                if(items.Length!=2)throw new InvalidOperationException("Workplane result must contain one solid and one workplane.");
                object types=null,ids=null,names=null,descriptions=null,levels=null;document.GetItemData(items,out types,out ids,out names,out descriptions,out levels);
                var typeList=Program.ArrayValues(types);var idList=Program.ArrayValues(ids);int found=-1;
                if(typeList.Length!=2||idList.Length!=2)throw new InvalidOperationException("Incomplete workplane inventory.");
                for(int i=0;i<2;i++)if(Program.StrictInt(typeList[i])==54){if(found>=0)throw new InvalidOperationException("Multiple workplanes.");found=i;}
                if(found<0)throw new InvalidOperationException("Workplane missing.");
                int composite=Program.StrictInt(items[found]);double[] actual=Program.ArrayValues((object)document.GetWorkplaneInfo(composite)).Select(Program.StrictDouble).ToArray();
                ValidateWorkplane(actual);if(!TranslationTest.Same(actual,expected))throw new InvalidOperationException("World workplane origin/axes differ.");
                return new Dictionary<string,object>{{"compositeId",composite},{"nativeId",Program.StrictInt(idList[found])},{"worldInfo",actual},{"layout","origin XYZ; Z direction XYZ; Y direction XYZ"},{"activeWorkplane",Program.StrictInt(document.ActiveWorkplane)}};
            }
            finally{if(Marshal.IsComObject(reference))Marshal.ReleaseComObject(reference);}
        }
        public static double[] RotatePoint(double[] point,int angle)
        {double radians=angle*Math.PI/180.0,cos=Math.Cos(radians),sin=Math.Sin(radians);return new[]{point[0]*cos-point[1]*sin,point[0]*sin+point[1]*cos,point[2]};}
        public static double[] RotateBounds(double[] bounds,int angle)
        {
            var points=new List<double[]>();
            foreach(double x in new[]{bounds[0],bounds[1]})foreach(double y in new[]{bounds[2],bounds[3]})foreach(double z in new[]{bounds[4],bounds[5]})points.Add(RotatePoint(new[]{x,y,z},angle));
            return new[]{points.Min(p=>p[0]),points.Max(p=>p[0]),points.Min(p=>p[1]),points.Max(p=>p[1]),points.Min(p=>p[2]),points.Max(p=>p[2])};
        }
        public static string Digest(string text)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
        public static string CopyFrozen(string source,string destination)
        {
            string before=Program.Hash(source);
            using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {input.CopyTo(output);output.Flush(true);}
            string copied=Program.Hash(destination);
            if(copied!=before||Program.Hash(source)!=before)throw new IOException("Checkpoint source changed during copy; mutation suppressed.");
            return copied;
        }
        public static double[] ParseInput(string input)
        {
            if(input.Length>4096)throw new ArgumentException("Input too large.");
            var values=Program.Json.Deserialize<Dictionary<string,object>>(input);
            if(values==null||values.Count!=1||!values.ContainsKey("deltaWorldMm"))throw new ArgumentException("Only deltaWorldMm accepted.");
            var array=values["deltaWorldMm"] as System.Collections.IList;
            if(array==null)throw new ArgumentException("deltaWorldMm must be an array.");
            double[] delta=array.Cast<object>().Select(Program.StrictDouble).ToArray();ValidateVector(delta);return delta;
        }
        internal static string Geometry(CheckpointTest.Measurement m){return Digest(Program.Json.Serialize(m));}
        internal static string Fingerprint(int pid,string path,CheckpointTest.Measurement m)
        {return Digest(pid.ToString(CultureInfo.InvariantCulture)+"|"+Path.GetFullPath(path).ToUpperInvariant()+"|"+Geometry(m));}
        public static void ValidatePreparedPlan(Plan p,int pid,string path,string fingerprint,string geometryFingerprint,DateTime now)
        {
            Guid operation;DateTime created;
            if(p==null||!Guid.TryParseExact(p.operationId,"N",out operation)||
               !DateTime.TryParseExact(p.createdUtc,"o",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out created))throw new ArgumentException("Invalid plan identity/time.");
            if(p.recipeId=="rotate_z"){if(!p.angleDegrees.HasValue)throw new ArgumentException("Missing angle.");ValidateAngle(p.angleDegrees.Value);}
            else if(p.recipeId=="assign_test_level"){if(!p.targetLevelNumber.HasValue||!p.beforeLevelNumber.HasValue)throw new ArgumentException("Missing level identity.");ValidateTestLevel(p.targetLevelNumber.Value);}
            else if(p.recipeId=="create_workplane_world")ValidateWorkplane(p.workplaneWorldInfo);
            else if(p.recipeId=="scale_uniform_world"){if(!p.scaleFactor.HasValue)throw new ArgumentException("Missing scale factor.");ValidateScale(p.scaleFactor.Value);}
            else if(p.recipeId=="slice_test_cube_xy") {
                if(p.sliceHeightsWorldMm==null||!SliceRecipes.Heights(p.sliceParametersMm).SequenceEqual(p.sliceHeightsWorldMm)||p.deltaWorldMm==null||p.deltaWorldMm.Length!=3||p.deltaWorldMm.Any(x=>x!=0))throw new ArgumentException("Changed slice postconditions.");
            }
            else if((p.recipeId=="convert_test_solid_to_surfaces"||p.recipeId=="offset_test_top_surface_1mm")) {if(p.deltaWorldMm==null||p.deltaWorldMm.Length!=3||p.deltaWorldMm.Any(x=>x!=0))throw new ArgumentException("Conversion does not accept a transform.");}
            else if(p.recipeId==null||p.recipeId=="translate")ValidateVector(p.deltaWorldMm);
            else throw new ArgumentException("Unknown plan recipe.");
            if(p.recipeId=="offset_test_top_surface_1mm" &&
               (p.offsetDistanceMm!=1 || p.targetSide!=5 || p.expectedTopZMm!=21 ||
                p.expectedSurfaceUnionBoundsWorldMm==null || !p.expectedSurfaceUnionBoundsWorldMm.SequenceEqual(new[]{-10.0,10.0,-10.0,10.0,0.0,21.0})))
                throw new ArgumentException("Missing or changed fixed surface offset postconditions.");
            if(p.expectedBoundsWorldMm==null||p.expectedBoundsWorldMm.Length!=6||p.expectedCogWorldMm==null||p.expectedCogWorldMm.Length!=3||
               p.expectedBoundsWorldMm.Concat(p.expectedCogWorldMm).Any(x=>double.IsNaN(x)||double.IsInfinity(x)))throw new ArgumentException("Invalid plan postconditions.");
            if(created.Kind!=DateTimeKind.Utc||created>now||now-created>TimeSpan.FromMinutes(5))throw new InvalidOperationException("Plan expired; prepare again.");
            if(p.processId!=pid||p.modelPath!=path||p.fingerprint!=fingerprint||p.geometryFingerprint!=geometryFingerprint)
                throw new InvalidOperationException("Model changed since planning; no command executed.");
        }
        internal static void SaveJson(string path,object data)
        {
            string temp=path+".tmp";
            byte[] bytes=new UTF8Encoding(false).GetBytes(Program.Json.Serialize(data));
            using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
        static void Verify(CheckpointTest.Measurement before,CheckpointTest.Measurement after,double[] delta,int? angle=null,double? scale=null)
        {
            double[] expectedBounds=angle.HasValue?RotateBounds(before.boundsWorldMm,angle.Value):ShiftBounds(before.boundsWorldMm,delta);
            double[] expectedCog=angle.HasValue?RotatePoint(before.cogWorldMm,angle.Value):before.cogWorldMm.Zip(delta,(x,y)=>x+y).ToArray();
            if(scale.HasValue){expectedBounds=before.boundsWorldMm.Select(x=>x*scale.Value).ToArray();expectedCog=before.cogWorldMm.Select(x=>x*scale.Value).ToArray();}
            double volume=before.volumeRaw*(scale.HasValue?Math.Pow(scale.Value,3):1),area=before.areaRaw*(scale.HasValue?Math.Pow(scale.Value,2):1);
            if(!TranslationTest.Same(expectedBounds,after.boundsWorldMm)||
               !TranslationTest.Same(expectedCog,after.cogWorldMm)||
               !TranslationTest.Same(new[]{volume,area},new[]{after.volumeRaw,after.areaRaw})||
               before.volumeUnitsRaw!=after.volumeUnitsRaw||before.surfaceCount!=after.surfaceCount||
               before.closed!=after.closed||before.connected!=after.connected||before.watertight!=after.watertight||before.trimmingValid!=after.trimmingValid)
                throw new InvalidOperationException("Recipe geometry postconditions failed. Inspect the checkpoint without saving over it.");
        }
        public static Dictionary<string,object> Run(bool execute,double[] requestedDelta=null,string requestedId=null,string requestedDigest=null,int? requestedAngle=null,string requestedRecipe="translate",int? requestedLevel=null,double[] requestedWorkplane=null,double? requestedScale=null,double[] requestedSlices=null)
        {
            var r=new Dictionary<string,object>{{"schemaVersion",1},{"recipe",requestedRecipe=="slice_test_cube_xy"?"planned_slice_test_cube_xy_v1":requestedRecipe=="offset_test_top_surface_1mm"?"planned_offset_test_top_surface_1mm_v1":requestedRecipe=="convert_test_solid_to_surfaces"?"planned_convert_test_solid_to_surfaces_v1":requestedRecipe=="scale_uniform_world"?"planned_scale_uniform_world_v1":requestedRecipe=="create_workplane_world"?"planned_create_workplane_world_v1":requestedRecipe=="assign_test_level"?"planned_assign_test_level_v1":requestedRecipe=="rotate_z"?"planned_rotate_world_z_v1":"planned_translate_world_mm_v1"},{"state","rejected_before_mutation"},{"mutationAttempted",false}};
            object app=null,doc=null;
            bool attempted=false;
            string journal=null;
            try
            {
                Config c=Program.ReadConfig();TranslationTest.ValidateSandbox(c);
                if(!c.parameterizedTranslationPanel)throw new InvalidOperationException("Planned translation disabled.");
                string prior=Path.GetFullPath(Path.Combine(Program.Root,"..","P2.2","reports","checkpoint-test-result.json"));
                var receipt=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(prior,Encoding.UTF8));
                if(!Equals(receipt["state"],"succeeded_saved_and_reopened"))throw new InvalidOperationException("Checkpoint qualification missing.");
                string operations=Path.Combine(Program.Root,"operations");Directory.CreateDirectory(operations);
                using(var exclusive=new FileStream(Path.Combine(operations,"executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                {
                    app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;
                    int pid=Program.StrictInt(ps.Evaluate("app.pid"));
                    if(!Process.GetProcessesByName("powershape").Any(proc=>proc.Id==pid&&proc.SessionId==Process.GetCurrentProcess().SessionId)||Convert.ToBoolean(ps.Busy()))
                        throw new InvalidOperationException("Unexpected or busy instance.");
                    string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});
                    if(version!=c.expectedVersion)throw new InvalidOperationException("Version mismatch.");
                    string path=CheckpointTest.ModelPath(ps);r["connectedProcessId"]=pid;r["connectedModelPath"]=path;
                    if(!path.Equals(Path.GetFullPath(c.expectedProject),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Activate baseline Test_Block_P2 first.");
                    CheckpointTest.Measurement before=CheckpointTest.Measure(ps);
                    int beforeLevel=SolidLevel(ps,before.nativeId);
                    if(requestedRecipe=="assign_test_level"&&Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Level assignment clears selection; this test requires an empty selection.");
                    if(requestedRecipe=="create_workplane_world"&&Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Workplane test requires an empty selection.");
                    if(requestedRecipe=="scale_uniform_world"&&Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Scale test requires an empty selection.");
                    if(!TranslationTest.Same(before.boundsWorldMm,new[]{-10.0,10.0,-10.0,10.0,0.0,20.0})||!TranslationTest.Same(before.cogWorldMm,new[]{0.0,0.0,10.0}))
                        throw new InvalidOperationException("Baseline geometry differs; current prototype supports only the qualified test block.");
                    string planPath=requestedId==null?Path.Combine(Program.Root,"translation-plan.json"):Path.Combine(Program.Root,"plans",requestedId+".json");
                    if(!execute)
                    {
                        double[] delta=null;
                        if(requestedRecipe=="rotate_z"){if(!requestedAngle.HasValue)throw new ArgumentException("Missing angle.");ValidateAngle(requestedAngle.Value);}
                        else if(requestedRecipe=="slice_test_cube_xy"){SliceRecipes.Heights(requestedSlices);if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Slicing requires empty selection.");delta=new double[3];}
                        else if(requestedRecipe=="convert_test_solid_to_surfaces"||requestedRecipe=="offset_test_top_surface_1mm"){if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Conversion requires empty selection.");delta=new[]{0.0,0.0,0.0};}
                        else if(requestedRecipe=="scale_uniform_world"){if(!requestedScale.HasValue)throw new ArgumentException("Missing scale factor.");ValidateScale(requestedScale.Value);delta=new[]{0.0,0.0,0.0};}
                        else if(requestedRecipe=="create_workplane_world"){ValidateWorkplane(requestedWorkplane);delta=new[]{0.0,0.0,0.0};}
                        else if(requestedRecipe=="assign_test_level")
                        {
                            if(!requestedLevel.HasValue)throw new ArgumentException("Missing test level.");ValidateTestLevel(requestedLevel.Value);
                            if(Program.StrictInt(ps.Evaluate("LEVEL["+requestedLevel.Value+"].USED"))!=0)throw new InvalidOperationException("Target test level is already used.");
                            delta=new[]{0.0,0.0,0.0};
                        }
                        else{delta=requestedDelta??ParseInput(File.ReadAllText(Path.Combine(Program.Root,"translation-input.json"),Encoding.UTF8));ValidateVector(delta);}
                        var plan=new Plan {operationId=Guid.NewGuid().ToString("N"),createdUtc=DateTime.UtcNow.ToString("o"),processId=pid,modelPath=path,
                            fingerprint=Fingerprint(pid,path,before),geometryFingerprint=Geometry(before),deltaWorldMm=delta,
                            recipeId=requestedRecipe,angleDegrees=requestedAngle,
                            beforeLevelNumber=beforeLevel,targetLevelNumber=requestedLevel,
                            workplaneWorldInfo=requestedWorkplane,
                            scaleFactor=requestedScale,
                            expectedBoundsWorldMm=requestedAngle.HasValue?RotateBounds(before.boundsWorldMm,requestedAngle.Value):ShiftBounds(before.boundsWorldMm,delta),expectedCogWorldMm=requestedAngle.HasValue?RotatePoint(before.cogWorldMm,requestedAngle.Value):before.cogWorldMm.Zip(delta,(x,y)=>x+y).ToArray(),
                            qualification="single qualified solid; world mm; measured-property fingerprint is not a complete model revision"};
                        if(requestedScale.HasValue){plan.expectedBoundsWorldMm=before.boundsWorldMm.Select(x=>x*requestedScale.Value).ToArray();plan.expectedCogWorldMm=before.cogWorldMm.Select(x=>x*requestedScale.Value).ToArray();}
                        if(requestedRecipe=="slice_test_cube_xy") {
                            plan.sliceParametersMm=requestedSlices;plan.sliceHeightsWorldMm=SliceRecipes.Heights(requestedSlices);
                            plan.qualification="Fixed cube; 1..3 interior XY slices in world mm, explicit first/last height and exact step grid; one closed square per slice, length 80 mm. Bounds/COG describe unchanged source solid. Previous principal plane not restored; sets XY.";
                        }
                        if(requestedRecipe=="offset_test_top_surface_1mm") {
                            plan.offsetDistanceMm=1;plan.targetSide=5;plan.expectedTopZMm=21;
                            plan.expectedSurfaceUnionBoundsWorldMm=new[]{-10.0,10.0,-10.0,10.0,0.0,21.0};
                            plan.qualification="Baseline solid bounds/COG identify the input; final result is six separate surfaces. Explicit surface postconditions describe the offset top face; closed-shell equivalence is not promised.";
                        }
                        SaveJson(planPath,plan);Directory.CreateDirectory(Path.Combine(Program.Root,"plans"));
                        using(var immutable=new FileStream(Path.Combine(Program.Root,"plans",plan.operationId+".json"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
                        {byte[] bytes=Encoding.UTF8.GetBytes(Program.Json.Serialize(plan));immutable.Write(bytes,0,bytes.Length);immutable.Flush(true);}
                        r["state"]="plan_ready";r["plan"]=plan;r["planSha256"]=Digest(File.ReadAllText(planPath,Encoding.UTF8));r["before"]=before;return r;
                    }
                    string planText=File.ReadAllText(planPath,Encoding.UTF8);
                    if(planText.Length>65536)throw new ArgumentException("Plan too large.");
                    string reviewed=requestedDigest??File.ReadAllText(Path.Combine(Program.Root,"reviewed-plan.sha256"),Encoding.UTF8).Trim();
                    if(Digest(planText)!=reviewed)throw new InvalidOperationException("Reviewed plan changed; prepare a new plan.");
                    Plan p=Program.Json.Deserialize<Plan>(planText);
                    if((p.recipeId??"translate")!=requestedRecipe)throw new InvalidOperationException("Plan recipe differs from requested executor.");
                    if(requestedId!=null&&p.operationId!=requestedId)throw new InvalidOperationException("Requested plan ID differs.");
                    Guid replayId;if(!Guid.TryParseExact(p.operationId,"N",out replayId))throw new ArgumentException("Invalid plan operation ID.");
                    if(File.Exists(Path.Combine(operations,p.operationId+".once")))throw new InvalidOperationException("Operation ID already claimed; replay forbidden. Inspect its journal.");
                    ValidatePreparedPlan(p,pid,path,Fingerprint(pid,path,before),Geometry(before),DateTime.UtcNow);
                    if(p.beforeLevelNumber!=beforeLevel)throw new InvalidOperationException("Solid level changed since planning.");
                    if(p.recipeId=="assign_test_level"&&Program.StrictInt(ps.Evaluate("LEVEL["+p.targetLevelNumber.Value+"].USED"))!=0)throw new InvalidOperationException("Target level changed since planning.");
                    int? angle=p.recipeId=="rotate_z"?p.angleDegrees:null;
                    double? scale=p.recipeId=="scale_uniform_world"?p.scaleFactor:null;
                    if(!TranslationTest.Same(p.expectedBoundsWorldMm,scale.HasValue?before.boundsWorldMm.Select(x=>x*scale.Value).ToArray():angle.HasValue?RotateBounds(before.boundsWorldMm,angle.Value):ShiftBounds(before.boundsWorldMm,p.deltaWorldMm))||
                       !TranslationTest.Same(p.expectedCogWorldMm,scale.HasValue?before.cogWorldMm.Select(x=>x*scale.Value).ToArray():angle.HasValue?RotatePoint(before.cogWorldMm,angle.Value):before.cogWorldMm.Zip(p.deltaWorldMm,(x,y)=>x+y).ToArray()))throw new InvalidOperationException("Plan postconditions inconsistent.");
                    string folder=Path.GetFullPath(Path.Combine(Program.Root,"..","..","P2_Operations"));Directory.CreateDirectory(folder);
                    string checkpoint=Path.Combine(folder,"Checkpoint_"+p.operationId+".psmodel"),result=Path.Combine(folder,"Result_"+p.operationId+".psmodel");
                    string working=Path.Combine(folder,"Working_"+p.operationId+".psmodel");
                    CheckpointTest.ValidatePath(checkpoint);CheckpointTest.ValidatePath(result);CheckpointTest.ValidatePath(working);
                    if(File.Exists(checkpoint)||File.Exists(result)||File.Exists(working))throw new IOException("Output collision; overwrite forbidden.");
                    string gate=Path.Combine(operations,p.operationId+".once");
                    using(var locked=new FileStream(gate,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    {byte[] bytes=Encoding.UTF8.GetBytes(reviewed);locked.Write(bytes,0,bytes.Length);locked.Flush(true);}
                    journal=Path.Combine(operations,p.operationId+".json");
                    r["operationId"]=p.operationId;r["plan"]=p;r["before"]=before;r["checkpointPath"]=checkpoint;r["resultPath"]=result;r["workingProjectPath"]=working;
                    r["beforeLevelNumber"]=beforeLevel;
                    attempted=true;r["mutationAttempted"]=true;r["state"]="outcome_unknown";
                    TranslationTest.Journal(journal,r,"before_working_save_as");
                    ps.Exec("FILE SAVEASPATH "+working);
                    if(!File.Exists(working)||new FileInfo(working).Length==0||CheckpointTest.ModelPath(ps)!=working)throw new IOException("Working save not verified.");
                    // SaveAs may change model identity; geometric identity must remain unchanged.
                    if(Geometry(CheckpointTest.Measure(ps))!=p.geometryFingerprint)throw new InvalidOperationException("Geometry changed during checkpoint save.");
                    TranslationTest.Journal(journal,r,"before_freeze_checkpoint");
                    r["checkpointSha256"]=CopyFrozen(working,checkpoint);
                    r["checkpointStrategy"]="detached byte copy; never opened as active during mutation";
                    TranslationTest.ValidateSandbox(c);
                    TranslationTest.Journal(journal,r,p.recipeId=="create_workplane_world"?"before_workplane_creation":p.recipeId=="assign_test_level"?"before_level_assignment":angle.HasValue?"before_rotate":"before_translate");
                    doc=ps.ActiveDocument;dynamic document=doc;
                    // OLE axis is an array of VARIANT elements (SAFEARRAY(VARIANT)),
                    // not a typed SAFEARRAY(R8), which 23.2.2 rejects with -1.
                    int code;
                    if(scale.HasValue)
                    {
                        if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0||Program.StrictInt(document.ActiveWorkplane)!=0)throw new InvalidOperationException("Scale context changed during checkpoint save.");
                        string name=Convert.ToString(document.GetItemName(before.compositeId));
                        if(string.IsNullOrWhiteSpace(name)||name.Length>128||name.IndexOfAny(new[]{'\'','\r','\n'})>=0)throw new InvalidOperationException("Invalid solid name for fixed selection command.");
                        ps.Exec("SELECT CLEARLIST ADD SOLID '"+name+"'");
                        if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=1||!string.Equals(Convert.ToString(ps.Evaluate("SELECTION.TYPE[0]")),"SOLID",StringComparison.OrdinalIgnoreCase)||Convert.ToString(ps.Evaluate("SELECTION.NAME[0]"))!=name)throw new InvalidOperationException("Scale target selection failed.");
                        foreach(string command in new[]{"EDIT SCALE","UNIFORM","VALUE "+scale.Value.ToString("R",CultureInfo.InvariantCulture),"LOCK X OFF","LOCK Y OFF","LOCK Z OFF","SCALEORIGIN","0 0 0","APPLY","DISMISS","SELECT CLEARLIST"})ps.Exec(command);
                        if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Scale selection context was not cleared.");
                        r["scaleFactor"]=scale.Value;r["scaleOriginWorldMm"]=new[]{0.0,0.0,0.0};r["scaleBackend"]="fixed Autodesk PSEntityScaler command recipe";code=1;
                    }
                    else if(p.recipeId=="create_workplane_world")
                    {
                        if(Program.StrictInt(document.ActiveWorkplane)!=0||Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Workplane context changed during checkpoint save.");
                        code=Program.StrictInt(document.CreateWorkplane(p.workplaneWorldInfo.Cast<object>().ToArray()));
                        r["createdWorkplaneCompositeId"]=code;
                        if(code<=0)throw new InvalidOperationException("CreateWorkplane rejected input.");
                        r["createdWorkplaneActiveRaw"]=Program.StrictInt(document.ActiveWorkplane);
                        if((int)r["createdWorkplaneActiveRaw"]!=code)throw new InvalidOperationException("Created workplane did not become active.");
                        r["workplaneWorldInfo"]=p.workplaneWorldInfo;
                        r["workplaneCreated"]=InspectWorkplane(ps,p.workplaneWorldInfo);
                        document.ActiveWorkplane=0;
                        if(Program.StrictInt(document.ActiveWorkplane)!=0)throw new InvalidOperationException("World coordinate context was not restored.");
                        code=1;
                    }
                    else if(p.recipeId=="assign_test_level")
                    {
                        int target=p.targetLevelNumber.Value;
                        if(Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"))!=0||SolidLevel(ps,before.nativeId)!=beforeLevel||Program.StrictInt(ps.Evaluate("LEVEL["+target+"].USED"))!=0)throw new InvalidOperationException("Level context changed during checkpoint save.");
                        code=Convert.ToBoolean(document.SetItemsLevel(before.compositeId,target))?1:0;
                        if(code!=1)throw new InvalidOperationException("SetItemsLevel returned false.");
                        string levelName="AI_Bridge_Test_"+target;document.SetLevelName(target,levelName);
                        // OLE updates model metadata before Evaluate's UI-derived values settle.
                        // Poll read-only postconditions; never replay either editing call.
                        bool settled=false;
                        for(int check=0;check<20;check++)
                        {
                            r["observedLevelNumber"]=SolidLevel(ps,before.nativeId);
                            r["observedLevelName"]=Convert.ToString(ps.Evaluate("LEVEL["+target+"].NAME"));
                            r["observedSelectionCount"]=Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"));
                            if((int)r["observedLevelNumber"]==target&&(string)r["observedLevelName"]==levelName&&(int)r["observedSelectionCount"]==0){settled=true;break;}
                            System.Threading.Thread.Sleep(100);
                        }
                        if(!settled)throw new InvalidOperationException("Level assignment/name postconditions failed.");
                        r["afterLevelNumber"]=target;r["afterLevelName"]=levelName;
                    }
                    else code=angle.HasValue?Program.StrictInt(document.RotateItem(before.compositeId,angle.Value,new object[]{0.0,0.0,0.0,0.0,0.0,1.0})):Program.StrictInt(document.TranslateItem(before.compositeId,p.deltaWorldMm[0],p.deltaWorldMm[1],p.deltaWorldMm[2]));
                    Marshal.FinalReleaseComObject(doc);doc=null;r[p.recipeId=="create_workplane_world"?"workplanePostconditionsCode":p.recipeId=="assign_test_level"?"setItemsLevelReturnCode":angle.HasValue?"rotateReturnCode":"translateReturnCode"]=code;
                    if(code!=1)throw new InvalidOperationException(angle.HasValue?"RotateItem did not return success.":"TranslateItem did not return success.");
                    CheckpointTest.Measurement after=CheckpointTest.Measure(ps,p.recipeId=="create_workplane_world");Verify(before,after,p.deltaWorldMm,angle,scale);r["after"]=after;
                    if(CheckpointTest.ModelPath(ps)!=working)throw new InvalidOperationException("Active model changed unexpectedly.");
                    TranslationTest.Journal(journal,r,"before_result_save_as");
                    if(File.Exists(result))throw new IOException("Result collision.");
                    ps.Exec("FILE SAVEASPATH "+result);
                    if(!File.Exists(result)||new FileInfo(result).Length==0||CheckpointTest.ModelPath(ps)!=result)throw new IOException("Result save not verified.");
                    Verify(before,CheckpointTest.Measure(ps,p.recipeId=="create_workplane_world"),p.deltaWorldMm,angle,scale);
                    if(p.recipeId=="create_workplane_world")r["workplaneAfterSave"]=InspectWorkplane(ps,p.workplaneWorldInfo);
                    if(p.recipeId=="assign_test_level"&&(SolidLevel(ps,before.nativeId)!=p.targetLevelNumber.Value||Convert.ToString(ps.Evaluate("LEVEL["+p.targetLevelNumber.Value+"].NAME"))!=(string)r["afterLevelName"]))throw new InvalidOperationException("Level metadata changed during SaveAs.");
                    if(Program.Hash(checkpoint)!=(string)r["checkpointSha256"])throw new IOException("Checkpoint changed.");
                    r["resultSha256"]=Program.Hash(result);TranslationTest.ValidateSandbox(c);
                    r["originalAndBaselineHashesUnchanged"]=true;r["state"]="succeeded_saved";
                    r["verificationScope"]="measured geometry and applicable level metadata after operation and SaveAs; result disk reopen is a separate audit; full topology/context not certified";
                    TranslationTest.Journal(journal,r,"completed");
                }
            }
            catch(Exception ex)
            {
                r["state"]=attempted?"outcome_unknown_requires_manual_check":"rejected_before_mutation";r["error"]=ex.Message;
                if(attempted)r["recoveryInstructions"]="Stop; inspect operation journal. Do not save over the checkpoint. Reopen the listed checkpoint manually if recovery is needed; no automatic retry.";
                if(journal!=null)try{TranslationTest.Journal(journal,r,"stopped");}catch{}
            }
            finally
            {
                if(doc!=null&&Marshal.IsComObject(doc))Marshal.FinalReleaseComObject(doc);
                if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);
            }
            return r;
        }
        public static void AddControls(Form panel,FlowLayoutPanel bar,Button read,Label status,TextBox output)
        {
            panel.Text="PowerShape Bridge — перемещение по плану P2.3.1";bar.Height=130;
            status.Text="Один тестовый solid; глобальная система; мм";
            var boxes=new[]{new TextBox{Text="5",Width=65},new TextBox{Text="-2",Width=65},new TextBox{Text="3",Width=65}};
            for(int i=0;i<3;i++){bar.Controls.Add(new Label{Text=new[]{"X, мм","Y, мм","Z, мм"}[i],AutoSize=true});bar.Controls.Add(boxes[i]);}
            var prepare=new Button{Text="Подготовить план",AutoSize=true};var apply=new Button{Text="Выполнить план",AutoSize=true,Enabled=false};
            bar.Controls.Add(prepare);bar.Controls.Add(apply);
            string displayedPlan=null;
            foreach(var box in boxes)box.TextChanged+=delegate{displayedPlan=null;apply.Enabled=false;};
            prepare.Click+=async delegate
            {
                if(!read.Enabled)return;
                prepare.Enabled=false;apply.Enabled=false;read.Enabled=false;foreach(var b in boxes)b.Enabled=false;
                try
                {
                    double[] delta=boxes.Select(b=>double.Parse(b.Text,NumberStyles.Float,CultureInfo.CurrentCulture)).ToArray();ValidateVector(delta);
                    SaveJson(Path.Combine(Program.Root,"translation-input.json"),new{deltaWorldMm=delta});
                    string text=await Task.Run(()=>Panel.RunChild(30,"--prepare-translation"));output.Text=text;
                    var result=Program.Json.Deserialize<Dictionary<string,object>>(text);
                    if(Equals(result["state"],"plan_ready"))
                    {
                        displayedPlan=File.ReadAllText(Path.Combine(Program.Root,"translation-plan.json"),Encoding.UTF8);
                        if(Digest(displayedPlan)!=(string)result["planSha256"])throw new InvalidOperationException("Plan file changed before review; prepare again.");
                        status.Text="План: X="+delta[0]+", Y="+delta[1]+", Z="+delta[2]+" мм. Проверьте ожидаемые границы в отчёте.";apply.Enabled=true;
                    }
                    else {displayedPlan=null;status.Text="План не подготовлен — прочитайте отчёт";}
                }
                catch(Exception ex){displayedPlan=null;output.Text=ex.Message;status.Text="Проверьте параметры";}
                finally{prepare.Enabled=true;read.Enabled=true;foreach(var b in boxes)b.Enabled=true;}
            };
            apply.Click+=async delegate
            {
                if(!read.Enabled||displayedPlan==null)return;
                apply.Enabled=false;prepare.Enabled=false;read.Enabled=false;foreach(var b in boxes)b.Enabled=false;
                try
                {
                    File.WriteAllText(Path.Combine(Program.Root,"reviewed-plan.sha256"),Digest(displayedPlan),new UTF8Encoding(false));
                    string text=await Task.Run(()=>Panel.RunChild(60,"--execute-translation"));output.Text=text;
                    var result=Program.Json.Deserialize<Dictionary<string,object>>(text);
                    status.Text=Equals(result["state"],"succeeded_saved")?"Перемещение проверено, результат сохранён":"Остановлено — проверьте журнал";
                    Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));SaveJson(Path.Combine(Program.Root,"reports","planned-translation-result.json"),result);
                }
                catch(Exception ex){output.Text=ex.ToString();status.Text="Исход требует проверки";}
                finally{displayedPlan=null;prepare.Enabled=true;read.Enabled=true;foreach(var b in boxes)b.Enabled=true;}
            };
        }
    }
}

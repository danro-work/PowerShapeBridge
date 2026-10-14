using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 // Strict filesystem-only preparation. It never selects/exports/activates CAD.
 internal static class OwnedDgkTargets {
  const string SelectionPolicy="explicit_target_set; no empty-selection fallback";
  const string Qualification="Disabled DGK target/bundle plan; settings/provider/geometry unqualified; no CAD command authority. Working ordinary data writes locked; EOF/mapped changes require retained identity/length/SHA rechecks, no continuous write exclusion.";
  static readonly string[] Refs={"bindingId","bindingSha256","entitySetId","entitySetSha256"};
  static void Require(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
  static void Fields(Dictionary<string,object> value,params string[] names){Require(value!=null&&value.Count==names.Length&&names.All(value.ContainsKey),"Exact DGK schema required.");}
  static object[] List(object value,int limit){var list=value as IList;Require(list!=null&&list.Count<=limit,"Bounded explicit DGK list required.");return list.Cast<object>().ToArray();}
  static Dictionary<string,object> Detached(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  static string Digest(object value){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(EntitySets.Canonical(value)))).Replace("-","").ToLowerInvariant();}
  internal static string Folder(string root,string id) {
   Require(EntitySets.Hex(id,32),"DGK target identity required.");
   string path=Path.Combine(OwnedCopies.LocalPath(root.TrimEnd('\\','/')),"dgkexports",id);OwnedCopies.NoReparse(path);return path;
  }
  internal static string BundleFolder(string root,string id){return Path.Combine(Folder(root,id),"artifacts");}
  internal static string Stem(string id){Require(EntitySets.Hex(id,32),"DGK target identity required.");return "Geometry_"+id;}
  internal static DgkBundleVerifier.Plan Bundle(string id,Dictionary<string,object> parameters) {
   Fields(parameters,"coordinateFrame","exportVariant","triangleCompanions","featureCompanions","settingsQualification");
   Require(Equals(parameters["coordinateFrame"],"world")&&Equals(parameters["settingsQualification"],"planned_unqualified"),"Initial DGK plan requires explicit world frame and unqualified planned settings.");
   Require(Equals(parameters["exportVariant"],"geometry")||Equals(parameters["exportVariant"],"geometry_features"),"Explicit DGK variant required.");
   string stem=Stem(id);var artifacts=new List<DgkBundleVerifier.Artifact>{new DgkBundleVerifier.Artifact(stem+".dgk","geometry_dgk",true)};
   foreach(object raw in List(parameters["triangleCompanions"],2)) {
    var item=raw as Dictionary<string,object>;Fields(item,"suffix","required");
    Require(item["required"] is bool&&(Equals(item["suffix"],".dmt")||Equals(item["suffix"],".dgk.dmt")),"Explicit documented triangle suffix/boolean required.");
    artifacts.Add(new DgkBundleVerifier.Artifact(stem+(string)item["suffix"],"triangles_dmt",(bool)item["required"]));
   }
   object[] features=List(parameters["featureCompanions"],2);
   Require(Equals(parameters["exportVariant"],"geometry")?features.Length==0:features.Length>0,"DGK variant and feature manifest differ.");
   foreach(object raw in features) {
    var item=raw as Dictionary<string,object>;Fields(item,"name","role","required");
    Require(item["name"] is string&&item["required"] is bool&&(Equals(item["role"],"features_mac")||Equals(item["role"],"features_xml")),"Explicit feature basename/role/boolean required.");
    artifacts.Add(new DgkBundleVerifier.Artifact((string)item["name"],(string)item["role"],(bool)item["required"]));
   }
   Require(features.Length==0||artifacts.Any(item=>item.Role.StartsWith("features_",StringComparison.Ordinal)&&item.Required),"Features export must declare at least one mandatory companion; missing all features cannot be accepted.");
   return new DgkBundleVerifier.Plan(stem,artifacts);
  }
  static Dictionary<string,object>[] Manifest(DgkBundleVerifier.Plan plan) {
   return plan.Artifacts.Select(item=>new Dictionary<string,object>{{"name",item.Name},{"role",item.Role},{"required",item.Required},{"expectedSha256",item.ExpectedSha256}}).ToArray();
  }
  static Dictionary<string,object> ReferenceArgs(Dictionary<string,object> args){return Refs.ToDictionary(key=>key,key=>args[key]);}
  internal static void Validate(Dictionary<string,object> args) {
   Fields(args,"bindingId","bindingSha256","entitySetId","entitySetSha256","parameters");
   OwnedGeometryExportTargets.Validate(ReferenceArgs(args));Require(args["parameters"] is Dictionary<string,object>,"DGK parameters object required.");
   Bundle(new string('a',32),(Dictionary<string,object>)args["parameters"]);
  }
  static void EmptyDirectory(string path){OwnedCopies.NoReparse(path);Require(Directory.Exists(path)&&!Directory.EnumerateFileSystemEntries(path).Any(),"DGK artifacts directory must remain empty before execution.");}
  internal static Dictionary<string,object> Prepare(string root,string id,Dictionary<string,object> input) {
   var result=new Dictionary<string,object>{{"state","owned_dgk_target_rejected"},{"targetId",id},
    {"executionEnabled",false},{"exportAttempted",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},
    {"cadSaveAttempted",false},{"powerShapeConnectionAttempted",false},{"retryAllowed",false},{"exportSettingsQualified",false},{"nativeProviderQualified",false}};
   try {
    Validate(input);var args=Detached(input);var parameters=(Dictionary<string,object>)args["parameters"];var bundle=Bundle(id,parameters);
    string folder=Folder(root,id),artifacts=BundleFolder(root,id);Require(!File.Exists(folder)&&!Directory.Exists(folder),"DGK target exists; no overwrite.");
    using(var proofs=new OwnedOperationPlans.Proofs()) {
     var source=new OwnedGeometryExportTargets.Dependencies(root,ReferenceArgs(args),proofs,null,true);var binding=source.Binding;
     using(var copy=OwnedCopies.Hold(root,(string)binding["copyId"],(string)binding["ownershipSha256"]))
     using(var workingPin=new OwnedWorkingBytePin(copy.WorkingPath)) {
      copy.CheckPath((string)((Dictionary<string,object>)source.Expected["model"])["path"]);copy.Recheck();
      OwnedGeometryExportTargets.NoExportFence(root,(string)binding["copyId"]);var fence=OwnedGeometryExportTargets.Fence(root,binding,source.Expected,proofs);
      string parent=Path.GetDirectoryName(folder);Directory.CreateDirectory(parent);OwnedCopies.NoReparse(parent);
      using(var rootPin=new OwnedDirectoryLease(root))using(var parentPin=new OwnedDirectoryLease(parent)) {
       result["localArtifactsMayExist"]=true;
       NativeJournal.Freeze(folder+".reservation.json",new Dictionary<string,object>{{"schemaVersion",1},{"targetId",id},{"format","dgk"},{"bindingId",args["bindingId"]},{"bindingSha256",args["bindingSha256"]},{"parametersSha256",Digest(parameters)},{"automaticReplayForbidden",true}});
       Directory.CreateDirectory(folder);EmptyDirectory(folder);Directory.CreateDirectory(artifacts);EmptyDirectory(artifacts);
       using(var folderPin=new OwnedDirectoryLease(folder))using(var bundlePin=new OwnedDirectoryLease(artifacts)) {
        foreach(var item in bundle.Artifacts)OwnedGeometryExport.QuotedPath(Path.Combine(artifacts,item.Name));
        var plan=new Dictionary<string,object>(args){{"schemaVersion",1},{"targetId",id},{"operation","export_owned_dgk"},{"format","dgk"},
         {"selectionPolicy",SelectionPolicy},{"parametersSha256",Digest(parameters)},{"preparedUtc",DateTime.UtcNow.ToString("o")},{"expiresUtc",source.Expires.ToString("o")},
         {"executionEnabled",false},{"exportSettingsQualified",false},{"nativeProviderQualified",false},
         {"copyId",binding["copyId"]},{"ownershipSha256",binding["ownershipSha256"]},{"inspectionRequestId",binding["inspectionRequestId"]},
         {"inspectionSha256",binding["inspectionSha256"]},{"nativeObservedPropertiesSha256",binding["observedPropertiesSha256"]},
         {"windowContextSha256",binding["windowContextSha256"]},{"entityKeys",source.Keys},
         {"workingPath",copy.WorkingPath},{"workingSha256",copy.Inspection["workingSha256"]},{"checkpointPath",copy.CheckpointPath},
         {"checkpointSha256",copy.CheckpointSha},{"bundleDirectory",artifacts},{"targetPath",Path.Combine(artifacts,Stem(id)+".dgk")},
         {"artifactManifest",Manifest(bundle)},{"directoryIdentity",folderPin.Identity},{"bundleDirectoryIdentity",bundlePin.Identity},
         {"fenceObservation",fence},{"qualification",Qualification}};
        string sha=NativeJournal.Freeze(Path.Combine(folder,"target.json"),plan);
        rootPin.Recheck();parentPin.Recheck();folderPin.Recheck();bundlePin.Recheck();copy.Recheck();EmptyDirectory(artifacts);
        Require(OwnedCopies.Digest(workingPin.Stream)==(string)copy.Inspection["workingSha256"],"Pinned DGK working bytes changed.");
        Require(EntitySets.Canonical(OwnedGeometryExportTargets.Fence(root,binding,source.Expected,proofs))==EntitySets.Canonical(fence),"Copy fence changed during DGK preparation.");
        Require(DateTime.UtcNow<source.Expires,"DGK preparation expired.");result["state"]="owned_dgk_target_prepared_files";result["targetSha256"]=sha;result["target"]=plan;
       }
      }
     }
    }
   }catch(Exception error){result["error"]=error.Message;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  internal sealed class TargetLease : IDisposable {
   internal readonly Dictionary<string,object> Plan;
   internal readonly OwnedCopies.OwnershipLease Copy;
   internal readonly OwnedGeometryExportTargets.Dependencies Source;
   internal readonly DgkBundleVerifier.Plan BundlePlan;
   readonly string root,id;readonly bool verification;readonly OwnedOperationPlans.Proofs proofs=new OwnedOperationPlans.Proofs();
   OwnedDirectoryLease rootPin,parentPin,folderPin,bundlePin;OwnedWorkingBytePin workingPin;
   internal TargetLease(string bridgeRoot,string targetId,string sha,bool forVerification=false) {
    root=OwnedCopies.LocalPath(bridgeRoot.TrimEnd('\\','/'));id=targetId;verification=forVerification;
    try {
     Require(EntitySets.Hex(sha,64),"DGK target SHA required.");string folder=Folder(root,id),artifacts=BundleFolder(root,id);
     rootPin=new OwnedDirectoryLease(root);parentPin=new OwnedDirectoryLease(Path.GetDirectoryName(folder));
     folderPin=new OwnedDirectoryLease(folder);bundlePin=new OwnedDirectoryLease(artifacts);Plan=proofs.Json(Path.Combine(folder,"target.json"),sha);
     Fields(Plan,"bindingId","bindingSha256","entitySetId","entitySetSha256","parameters","schemaVersion","targetId","operation","format",
      "selectionPolicy","parametersSha256","preparedUtc","expiresUtc","executionEnabled","exportSettingsQualified","nativeProviderQualified","copyId","ownershipSha256",
      "inspectionRequestId","inspectionSha256","nativeObservedPropertiesSha256","windowContextSha256","entityKeys","workingPath","workingSha256",
      "checkpointPath","checkpointSha256","bundleDirectory","targetPath","artifactManifest","directoryIdentity","bundleDirectoryIdentity","fenceObservation","qualification");
     Require(Equals(Plan["schemaVersion"],1)&&Equals(Plan["targetId"],id)&&Equals(Plan["operation"],"export_owned_dgk")&&Equals(Plan["format"],"dgk")&&
      Equals(Plan["selectionPolicy"],SelectionPolicy)&&Equals(Plan["qualification"],Qualification)&&Equals(Plan["executionEnabled"],false)&&
      Equals(Plan["exportSettingsQualified"],false)&&Equals(Plan["nativeProviderQualified"],false),"Invalid disabled DGK target.");
     var parameters=Plan["parameters"] as Dictionary<string,object>;Require(parameters!=null,"DGK parameter object missing.");BundlePlan=Bundle(id,parameters);
     Require(Equals(Plan["parametersSha256"],Digest(parameters)),"DGK parameters digest differs.");
     Require(Equals(Plan["bundleDirectory"],artifacts)&&Equals(Plan["targetPath"],Path.Combine(artifacts,Stem(id)+".dgk"))&&
      EntitySets.Canonical(Plan["artifactManifest"])==EntitySets.Canonical(Manifest(BundlePlan))&&
      EntitySets.Canonical(Plan["directoryIdentity"])==EntitySets.Canonical(folderPin.Identity)&&
      EntitySets.Canonical(Plan["bundleDirectoryIdentity"])==EntitySets.Canonical(bundlePin.Identity),"DGK fixed bundle/manifest/directory identity differs.");
     foreach(var item in BundlePlan.Artifacts)OwnedGeometryExport.QuotedPath(Path.Combine(artifacts,item.Name));
     Source=new OwnedGeometryExportTargets.Dependencies(root,ReferenceArgs(Plan),proofs,verification?(DateTime?)EntitySets.Expiry(Plan["preparedUtc"]):null,true);
     foreach(string key in new[]{"copyId","ownershipSha256","inspectionRequestId","inspectionSha256","windowContextSha256"})Require(Equals(Plan[key],Source.Binding[key]),"DGK binding dependency differs.");
     Require(Equals(Plan["nativeObservedPropertiesSha256"],Source.Binding["observedPropertiesSha256"])&&
      EntitySets.Canonical(Plan["entityKeys"])==EntitySets.Canonical(Source.Keys)&&EntitySets.Expiry(Plan["expiresUtc"])==Source.Expires&&
      EntitySets.Expiry(Plan["preparedUtc"])>=EntitySets.Expiry(Source.Expected["timestampUtc"])&&
      EntitySets.Expiry(Plan["preparedUtc"])<=DateTime.UtcNow&&EntitySets.Expiry(Plan["preparedUtc"])<Source.Expires,"DGK observation/targets/lifetime differs.");
     Copy=OwnedCopies.Hold(root,(string)Plan["copyId"],(string)Plan["ownershipSha256"]);Copy.CheckPath((string)((Dictionary<string,object>)Source.Expected["model"])["path"]);
     workingPin=new OwnedWorkingBytePin(Copy.WorkingPath);
     Require(Equals(Plan["workingPath"],Copy.WorkingPath)&&Equals(Plan["workingSha256"],Copy.Inspection["workingSha256"])&&
      Equals(Plan["checkpointPath"],Copy.CheckpointPath)&&Equals(Plan["checkpointSha256"],Copy.CheckpointSha),"DGK owned saved bytes differ.");
     var reservation=proofs.Json(folder+".reservation.json",null);Fields(reservation,"schemaVersion","targetId","format","bindingId","bindingSha256","parametersSha256","automaticReplayForbidden");
     Require(Equals(reservation["schemaVersion"],1)&&Equals(reservation["targetId"],id)&&Equals(reservation["format"],"dgk")&&
      Equals(reservation["bindingId"],Plan["bindingId"])&&Equals(reservation["bindingSha256"],Plan["bindingSha256"])&&
      Equals(reservation["parametersSha256"],Plan["parametersSha256"])&&Equals(reservation["automaticReplayForbidden"],true),"DGK reservation differs.");
     if(verification)RecheckDependencies();else Recheck();
    }catch{Dispose();throw;}
   }
   internal void RecheckDependencies() {
    rootPin.Recheck();parentPin.Recheck();folderPin.Recheck();bundlePin.Recheck();Copy.Recheck();
    Require(OwnedCopies.Digest(workingPin.Stream)==(string)Plan["workingSha256"],"Pinned DGK working bytes changed.");
    EntitySets.CheckObserved(Source.Expected,Source.Expected,Source.Keys,Source.Expires,verification?EntitySets.Expiry(Plan["preparedUtc"]):DateTime.UtcNow);
    Require(EntitySets.Canonical(OwnedGeometryExportTargets.Fence(root,Source.Binding,Source.Expected,proofs))==EntitySets.Canonical(Plan["fenceObservation"]),"DGK copy fence changed.");
   }
   internal void Recheck() {
    Require(!verification,"Historical DGK proof lease cannot authorize execution.");
    RecheckDependencies();OwnedGeometryExportTargets.NoExportFence(root,(string)Plan["copyId"]);EmptyDirectory((string)Plan["bundleDirectory"]);
    string claim=Path.Combine(Folder(root,id),"export-once.json");OwnedCopies.NoReparse(claim);Require(!File.Exists(claim)&&!Directory.Exists(claim),"DGK export already claimed; replay forbidden.");
   }
   public void Dispose(){if(workingPin!=null)workingPin.Dispose();if(Copy!=null)Copy.Dispose();proofs.Dispose();if(bundlePin!=null)bundlePin.Dispose();if(folderPin!=null)folderPin.Dispose();if(parentPin!=null)parentPin.Dispose();if(rootPin!=null)rootPin.Dispose();}
  }
 }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 // Passive retained-handle inspection of a dedicated directory, not ownership
 // authorization, a CAD export provider or a public recipe.
 internal static class DgkBundleFiles {
  static void Require(bool ok,string error){if(!ok)throw new InvalidDataException(error);}
  static string[] Names(string folder,DgkBundleVerifier.Plan plan) {
   var entries=Directory.EnumerateFileSystemEntries(folder).Take(DgkBundleVerifier.MaxArtifacts+1).ToArray();
   Require(entries.Length<=DgkBundleVerifier.MaxArtifacts,"Unexpected DGK bundle directory entry count.");
   foreach(string path in entries) {
    OwnedCopies.NoReparse(path);Require(!Directory.Exists(path),"Nested bundle directories are refused.");
    Require(plan.Artifacts.Any(item=>item.Name==Path.GetFileName(path)),"Undeclared DGK bundle directory entry.");
   }
   return entries.Select(Path.GetFileName).OrderBy(name=>name,StringComparer.Ordinal).ToArray();
  }
  internal sealed class Lease : IDisposable {
   internal readonly Dictionary<string,object> Report;
   readonly string directory;readonly DgkBundleVerifier.Plan plan;
   readonly Dictionary<string,FileStream> files=new Dictionary<string,FileStream>();
   readonly Dictionary<string,object> identities=new Dictionary<string,object>();
   OwnedDirectoryLease folder;string[] before;
   internal Lease(string root,string bundleDirectory,DgkBundleVerifier.Plan bundlePlan) {
    if(bundlePlan==null)throw new ArgumentNullException("bundlePlan");plan=bundlePlan;
    root=OwnedCopies.LocalPath(root).TrimEnd('\\');directory=OwnedCopies.LocalPath(bundleDirectory).TrimEnd('\\');
    Require(directory.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase),"DGK bundle directory must be below the supplied root.");
    try {
    folder=new OwnedDirectoryLease(directory);before=Names(directory,plan);
    foreach(string name in before) {
     string path=Path.Combine(directory,name);OwnedCopies.NoReparse(path);
     var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);files.Add(name,stream);
     identities.Add(name,OwnedCopies.FileIdentity(stream,path,true));
    }
    var report=DgkBundleVerifier.Verify(plan,files.Select(pair=>new KeyValuePair<string,Stream>(pair.Key,pair.Value)));
    foreach(var pair in files)Require(EntitySets.Canonical(identities[pair.Key])==EntitySets.Canonical(OwnedCopies.FileIdentity(pair.Value,Path.Combine(directory,pair.Key),true)),"Retained DGK artifact identity changed.");
    folder.Recheck();Require(before.SequenceEqual(Names(directory,plan)),"DGK directory membership changed.");
    var artifacts=report.Artifacts.Select(item=>new Dictionary<string,object>{{"name",item.Name},{"role",item.Role},
     {"length",item.Length},{"sha256",item.Sha256},{"fileIdentity",identities[item.Name]},
     {"expectedHashMatched",item.ExpectedHashMatched},{"contentQualification",item.ContentQualification},
     {"lengthUnits",item.LengthUnits},{"vertexCount",item.VertexCount},{"triangleCount",item.TriangleCount}}).ToArray();
    Report=new Dictionary<string,object>{{"state","dgk_bundle_bytes_observed_unqualified"},{"schemaVersion",1},
     {"artifacts",artifacts},{"absentOptionalArtifacts",report.AbsentOptionalArtifacts.ToArray()},{"totalBytes",report.TotalBytes},
     {"directory",directory},{"directoryIdentity",folder.Identity},{"timestampUtc",DateTime.UtcNow.ToString("o")},
     {"cadGeometryVerified",false},{"exportSettingsQualified",false},{"ownershipAuthorized",false},
     {"cadConnectionAttempted",false},{"companionExecutionAttempted",false},
     {"qualification","Retained local file bytes/identity and rechecked directory membership only; DGK/MAC/XML opaque; no CAD equivalence or continuous membership tracking."}};
    Recheck();
   }catch{Dispose();throw;}
   }
   internal void Recheck() {
    folder.Recheck();Require(before.SequenceEqual(Names(directory,plan)),"DGK bundle membership changed while held.");
    var artifacts=(Dictionary<string,object>[])Report["artifacts"];
    foreach(var item in artifacts) {
     string name=(string)item["name"];var stream=files[name];
     Require(stream.Length==Convert.ToInt64(item["length"])&&OwnedCopies.Digest(stream)==(string)item["sha256"]&&
      EntitySets.Canonical(identities[name])==EntitySets.Canonical(OwnedCopies.FileIdentity(stream,Path.Combine(directory,name),true)),"DGK held file bytes/identity changed.");
    }
   }
   public void Dispose(){foreach(var stream in files.Values)stream.Dispose();if(folder!=null)folder.Dispose();}
  }
  internal static Dictionary<string,object> Verify(string root,string directory,DgkBundleVerifier.Plan plan) {
   using(var lease=new Lease(root,directory,plan)){return lease.Report;}
  }
 }
}

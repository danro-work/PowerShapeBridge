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
  internal static Dictionary<string,object> Verify(string root,string directory,DgkBundleVerifier.Plan plan) {
   if(plan==null)throw new ArgumentNullException("plan");root=OwnedCopies.LocalPath(root).TrimEnd('\\');
   directory=OwnedCopies.LocalPath(directory).TrimEnd('\\');
   Require(directory.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase),"DGK bundle directory must be below the supplied root.");
   var files=new Dictionary<string,FileStream>();var identities=new Dictionary<string,object>();
   using(var folder=new OwnedDirectoryLease(directory))try {
    string[] before=Names(directory,plan);
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
    return new Dictionary<string,object>{{"state","dgk_bundle_bytes_observed_unqualified"},{"schemaVersion",1},
     {"artifacts",artifacts},{"absentOptionalArtifacts",report.AbsentOptionalArtifacts.ToArray()},{"totalBytes",report.TotalBytes},
     {"directory",directory},{"directoryIdentity",folder.Identity},{"timestampUtc",DateTime.UtcNow.ToString("o")},
     {"cadGeometryVerified",false},{"exportSettingsQualified",false},{"ownershipAuthorized",false},
     {"cadConnectionAttempted",false},{"companionExecutionAttempted",false},
     {"qualification","Retained local file bytes/identity and rechecked directory membership only; DGK/MAC/XML opaque; no CAD equivalence or continuous membership tracking."}};
   }finally{foreach(var stream in files.Values)stream.Dispose();}
  }
 }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PowerShapeGeometry {
 // File-byte witness only. Callers must separately establish ownership, retain file
 // and directory handles, and qualify export settings/CAD identity before use.
 public static class DgkBundleVerifier {
  public const long MaxFileBytes=128L*1024*1024,MaxBundleBytes=256L*1024*1024;
  public const int MaxArtifacts=5;
  public sealed class Artifact {
   public readonly string Name,Role,ExpectedSha256;
   public readonly bool Required;
   public Artifact(string name,string role,bool required,string expectedSha256="") {
    Name=name;Role=role;Required=required;ExpectedSha256=expectedSha256;
   }
  }
  public sealed class Plan {
   public readonly string Stem;
   public readonly ReadOnlyCollection<Artifact> Artifacts;
   public Plan(string stem,IEnumerable<Artifact> artifacts) {
    SafeName(stem);Require(stem.Length<=80&&!stem.Contains('.'),"DGK stem must be a bounded plain basename.");
    if(artifacts==null)throw new ArgumentNullException("artifacts");
    var detached=artifacts.Take(MaxArtifacts+1).ToArray();
    Require(detached.Length>=1&&detached.Length<=MaxArtifacts,"DGK bundle artifact count outside limit.");
    var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach(var item in detached) {
     if(item==null)throw new InvalidDataException("Null DGK artifact.");
     SafeName(item.Name);Require(names.Add(item.Name),"Duplicate DGK artifact basename.");
     Require(item.ExpectedSha256!=null&&(item.ExpectedSha256.Length==0||Hex(item.ExpectedSha256)),"Expected SHA256 must be lowercase hexadecimal.");
     switch(item.Role) {
      case "geometry_dgk":Require(item.Name==stem+".dgk"&&item.Required,"Exactly named mandatory primary DGK required.");break;
      case "triangles_dmt":Require(item.Name==stem+".dmt"||item.Name==stem+".dgk.dmt","DMT companion basename differs from DGK.");break;
      // Documentation does not specify feature companion basenames: they are
      // explicit caller declarations, never guessed from the primary DGK name.
      case "features_mac":Require(item.Name.EndsWith(".mac",StringComparison.Ordinal),"Declared feature MAC extension required.");break;
      case "features_xml":Require(item.Name.EndsWith(".xml",StringComparison.Ordinal),"Declared feature XML extension required.");break;
      default:throw new InvalidDataException("Unsupported DGK artifact role.");
     }
    }
    Require(detached.Count(x=>x.Role=="geometry_dgk")==1,"Exactly one primary DGK required.");
    Require(detached.Count(x=>x.Role=="features_mac")<=1&&detached.Count(x=>x.Role=="features_xml")<=1,"At most one declared feature MAC and XML supported.");
    Stem=stem;Artifacts=Array.AsReadOnly(detached);
   }
  }
  public sealed class ObservedArtifact {
   public readonly string Name,Role,Sha256,ContentQualification,LengthUnits;
   public readonly long Length;
   public readonly bool ExpectedHashMatched;
   public readonly int VertexCount,TriangleCount;
   internal ObservedArtifact(Artifact item,long length,string sha,int vertices,int triangles,string units) {
    Name=item.Name;Role=item.Role;Length=length;Sha256=sha;ExpectedHashMatched=item.ExpectedSha256.Length!=0;
    VertexCount=vertices;TriangleCount=triangles;LengthUnits=units;
    ContentQualification=item.Role=="triangles_dmt"?"parsed_mesh_only_cad_equivalence_unqualified":"opaque_bytes_content_unqualified";
   }
  }
  public sealed class Report {
   public readonly ReadOnlyCollection<ObservedArtifact> Artifacts;
   public readonly ReadOnlyCollection<string> AbsentOptionalArtifacts;
   public readonly long TotalBytes;
   public bool CadGeometryVerified {get{return false;}}
   public bool ExportSettingsQualified {get{return false;}}
   public bool CompanionExecutionAttempted {get{return false;}}
   internal Report(List<ObservedArtifact> artifacts,List<string> absent,long total) {
    Artifacts=artifacts.AsReadOnly();AbsentOptionalArtifacts=absent.AsReadOnly();TotalBytes=total;
   }
  }
  static void Require(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
  static bool Hex(string value){return value.Length==64&&value.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');}
  static void SafeName(string value) {
   Require(!string.IsNullOrEmpty(value)&&value.Length<=120,"Bounded artifact basename required.");
   Require(value.All(c=>c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||c=='_'||c=='-'||c=='.'),"Plain ASCII artifact basename required; paths and commands forbidden.");
   Require(!value.StartsWith(".",StringComparison.Ordinal)&&!value.EndsWith(".",StringComparison.Ordinal)&&!value.Contains(".."),"Ambiguous artifact basename.");
   string device=value.Split('.')[0].ToUpperInvariant();
   Require(!new[]{"CON","PRN","AUX","NUL","CLOCK$"}.Contains(device)&&
    !(device.Length==4&&(device.StartsWith("COM",StringComparison.Ordinal)||device.StartsWith("LPT",StringComparison.Ordinal))&&device[3]>='0'&&device[3]<='9'),"Windows device name forbidden.");
  }
  static string Digest(Stream stream,long length) {
   stream.Position=0;long count=0;var buffer=new byte[65536];
   using(var hash=SHA256.Create()) {
    int got;while((got=stream.Read(buffer,0,buffer.Length))>0) {
     count+=got;Require(count<=length,"Artifact grew while hashing.");hash.TransformBlock(buffer,0,got,buffer,0);
    }
    Require(count==length&&stream.Length==length,"Artifact length changed or stream was truncated.");
    hash.TransformFinalBlock(new byte[0],0,0);var digest=hash.Hash;
    if(digest==null)throw new InvalidDataException("SHA256 unavailable.");
    return BitConverter.ToString(digest).Replace("-","").ToLowerInvariant();
   }
  }
  public static Report Verify(Plan plan,IEnumerable<KeyValuePair<string,Stream>> openedFiles) {
   if(plan==null)throw new ArgumentNullException("plan");if(openedFiles==null)throw new ArgumentNullException("openedFiles");
   var inputs=openedFiles.Take(MaxArtifacts+1).ToArray();Require(inputs.Length<=MaxArtifacts,"Unexpected artifact count.");
   var files=new Dictionary<string,Stream>(StringComparer.OrdinalIgnoreCase);var streams=new HashSet<Stream>();
   foreach(var input in inputs) {
    SafeName(input.Key);Require(!files.ContainsKey(input.Key),"Duplicate observed basename.");
    Require(plan.Artifacts.Any(x=>x.Name==input.Key),"Observed artifact was not explicitly declared with exact spelling.");
    if(input.Value==null)throw new InvalidDataException("Artifact stream missing.");
    Require(input.Value.CanRead&&input.Value.CanSeek&&streams.Add(input.Value),"Distinct readable seekable artifact streams required.");
    files.Add(input.Key,input.Value);
   }
   long total=0;var observed=new List<ObservedArtifact>();var absent=new List<string>();
   foreach(var item in plan.Artifacts) {
    if(!files.ContainsKey(item.Name)) {
     Require(!item.Required,"Required DGK bundle artifact missing: "+item.Name);absent.Add(item.Name);continue;
    }
    Stream stream=files[item.Name];long length=stream.Length;Require(length>0&&length<=MaxFileBytes,"Artifact length outside limit.");
    total+=length;Require(total<=MaxBundleBytes,"DGK bundle exceeds byte limit.");
    string sha=Digest(stream,length);Require(item.ExpectedSha256.Length==0||item.ExpectedSha256==sha,"DGK bundle expected SHA256 differs.");
    int vertices=-1,triangles=-1;string units="unknown";
    if(item.Role=="triangles_dmt") {
     stream.Position=0;var mesh=DmtMeshReader.Read(stream);vertices=mesh.VertexCount;triangles=mesh.TriangleCount;units=mesh.LengthUnits;
    }
    Require(Digest(stream,length)==sha,"Artifact bytes changed during verification.");
    observed.Add(new ObservedArtifact(item,length,sha,vertices,triangles,units));
   }
   // Recheck the entire retained set after all companions, so changes to an
   // earlier artifact while another one is read cannot produce a mixed receipt.
   foreach(var item in observed)Require(Digest(files[item.Name],item.Length)==item.Sha256,"Bundle changed across companion verification.");
   return new Report(observed,absent,total);
  }
 }
}

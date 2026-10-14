using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 internal static class DgkBundleFilesTests {
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
  static extern bool CreateHardLink(string name,string existing,IntPtr security);
  static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
  internal static void Run() {
   string root=Path.Combine(Program.Root,"dgk-bundle-file-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   var cases=new List<Dictionary<string,object>>();
   foreach(string kind in new[]{"primary","features","optional_absent","missing_primary","missing_companion","stray_file","nested_directory","hash_mismatch","empty","hardlink","held_writer","outside_root"}) {
    string folder=Path.Combine(root,kind);Directory.CreateDirectory(folder);string primary=Path.Combine(folder,"sample.dgk");
    File.WriteAllBytes(primary,new byte[]{1,2,3});var specs=new List<DgkBundleVerifier.Artifact>{new DgkBundleVerifier.Artifact("sample.dgk","geometry_dgk",true)};
    if(kind=="features"){File.WriteAllText(Path.Combine(folder,"holes.mac"),"NEVER EXECUTE THIS TEST FILE");File.WriteAllText(Path.Combine(folder,"holes.xml"),"<!DOCTYPE x SYSTEM 'file:///test'>");specs.Add(new DgkBundleVerifier.Artifact("holes.mac","features_mac",true));specs.Add(new DgkBundleVerifier.Artifact("holes.xml","features_xml",true));}
    if(kind=="optional_absent"||kind=="missing_companion")specs.Add(new DgkBundleVerifier.Artifact("sample.dmt","triangles_dmt",kind=="missing_companion"));
    if(kind=="missing_primary")File.Delete(primary);
    if(kind=="stray_file")File.WriteAllText(Path.Combine(folder,"other.xml"),"unexpected");
    if(kind=="nested_directory")Directory.CreateDirectory(Path.Combine(folder,"nested"));
    if(kind=="empty")File.WriteAllBytes(primary,new byte[0]);
    if(kind=="hash_mismatch")specs[0]=new DgkBundleVerifier.Artifact("sample.dgk","geometry_dgk",true,new string('a',64));
    if(kind=="hardlink")Require(CreateHardLink(Path.Combine(root,"outside-alias.dgk"),primary,IntPtr.Zero),"Test hardlink setup failed.");
    FileStream writer=kind=="held_writer"?new FileStream(primary,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite):null;
    bool accepted=false;string error=null;Dictionary<string,object> result=null;
    try{result=DgkBundleFiles.Verify(kind=="outside_root"?Path.Combine(root,"other"):root,folder,new DgkBundleVerifier.Plan("sample",specs));accepted=true;}
    catch(Exception failure){if(!(failure is IOException)&&!(failure is InvalidDataException))throw;error=failure.Message;}
    finally{if(writer!=null)writer.Dispose();}
    bool expected=kind=="primary"||kind=="features"||kind=="optional_absent";
    Require(accepted==expected,"DGK retained file matrix differs: "+kind);
    if(accepted)Require(Equals(result["cadGeometryVerified"],false)&&Equals(result["cadConnectionAttempted"],false)&&Equals(result["companionExecutionAttempted"],false),"Byte witness claimed CAD/execution qualification.");
    cases.Add(new Dictionary<string,object>{{"kind",kind},{"accepted",accepted},{"error",error},{"result",result}});
   }
   string output=Path.Combine(root,"matrix.json");File.WriteAllText(output,Program.Json.Serialize(new Dictionary<string,object>{{"state","passed"},{"cases",cases},{"cadConnectionAttempted",false}}));
   Console.WriteLine("DGK retained file inspection: "+cases.Count+" cases passed; "+output);
  }
 }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class RotationMeasurementRecordTests {
  static void Require(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
  internal static int Run() {
   Require(File.Exists(Path.Combine(Program.Root,".rotation-measurement-record-fixture")),"Explicit own record fixture marker required; no CAD.");var rows=new List<Dictionary<string,object>>();
   foreach(string fault in new[]{"control","extra_record_field","wrong_schema","wrong_phase","wrong_index","wrong_provenance","wrong_descriptor","extra_sample_field","bool_numeric","duplicate_anchor","bad_vector","nonfinite_text","wrong_sha","foreign_execution","rewrite","held_tamper"}) {
    var f=new OwnedRotationPlanTests.Fixture();f.Freeze();string root=f.Base.Root,packetId=Guid.NewGuid().ToString("N");var packet=OwnedRotationPlans.Prepare(root,packetId,f.Args,f.Base.NewContext());Require(Equals(packet["state"],"owned_rotation_prepared_observed"),"Own packet failed.");
    Dictionary<string,object> provenance;using(var proofs=new OwnedOperationPlans.Proofs()){var d=new OwnedRotationPlans.Dependencies(root,f.Args,proofs);provenance=RotationMeasurementRecords.Provenance((Dictionary<string,object>)packet["ownedRotation"],d);}
    var context=new OwnedRotationExecutionTests.Fake(f,"control");var sample=context.Measurements(40);string id=Guid.NewGuid().ToString("N");string sha=RotationMeasurementRecords.Freeze(root,id,0,"before",provenance,sample),path=RotationMeasurementRecords.PathFor(root,id,0,"before");
    if(!new[]{"control","wrong_sha","foreign_execution","rewrite","held_tamper"}.Contains(fault)) {
     var row=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));var data=(Dictionary<string,object>)row["samples"];var witnesses=(IList)data["positions"];var point=(Dictionary<string,object>)witnesses[0];
     if(fault=="extra_record_field")row["command"]="NeverExecute";
     if(fault=="wrong_schema")row["schemaVersion"]=2;
     if(fault=="wrong_phase")row["phase"]="after";
     if(fault=="wrong_index")row["index"]=1;
     if(fault=="wrong_provenance")((Dictionary<string,object>)row["provenance"])["copyId"]=new string('f',32);
     if(fault=="wrong_descriptor")data["descriptorSha256"]="bad";
     if(fault=="extra_sample_field")data["rotate"]=true;
     if(fault=="bool_numeric")point["value"]=new object[]{true,0,0};
     if(fault=="duplicate_anchor")((Dictionary<string,object>)witnesses[1])["key"]=point["key"];
     if(fault=="bad_vector")point["value"]=new[]{0,0};
     if(fault=="nonfinite_text")point["value"]=new object[]{"NaN",0,0};
     // Deliberately malformed own test record; no original source/plan/owner is altered.
     File.WriteAllText(path,Program.Json.Serialize(row));sha=Program.Hash(path);
    }
    var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Program.Hash(p));bool accepted=false;string error=null;
    using(var proofs=new OwnedOperationPlans.Proofs())try {
     var decoded=RotationMeasurementRecords.Read(root,fault=="foreign_execution"?Guid.NewGuid().ToString("N"):id,0,"before",fault=="wrong_sha"?new string('f',64):sha,provenance,proofs);
     if(fault=="rewrite")RotationMeasurementRecords.Freeze(root,id,0,"before",provenance,sample);
     if(fault=="held_tamper")File.AppendAllText(path," ");
     Require(EntitySets.Canonical(RotationMeasurementRecords.Encode(decoded))==EntitySets.Canonical(RotationMeasurementRecords.Encode(sample)),"Round-trip samples changed.");accepted=true;
    }catch(Exception failure){error=failure.Message;}
    Require(accepted==(fault=="control"),fault+": wrong record decision.");Require(before.All(pair=>Program.Hash(pair.Key)==pair.Value),"Read/rewrite rejection changed artifacts.");
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",root},{"executionId",id},{"recordSha256",sha},{"accepted",accepted},{"error",error},{"artifactHashes",before},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","rotation-measurement-record-tests.json"),new Dictionary<string,object>{{"state","passed"},{"rows",rows},{"cadConnectionAttempted",false}});Console.WriteLine("PASS: "+rows.Count+" immutable measurement record cases; no CAD.");return rows.Count;
  }
 }
}

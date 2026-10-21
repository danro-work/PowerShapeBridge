using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedSolidScaleTests {
  static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  sealed class Context : OwnedSolidScale.IExecutionContext {
   internal readonly SolidScaleMembershipMeasurementTests.Context Reader=new SolidScaleMembershipMeasurementTests.Context();internal int FailPhase=-1,Calls;internal bool Selected;
   public Dictionary<string,object> Observation(){return Reader.Observation();}public Dictionary<string,object> Windows(){return Reader.Windows();}public void ConfirmDocument(){Reader.ConfirmDocument();}public int Composite(int type,int id){return Reader.Composite(type,id);}public double[] Bounds(int composite){return Reader.Bounds(composite);}public object ClosestSolidFace(int id,double[] point){return Reader.ClosestSolidFace(id,point);}public object FaceNear(string face,double[] point){return Reader.FaceNear(face,point);}public object FacePoint(string face,double t,double u,bool normal){return Reader.FacePoint(face,t,u,normal);}public void Dispose(){}
   public void ScalePhaseGuard(Dictionary<string,object> expected,string name,int phase,int count){Need(Selected==(phase>0)&&count==1&&name==(string)EntitySets.Inventory(Reader.Snapshot)[0]["name"],"Synthetic scale selection context changed.");}
   public void ScalePhase(SolidUniformScaleProgram program,int phase){string command=program.Take(phase);Calls++;if(FailPhase==phase)throw new IOException("Synthetic unknown phase "+phase);if(phase==0)Selected=true;if(phase==10)Reader.ApplyScale(0.5);if(phase==12)Selected=false;Reader.Snapshot["contextBefore"]=Reader.Snapshot["contextAfter"]=new[]{Selected?1:0,0};Reader.Snapshot["selectionBefore"]=Reader.Snapshot["selectionAfter"]=Selected?new object[]{new Dictionary<string,object>{{"type","Solid"},{"name","Synthetic_40"}}}:new object[0];Need(!string.IsNullOrEmpty(command),"Missing frozen phase.");}
  }
  sealed class Fixture {
   internal readonly Context C=new Context();internal readonly string Root,Id;internal readonly Dictionary<string,object> Prepared,References;
   internal Fixture(){
    string working=(string)((Dictionary<string,object>)C.Reader.Snapshot["model"])["path"],ownDir=Path.GetDirectoryName(working);Root=Directory.GetParent(Directory.GetParent(ownDir).FullName).FullName;string copy=Path.GetFileName(ownDir);string owner=Program.Hash(Path.Combine(ownDir,"ownership.json"));
    string inspection=Guid.NewGuid().ToString("N"),inspectionSha=NativeJournal.Freeze(Path.Combine(Root,"responses",inspection+".json"),C.Observation());string expiry=DateTime.UtcNow.AddSeconds(250).ToString("o");string bindingId=Guid.NewGuid().ToString("N");
    var binding=DocumentBindings.Prepare(Root,bindingId,new Dictionary<string,object>{{"copyId",copy},{"ownershipSha256",owner},{"inspectionRequestId",inspection},{"inspectionSha256",inspectionSha},{"expiresUtc",expiry}},C.Observation(),C,new Dictionary<string,object>());
    string setId=Guid.NewGuid().ToString("N");var set=new Dictionary<string,object>{{"inspectionRequestId",inspection},{"inspectionSha256",inspectionSha},{"expiresUtc",expiry},{"entityKeys",new[]{new[]{40,5}}}};string setSha=NativeJournal.Freeze(Path.Combine(Root,"entitysets",setId+".json"),set);
    var args=new Dictionary<string,object>{{"entitySetId",setId},{"entitySetSha256",setSha},{"bindingId",bindingId},{"bindingSha256",binding["bindingSha256"]},{"parameters",new Dictionary<string,object>{{"factor",0.5},{"centre",new[]{2,3,4}},{"coordinateFrame","world"},{"lengthUnits","document_raw"},{"mode","scale_in_place"}}}};
    Id=Guid.NewGuid().ToString("N");Prepared=OwnedSolidScale.Prepare(Root,Id,args,C);Need(Equals(Prepared["state"],"owned_uniform_scale_prepared_observed")&&Prepared.ContainsKey("membershipBundleSha256"),"Qualified fake preparation failed: "+Program.Json.Serialize(Prepared));
    string sha=NativeJournal.Freeze(Path.Combine(Root,"responses",Id+".json"),Prepared);References=new Dictionary<string,object>{{"ownedScaleId",Id},{"ownedScaleSha256",Prepared["ownedScaleSha256"]},{"preparationReceiptSha256",sha},{"membershipBundleSha256",Prepared["membershipBundleSha256"]}};
   }
   internal string Fence {get{return OwnedTranslation.FencePath(Root,(string)Prepared["copyId"]);}}
  }
  internal static int Run(){
   Need(File.Exists(Path.Combine(Program.Root,".owned-solid-scale-fixture")),"Explicit isolated fake scale fixture marker required; never connect CAD.");int checks=0;Action<bool> check=v=>{Need(v,"Owned SOLID scale guard check failed: "+checks);checks++;};
   foreach(int failed in Enumerable.Range(0,13)){
    var f=new Fixture();f.C.FailPhase=failed;var r=OwnedSolidScale.Execute(f.Root,Guid.NewGuid().ToString("N"),f.References,f.C);check(Equals(r["state"],"owned_solid_scale_outcome_unknown")&&Equals(r["commandCallsAttempted"],failed+1)&&f.C.Calls==failed+1&&File.Exists(f.Fence));
   }
   var ok=new Fixture();string execution=Guid.NewGuid().ToString("N");var result=OwnedSolidScale.Execute(ok.Root,execution,ok.References,ok.C);check(Equals(result["state"],"owned_solid_scale_executed_unverified")&&Equals(result["commandCallsAttempted"],13)&&!ok.C.Selected&&ok.C.Calls==13&&File.Exists(ok.Fence));
   string receiptSha=NativeJournal.Freeze(Path.Combine(ok.Root,"responses",execution+".json"),result);var journal=NativeJournal.ForVerification(ok.Root,execution,(string)result["planSha256"]);var after=ok.C.Observation();after["timestampUtc"]=journal.CommandReturnedUtc.AddMilliseconds(1).ToString("o");string inspectId=Guid.NewGuid().ToString("N"),inspectSha=NativeJournal.Freeze(Path.Combine(ok.Root,"responses",inspectId+".json"),after);var listing=ok.C.Windows();listing["state"]="documents_listed_verified";listing["timestampUtc"]=after["timestampUtc"];string listId=Guid.NewGuid().ToString("N"),listSha=NativeJournal.Freeze(Path.Combine(ok.Root,"responses",listId+".json"),listing);
   var args=new Dictionary<string,object>{{"executionRequestId",execution},{"executionReceiptSha256",receiptSha},{"inspectionRequestId",inspectId},{"inspectionSha256",inspectSha},{"listingRequestId",listId},{"listingSha256",listSha},{"windowNumber",1},{"modelId",9},{"expiresUtc",DateTime.UtcNow.AddSeconds(60).ToString("o")}};var verified=OwnedSolidScale.Verify(ok.Root,Guid.NewGuid().ToString("N"),args,ok.C);
   check(Equals(verified["state"],"owned_solid_scale_samples_match_unqualified")&&Equals(verified["samplePostconditionsPassed"],true)&&Equals(verified["copyFenceReleased"],false)&&File.Exists(ok.Fence)&&NativeJournal.ForVerification(ok.Root,execution,(string)result["planSha256"]).State=="executed_unverified");
   int before=ok.C.Calls;var replay=OwnedSolidScale.Execute(ok.Root,Guid.NewGuid().ToString("N"),ok.References,ok.C);check(Equals(replay["state"],"owned_solid_scale_rejected_before_change")&&Equals(replay["commandCallsAttempted"],0)&&ok.C.Calls==before);
   var bad=Clone(args);bad["executionReceiptSha256"]=new string('a',64);var rejected=OwnedSolidScale.Verify(ok.Root,Guid.NewGuid().ToString("N"),bad,ok.C);check(Equals(rejected["state"],"owned_solid_scale_verification_rejected")&&File.Exists(ok.Fence));
   var previous=System.Globalization.CultureInfo.CurrentCulture;try{System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("ru-RU");var p=new SolidUniformScaleProgram("1",0.5,new[]{2.5,3.5,4.5});check(p.Commands[2]=="NOKEEP"&&p.Commands[8]=="2.5 3.5 4.5"&&p.Commands[9]=="VALUE 0.5");p.Take(0);bool refused=false;try{p.Take(0);}catch(InvalidOperationException){refused=true;}check(refused);p.Abort();refused=false;try{p.Take(1);}catch(InvalidOperationException){refused=true;}check(refused);}finally{System.Globalization.CultureInfo.CurrentCulture=previous;}
   Console.WriteLine("PASS: "+checks+" owned SOLID scale once/phase-stop/independent-fake-verifier checks; no CAD.");return checks;
  }
 }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Synthetic retained contexts and opaque saved files; no COM or CAD macro.
 internal static class OwnedSavedReopenExecutionTests {
  internal static Dictionary<string,object> Clone(Dictionary<string,object> data){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(data));}
  internal sealed class Context : WorkingCopyOpen.IContext,DocumentBindings.IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal int Commands,Reads;internal bool Throw,ChangedContent,ChangedUnits;
   internal Action<int> DuringRead;internal Action<string> DuringOpen;
   public Dictionary<string,object> Windows(){return Clone(Listing);}
   public Dictionary<string,object> Observation(){Reads++;if(DuringRead!=null)DuringRead(Reads);return Clone(Snapshot);}
   public void ConfirmDocument(){}
   public void Dispose(){}
   public void Open(string path) {
    Commands++;if(DuringOpen!=null)DuringOpen(path);if(Throw)throw new IOException("Synthetic lost OPEN return.");
    var previous=WorkingCopyOpen.Windows(Listing);foreach(var window in previous)window["selectedRaw"]=0;
    var model=(Dictionary<string,object>)Snapshot["model"];model["id"]=500;model["path"]=path;model["name"]=Path.GetFileNameWithoutExtension(path);
    foreach(var entity in EntitySets.Inventory(Snapshot))entity["nativeId"]=(int)entity["nativeId"]+1000;
    var added=new Dictionary<string,object>{{"windowNumber",2},{"nativeWindowId",90},{"type","Model"},{"selectedRaw",1},{"modelId",model["id"]},{"modelName",model["name"]},{"modelPath",path}};
    Listing["windows"]=previous.Concat(new[]{added}).ToArray();Listing["activeWindow"]=added;Listing["windowCount"]=previous.Length+1;
    if(ChangedContent)((Dictionary<string,object>)EntitySets.Inventory(Snapshot)[0]["geometry"])["volumeRaw"]=999;
    if(ChangedUnits)Snapshot["lengthUnitFactorRaw"]=2;
   }
  }
  internal sealed class Case {
   internal readonly OwnedSavedReopenTests.Fixture Saved=new OwnedSavedReopenTests.Fixture();
   internal readonly Dictionary<string,object> Prepared,Args;internal readonly Context Context;
   internal string Root {get{return Saved.Save.Fixture.Root;}}
   internal Case() {
    File.Copy(Path.Combine(Program.Root,"bridge.config.json"),Path.Combine(Root,"bridge.config.json"));
    Prepared=OwnedSavedReopen.Prepare(Root,Guid.NewGuid().ToString("N"),Saved.Args);
    var evidence=(IList)Saved.Verified["evidence"];var snapshotEntry=(Dictionary<string,object>)evidence[1];var listingEntry=(Dictionary<string,object>)evidence[2];
    Context=new Context{Snapshot=Clone(Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Root,"responses",(string)snapshotEntry["requestId"]+".json")))),Listing=Clone(Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Root,"responses",(string)listingEntry["requestId"]+".json"))))};
    var preparedPlan=(Dictionary<string,object>)Prepared["reopen"];DateTime sourceExpiry=EntitySets.Expiry(preparedPlan["expiresUtc"]),freshExpiry=DateTime.UtcNow.AddSeconds(100);
    Args=new Dictionary<string,object>{{"reopenId",Prepared["reopenId"]},{"reopenSha256",Prepared["reopenSha256"]},{"modelId",9},{"windowNumber",1},{"expiresUtc",(sourceExpiry<freshExpiry?sourceExpiry:freshExpiry).ToString("o")}};
    Reads(Args,"inspectionRequestId","inspectionSha256","listingRequestId","listingSha256");
   }
   internal void Reads(Dictionary<string,object> args,string inspectionId,string inspectionSha,string listingId,string listingSha) {
    // Framework UtcNow can return the same coarse clock tick as the preceding
    // synthetic command return on fast Windows runners. Put fake reads in the
    // next observed tick; production receipt freshness remains strictly checked.
    string stamp=DateTime.UtcNow.AddMilliseconds(20).ToString("o");
    string snapshotId=Guid.NewGuid().ToString("N"),windowsId=Guid.NewGuid().ToString("N");var snapshot=Context.Observation();snapshot["timestampUtc"]=stamp;snapshot["bridgeRequestId"]=snapshotId;
    var listing=Context.Windows();listing["timestampUtc"]=stamp;listing["bridgeRequestId"]=windowsId;
    args[inspectionId]=snapshotId;args[inspectionSha]=NativeJournal.Freeze(Path.Combine(Root,"responses",snapshotId+".json"),snapshot);
    args[listingId]=windowsId;args[listingSha]=NativeJournal.Freeze(Path.Combine(Root,"responses",windowsId+".json"),listing);
   }
   internal Dictionary<string,object> Execute(string id=null) {
    id=id??Guid.NewGuid().ToString("N");var result=OwnedSavedReopenExecution.Execute(Root,id,Args,Context);result["bridgeRequestId"]=id;
    NativeJournal.Freeze(Path.Combine(Root,"responses",id+".json"),result);return result;
   }
   internal Dictionary<string,object> Proof(Dictionary<string,object> receipt) {
    var proof=new Dictionary<string,object>{{"reopenRequestId",receipt["operationId"]},{"reopenReceiptSha256",Program.Hash(Path.Combine(Root,"responses",(string)receipt["operationId"]+".json"))},{"modelId",500},{"windowNumber",2},{"expiresUtc",DateTime.UtcNow.AddSeconds(100).ToString("o")}};
    Reads(proof,"inspectionRequestId","inspectionSha256","listingRequestId","listingSha256");return proof;
   }
  }
  internal static int Run() {
   int count=0;Action<bool> check=ok=>{if(!ok)throw new Exception("Saved reopen executor self-test failed at "+count);count++;};
   var good=new Case();var executed=good.Execute();if(!Equals(executed["state"],"owned_saved_reopen_executed_unverified"))throw new Exception("Saved reopen first execution: "+Program.Json.Serialize(executed));check(good.Context.Commands==1&&Equals(executed["observableContentChecked"],true));
   var proof=good.Proof(executed);string verifyId=Guid.NewGuid().ToString("N");var verified=OwnedSavedReopenVerification.Verify(good.Root,verifyId,proof,good.Context);
   if(!Equals(verified["state"],"owned_saved_reopen_verified_observed"))throw new Exception("Saved reopen first verification: "+Program.Json.Serialize(verified));
   check(Equals(verified["state"],"owned_saved_reopen_verified_observed")&&good.Context.Commands==1&&Equals(verified["noCommandReplay"],true)&&good.Saved.Save.Protected());
   var binding=DocumentBindings.Load(good.Root,new Dictionary<string,object>{{"bindingId",verified["bindingId"]},{"bindingSha256",verified["bindingSha256"]}});
   check(Equals(binding["inspectionRequestId"],proof["inspectionRequestId"])&&Equals(binding["copyId"],good.Prepared["reopenId"])&&Equals(((Dictionary<string,object>)verified["model"])["id"],500));
   string terminal=Path.Combine(good.Root,"operations",(string)executed["operationId"],"000005.json"),terminalSha=Program.Hash(terminal);
   var again=OwnedSavedReopenVerification.Verify(good.Root,Guid.NewGuid().ToString("N"),good.Proof(executed),good.Context);
   check(Equals(again["state"],"owned_saved_reopen_verified_observed")&&Program.Hash(terminal)==terminalSha&&good.Context.Commands==1);
   string verifiedSha=NativeJournal.Freeze(Path.Combine(good.Root,"responses",verifyId+".json"),new Dictionary<string,object>(verified){{"bridgeRequestId",verifyId}});
   NativeJournal.Freeze(Path.Combine(good.Root,"saved-reopen-fixture.json"),new Dictionary<string,object>{{"verificationRequestId",verifyId},{"verificationReceiptSha256",verifiedSha},{"parameters",proof}});
   var replay=new Context{Snapshot=Clone(good.Saved.Save.Fixture.Snapshot),Listing=good.Context.Windows()};check(Equals(OwnedSavedReopenExecution.Execute(good.Root,Guid.NewGuid().ToString("N"),good.Args,replay)["state"],"owned_saved_reopen_rejected_before_change")&&replay.Commands==0);
   foreach(string fault in new[]{"seed_changed","result_changed","directory_replaced","wrong_source","wrong_sha","expired","context_changed","selection","late_context"}) {
    var test=new Case();var plan=(Dictionary<string,object>)test.Prepared["reopen"];var seed=(Dictionary<string,object>)plan["seed"];
    if(fault=="seed_changed"){File.SetAttributes((string)seed["path"],FileAttributes.Normal);File.AppendAllText((string)seed["path"],"changed");}
    if(fault=="result_changed")File.AppendAllText(test.Saved.Save.ResultPath,"changed");
    if(fault=="directory_replaced"){string dir=Path.GetDirectoryName((string)seed["path"]);Directory.Move(dir,dir+".old");Directory.CreateDirectory(dir);foreach(string path in Directory.GetFiles(dir+".old"))File.Copy(path,Path.Combine(dir,Path.GetFileName(path)));}
    if(fault=="wrong_source")test.Args["modelId"]=999;
    if(fault=="wrong_sha")test.Args["reopenSha256"]=new string('f',64);
    if(fault=="expired")test.Args["expiresUtc"]=DateTime.UtcNow.AddSeconds(-1).ToString("o");
    if(fault=="context_changed")test.Context.Snapshot["processId"]=99;
    if(fault=="selection")test.Context.Snapshot["selectionBefore"]=new[]{5};
    if(fault=="late_context")test.Context.DuringRead=n=>{if(n==4)test.Context.Snapshot["processId"]=99;};
    var refusal=test.Execute();check(Equals(refusal["state"],"owned_saved_reopen_rejected_before_change")&&test.Context.Commands==0&&test.Saved.Save.Protected());
   }
   foreach(string fault in new[]{"throw","wrong_content","wrong_units","late_file"}) {
    var test=new Case();test.Context.Throw=fault=="throw";test.Context.ChangedContent=fault=="wrong_content";test.Context.ChangedUnits=fault=="wrong_units";
    if(fault=="late_file")test.Context.DuringOpen=path=>File.AppendAllText(path,"changed during open");
    var unknown=test.Execute();check(Equals(unknown["state"],"owned_saved_reopen_outcome_unknown")&&test.Context.Commands==1&&test.Saved.Save.Protected());
    var refused=OwnedSavedReopenExecution.Execute(test.Root,Guid.NewGuid().ToString("N"),test.Args,test.Context);check(Equals(refused["state"],"owned_saved_reopen_rejected_before_change")&&test.Context.Commands==1);
    var rejected=OwnedSavedReopenVerification.Verify(test.Root,Guid.NewGuid().ToString("N"),test.Proof(unknown),test.Context);check(Equals(rejected["state"],"owned_saved_reopen_verification_rejected")&&test.Context.Commands==1);
   }
   foreach(string fault in new[]{"stale_read","changed_result","wrong_units","wrong_choice","partial_journal","forged_return"}) {
    var test=new Case();var original=test.Execute();var input=test.Proof(original);
    if(fault=="stale_read"){input["inspectionRequestId"]=test.Args["inspectionRequestId"];input["inspectionSha256"]=test.Args["inspectionSha256"];}
    if(fault=="changed_result")File.AppendAllText(test.Saved.Save.ResultPath,"changed saved bytes");
    if(fault=="wrong_units")test.Context.Snapshot["lengthUnitFactorRaw"]=2;
    if(fault=="wrong_choice")input["modelId"]=9;
    if(fault=="partial_journal")File.WriteAllText(Path.Combine(test.Root,"operations",(string)original["operationId"],"writer.lock"),"synthetic interrupted verifier");
    if(fault=="forged_return"){string returned=Path.Combine(test.Root,"operations",(string)original["operationId"],"000003.json");File.AppendAllText(returned," ");}
    var rejected=OwnedSavedReopenVerification.Verify(test.Root,Guid.NewGuid().ToString("N"),input,test.Context);
    check(Equals(rejected["state"],"owned_saved_reopen_verification_rejected")&&test.Context.Commands==1&&!File.Exists(Path.Combine(test.Root,"operations",(string)original["operationId"],"000005.json")));
   }
   // New entity IDs/order are acceptable, altered measurements/metadata are not.
   var before=Clone(good.Saved.Save.Fixture.Snapshot);var after=Clone(before);after["entities"]=EntitySets.Inventory(after).Reverse().ToArray();foreach(var entity in EntitySets.Inventory(after))entity["nativeId"]=(int)entity["nativeId"]+500;
   SavedReopenObservation.Check(before,after);count++;
   var geometry=(Dictionary<string,object>)EntitySets.Inventory(after).Single(e=>(int)e["nativeTypeCode"]==40)["geometry"];geometry["volumeRaw"]=0.8+1e-10;SavedReopenObservation.Check(before,after);count++;
   geometry["volumeRaw"]=0.9;bool different=false;try{SavedReopenObservation.Check(before,after);}catch(InvalidOperationException){different=true;}check(different);
   return count;
  }
 }
}

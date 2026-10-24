using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace PowerShapeBridge {
 internal static class OwnedSelectionTests {
  static Dictionary<string,object> Clone(object o){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(o));}
  static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
  sealed class Context:OwnedSelection.IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal int Calls,SelectedReads;internal bool ThrowAfterChange,NullSelected,GeometryDrift;internal int Count;
   public Dictionary<string,object> Windows(){return Clone(Listing);}
   public Dictionary<string,object> Observation(){return Clone(Snapshot);}
   public void ConfirmDocument(){}
   public void Dispose(){}
   public int SelectionCount(){return Count;}
   public int Composite(int type,int id){Require(type==40&&id==5,"Wrong composite tuple");return 800000005;}
   public double[] Bounds(int composite){return new double[]{0,0,0,10,10,10};}
   public object ModelItems(){return new[]{800000005};}
   public object SelectedItems(){SelectedReads++;return NullSelected?null:(object)(Count==0?new int[0]:new[]{800000005});}
   public object ItemData(object o){return new object[]{new[]{40},new[]{5},new[]{"Synthetic_40"},new[]{0}};}
   public void ChangeSelection(OwnedSelectionProgram p){string command=p.Take();Calls++;Count=command=="SELECT CLEARLIST"?0:1;Snapshot["contextBefore"]=Snapshot["contextAfter"]=new[]{Count,0};Snapshot["selectionBefore"]=Snapshot["selectionAfter"]=Count==0?(object)new object[0]:new object[]{"Synthetic_40"};if(GeometryDrift)((Dictionary<string,object>)EntitySets.Inventory(Snapshot)[0]["geometry"])["surfaces"]=7;if(ThrowAfterChange)throw new IOException("Unknown callback result, no replay.");}
  }
  static Dictionary<string,object> Binding(OwnedOperationPlanTests.Fixture f,Context c){
   var snapshot=c.Snapshot;snapshot["timestampUtc"]=DateTime.UtcNow.ToString("o");string inspect=Guid.NewGuid().ToString("N");snapshot["bridgeRequestId"]=inspect;string sha=NativeJournal.Freeze(Path.Combine(f.Root,"responses",inspect+".json"),snapshot);
   var old=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(f.Root,"documentbindings",f.BindingId+".json")));string id=Guid.NewGuid().ToString("N");var r=DocumentBindings.Prepare(f.Root,id,new Dictionary<string,object>{{"copyId",old["copyId"]},{"ownershipSha256",old["ownershipSha256"]},{"inspectionRequestId",inspect},{"inspectionSha256",sha},{"expiresUtc",DateTime.UtcNow.AddSeconds(180).ToString("o")}},snapshot,c,new Dictionary<string,object>());return new Dictionary<string,object>{{"bindingId",id},{"bindingSha256",r["bindingSha256"]}};
  }
  static Dictionary<string,object> Verification(OwnedOperationPlanTests.Fixture f,Context c,string execution,string sha){
   string inspect=Guid.NewGuid().ToString("N"),listing=Guid.NewGuid().ToString("N");var o=c.Observation();o["timestampUtc"]=DateTime.UtcNow.ToString("o");o["bridgeRequestId"]=inspect;var w=c.Windows();w["state"]="documents_listed_verified";w["timestampUtc"]=DateTime.UtcNow.ToString("o");string a=NativeJournal.Freeze(Path.Combine(f.Root,"responses",inspect+".json"),o),b=NativeJournal.Freeze(Path.Combine(f.Root,"responses",listing+".json"),w);
   return new Dictionary<string,object>{{"executionRequestId",execution},{"executionReceiptSha256",sha},{"inspectionRequestId",inspect},{"inspectionSha256",a},{"listingRequestId",listing},{"listingSha256",b},{"windowNumber",1},{"modelId",9},{"expiresUtc",DateTime.UtcNow.AddSeconds(150).ToString("o")}};
  }
  internal static int Run(){
   Require(File.Exists(Path.Combine(Program.Root,".owned-selection-fixture")),"Own isolated selection marker required, no CAD.");int passed=0;
   foreach(string fault in new[]{"control","throw_after_change","geometry_drift","null_positive"}){
    var f=new OwnedOperationPlanTests.Fixture(true,true);var c=new Context{Snapshot=Clone(f.Snapshot),Listing=Clone(f.Listing)};c.Snapshot["entities"]=new[]{EntitySets.Inventory(c.Snapshot)[0]};c.Snapshot["entityCount"]=1;
    for(int phase=0;phase<(fault=="control"?2:1);phase++){
     var args=Binding(f,c);args["parameters"]=phase==0?new Dictionary<string,object>{{"mode","select_single"},{"entityKey",new[]{40,5}}}:new Dictionary<string,object>{{"mode","clear"}};string prepId=Guid.NewGuid().ToString("N");var p=OwnedSelection.Check(f.Root,prepId,args,true,c);Require(Equals(p["state"],"owned_selection_prepared_observed"),Program.Json.Serialize(p));NativeJournal.Freeze(Path.Combine(f.Root,"responses",prepId+".json"),p);
     var refs=new Dictionary<string,object>{{"selectionId",prepId},{"selectionSha256",p["selectionSha256"]}};c.ThrowAfterChange=fault=="throw_after_change";c.GeometryDrift=fault=="geometry_drift";c.NullSelected=fault=="null_positive";string exec=Guid.NewGuid().ToString("N");var r=OwnedSelection.Execute(f.Root,exec,refs,c);string rawSha=NativeJournal.Freeze(Path.Combine(f.Root,"responses",exec+".json"),r);bool control=fault=="control";Require(Equals(r["state"],control?"owned_selection_executed_unverified":"owned_selection_outcome_unknown"),Program.Json.Serialize(r));Require(c.Calls==phase+1&&Equals(r["copyFenced"],true),"One callback and fence required.");
     Require(Equals(OwnedSelection.Execute(f.Root,Guid.NewGuid().ToString("N"),refs,c)["state"],"owned_selection_rejected_before_change")&&c.Calls==phase+1,"Intent replay reached callback.");
     var v=OwnedSelection.Verify(f.Root,Verification(f,c,exec,rawSha),c);Require(Equals(v["state"],control?"owned_selection_verified_observed":"owned_selection_verification_rejected"),Program.Json.Serialize(v));Require(control?!File.Exists(OwnedTranslation.FencePath(f.Root,(string)r["copyId"])):File.Exists(OwnedTranslation.FencePath(f.Root,(string)r["copyId"])),"Verifier removed unproved fence.");passed++;
    }
   }
   var program=new OwnedSelectionProgram(false,"Body_1");Require(program.Take()=="SELECT CLEARLIST ADD SOLID 'Body_1'","Wrong fixed selection");bool rejected=false;try{program.Take();}catch(InvalidOperationException){rejected=true;}Require(rejected,"Program replay accepted");passed++;
   foreach(string bad in new[]{"a' SAVE","a\nb","","bad-name"}){rejected=false;try{new OwnedSelectionProgram(false,bad);}catch(ArgumentException){rejected=true;}Require(rejected,"Unsafe name accepted");passed++;}
   Console.WriteLine("PASS: "+passed+" owned selection lifecycle/unknown/fence/metadata/grammar checks; no CAD.");return passed;
  }
 }
}

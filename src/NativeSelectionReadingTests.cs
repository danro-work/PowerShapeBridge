using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class NativeSelectionReadingTests {
  static void Require(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  internal sealed class Context : NativeSelectionReading.IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal string Fault;internal int SelectionReads,ModelReads,Observations;internal bool Empty;
   internal Context(OwnedOperationPlanTests.Fixture f){Snapshot=Clone(f.Snapshot);Listing=Clone(f.Listing);}
   public Dictionary<string,object> Windows(){var value=Clone(Listing);if(Fault=="windows"&&Observations>=2)value["processId"]=99;return value;}
   public Dictionary<string,object> Observation(){Observations++;var value=Clone(Snapshot);if(Fault=="document"&&Observations>=2)((Dictionary<string,object>)value["model"])["id"]=99;return value;}
   public void ConfirmDocument(){if(Fault=="retained")throw new InvalidOperationException("Retained document changed.");}
   public object ModelItems(){ModelReads++;if(Fault=="null_inventory")return null;if(Fault=="inventory_drift"&&ModelReads>1)return new[]{40,21};return new[]{40,21,59};}
   public object SelectedItems(){SelectionReads++;if(Fault=="null_selection")return null;if(Fault=="scalar_selection")return 40;if(Fault=="wrong_id_type")return new[]{40.0};if(Fault=="multi_rank")return new int[1,1];if(Fault=="duplicate")return new[]{40,40};if(Fault=="foreign")return new[]{999};if(Fault=="selection_drift"&&SelectionReads>1)return new[]{21};if(Empty)return new int[0];if(Fault=="reordered"&&SelectionReads>1)return new[]{21,40};if(Fault=="lower_bound"){var values=Array.CreateInstance(typeof(int),new[]{2},new[]{5});values.SetValue(40,5);values.SetValue(21,6);return values;}return new[]{40,21};}
   public object ItemData(object composites) {
    var items=((Array)composites).Cast<int>().ToArray();var inventory=EntitySets.Inventory(Snapshot);var types=items.Select(i=>i).ToArray();var ids=items.Select(i=>i==40?5:i==21?8:i==59?10:999).ToArray();var names=items.Select(i=>i==999?"Foreign":"Synthetic_"+i).ToArray();var levels=items.Select(i=>0).ToArray();if(Fault=="wrong_metadata")names[0]="Different";if(Fault=="count_mismatch")return new object[]{types,ids,new string[0],levels};if(Fault=="null_payload")return null;return new object[]{types,ids,names,levels};
   }
   public void Dispose(){}
  }
  internal static int Run() {
   Require(File.Exists(Path.Combine(Program.Root,".native-selection-fixture")),"Explicit own native selection marker required; no CAD.");var rows=new List<Dictionary<string,object>>();
   foreach(string fault in new[]{"control","empty","reordered","lower_bound","null_inventory","null_selection","scalar_selection","wrong_id_type","multi_rank","duplicate","foreign","selection_drift","inventory_drift","wrong_metadata","count_mismatch","null_payload","wrong_count","document","windows","retained","expired"}) {
    var f=new OwnedOperationPlanTests.Fixture(true,true);var context=new Context(f){Fault=fault,Empty=fault=="empty"};int count=fault=="empty"?0:fault=="wrong_count"?1:2;context.Snapshot["contextBefore"]=context.Snapshot["contextAfter"]=new[]{count,0};var expected=Clone(context.Snapshot);string id=Guid.NewGuid().ToString("N");var result=NativeSelectionReading.Read(expected,DateTime.UtcNow.AddSeconds(fault=="expired"?-1:150),context);bool control=new[]{"control","empty","reordered","lower_bound"}.Contains(fault);
    Require(Equals(result["state"],control?"native_selection_items_read_unqualified":"native_selection_read_rejected"),fault+": "+Program.Json.Serialize(result));foreach(string flag in new[]{"contextChangeAttempted","geometryMutationAttempted","cadSaveAttempted","nativeLayoutQualified"})Require(Equals(result[flag],false),"Passive getter acquired authority.");if(control)Require(Equals(result["selectionCount"],context.Empty?0:2)&&context.SelectionReads==2&&context.ModelReads==2&&context.Observations==2,"Missing independent direct getter checks.");
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"result",result},{"selectionReads",context.SelectionReads},{"modelReads",context.ModelReads},{"observations",context.Observations},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","native-selection-reading-tests.json"),new Dictionary<string,object>{{"state","passed"},{"rows",rows},{"cadConnectionAttempted",false}});Console.WriteLine("PASS: "+rows.Count+" passive native selection decoding/guard cases; no CAD.");return rows.Count;
  }
 }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Native getters only. No SelectionManager/Exec/activation/selection mutation or export fallback.
 internal static class NativeSelectionReading {
  internal interface IContext : DocumentBindings.IContext {object ModelItems();object SelectedItems();object ItemData(object composites);}
  static void Require(bool ok,string error){if(!ok)throw new InvalidDataException(error);}
  static object[] Values(object raw) {
   Require(raw!=null,"Null native getter result is failure, not empty selection.");var array=raw as Array;if(array!=null){Require(array.Rank==1&&array.Length<=100000,"Bounded one-dimensional native array required.");return array.Cast<object>().ToArray();}var list=raw as IList;Require(list!=null&&!(raw is string)&&list.Count<=100000,"Native array required.");return list.Cast<object>().ToArray();
  }
  static int[] Ids(object raw){var values=Values(raw);Require(values.All(v=>v is int&&(int)v>0),"Native composite IDs must be positive VT_I4.");var ids=values.Cast<int>().ToArray();Require(ids.Distinct().Count()==ids.Length,"Duplicate native composite IDs.");return ids;}
  static Dictionary<int,Dictionary<string,object>> Metadata(int[] composites,object raw) {
   var data=Values(raw);Require(data.Length==4,"Exact types/IDs/names/levels getter payload required.");var fields=data.Select(Values).ToArray();Require(fields.All(v=>v.Length==composites.Length),"ItemData array counts differ.");var result=new Dictionary<int,Dictionary<string,object>>();var keys=new HashSet<string>();
   for(int index=0;index<composites.Length;index++){Require(fields[0][index] is int&&(int)fields[0][index]>=0&&fields[1][index] is int&&(int)fields[1][index]>0&&fields[2][index] is string&&fields[3][index] is int,"Native tuple/name/level types differ.");int type=(int)fields[0][index],id=(int)fields[1][index];Require(keys.Add(type+":"+id),"Ambiguous tuple-to-composite mapping.");result.Add(composites[index],new Dictionary<string,object>{{"compositeId",composites[index]},{"nativeTypeCode",type},{"nativeId",id},{"name",fields[2][index]},{"levelNumber",fields[3][index]}});}
   return result;
  }
  internal static Dictionary<string,object> Read(Dictionary<string,object> expected,DateTime expires,IContext context) {
   var result=new Dictionary<string,object>{{"state","native_selection_read_rejected"},{"contextChangeAttempted",false},{"geometryMutationAttempted",false},{"cadSaveAttempted",false},{"nativeLayoutQualified",false}};
   try {
    EntitySets.CheckObserved(expected,expected,new int[0][],expires,DateTime.UtcNow);context.ConfirmDocument();string windows=DocumentBindings.WindowDigest(context.Windows());var first=context.Observation();EntitySets.CheckObserved(expected,first,new int[0][],expires,DateTime.UtcNow);
    var fullIds=Ids(context.ModelItems());var full=fullIds.Length==0?new Dictionary<int,Dictionary<string,object>>():Metadata(fullIds,context.ItemData(fullIds));var inventory=EntitySets.Inventory(expected);Require(full.Count==inventory.Length,"Direct inventory count differs.");foreach(var entry in inventory){var matches=full.Values.Where(v=>Equals(v["nativeTypeCode"],entry["nativeTypeCode"])&&Equals(v["nativeId"],entry["nativeId"])).ToArray();Require(matches.Length==1&&Equals(matches[0]["name"],entry["name"])&&Equals(matches[0]["levelNumber"],entry["levelNumber"]),"Direct inventory metadata differs.");}
    var selectedIds=Ids(context.SelectedItems());var selected=selectedIds.Length==0?new Dictionary<int,Dictionary<string,object>>():Metadata(selectedIds,context.ItemData(selectedIds));Require(selected.All(pair=>full.ContainsKey(pair.Key)&&EntitySets.Canonical(pair.Value)==EntitySets.Canonical(full[pair.Key])),"Selected item does not match the current inventory.");
    var selectedAgain=Ids(context.SelectedItems());Require(selectedIds.OrderBy(v=>v).SequenceEqual(selectedAgain.OrderBy(v=>v)),"Native selection set changed between getters.");var fullAgain=Ids(context.ModelItems());var metadataAgain=fullAgain.Length==0?new Dictionary<int,Dictionary<string,object>>():Metadata(fullAgain,context.ItemData(fullAgain));Require(fullIds.OrderBy(v=>v).SequenceEqual(fullAgain.OrderBy(v=>v))&&EntitySets.Canonical(full.OrderBy(p=>p.Key).Select(p=>p.Value))==EntitySets.Canonical(metadataAgain.OrderBy(p=>p.Key).Select(p=>p.Value)),"Native inventory changed between getters.");
    var after=context.Observation();EntitySets.CheckObserved(expected,after,new int[0][],expires,DateTime.UtcNow);var counts=Values(after["contextAfter"]);Require(counts.Length==2&&counts[0] is int&&Equals(counts[0],selectedIds.Length),"Observed selection count differs from direct getter.");context.ConfirmDocument();Require(windows==DocumentBindings.WindowDigest(context.Windows())&&DateTime.UtcNow<expires,"Native selection document/window/lifetime changed.");
    result["state"]="native_selection_items_read_unqualified";result["selectedCompositeIdsReportedOrder"]=selectedIds;result["selectedItemsCanonicalOrder"]=selected.OrderBy(p=>p.Key).Select(p=>p.Value).ToArray();result["selectedEntityKeys"]=selected.OrderBy(p=>p.Key).Select(p=>new[]{(int)p.Value["nativeTypeCode"],(int)p.Value["nativeId"]}).ToArray();result["selectionCount"]=selectedIds.Length;result["windowContextSha256"]=windows;result["getter"]="IPshapeModelDoc.GetSelectedItems/GetModelItems/GetItemData";
   }catch(Exception error){result["error"]=error.Message;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}

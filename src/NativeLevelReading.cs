using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Slot metadata only. Never changes level filters/visibility, selection or CAD.
 internal static class NativeLevelReading {
  internal interface IContext : DocumentBindings.IContext {
   object LevelFields();
   object LevelFilters();
   object LevelSample(int number);
  }
  static void Require(bool value,string error){if(!value)throw new InvalidDataException(error);}
  static object[] ArrayValues(object raw,int count) {
   var array=raw as Array;Require(array!=null&&array.Rank==1&&array.Length==count,"Exact one-dimensional native array required.");
   return array.Cast<object>().ToArray();
  }
  static int Bit(object raw){if(raw is int&&((int)raw==0||(int)raw==1))return (int)raw;if(raw is double&&((double)raw==0||(double)raw==1))return (int)(double)raw;throw new InvalidDataException("Native level flag must be Int32/Double exactly zero/one.");}
  static Dictionary<string,object>[] Rows(object raw) {
   var fields=ArrayValues(raw,3).Select(value=>ArrayValues(value,1000)).ToArray();
   return Enumerable.Range(0,1000).Select(number=>{
    Require(fields[0][number] is string&&((string)fields[0][number]).Length<=4096,"Bounded native level name required.");
    Bit(fields[1][number]);Bit(fields[2][number]);return new Dictionary<string,object>{{"number",number},{"name",fields[0][number]},{"usedRaw",fields[1][number]},{"activeRaw",fields[2][number]},{"usedValueType",fields[1][number].GetType().FullName},{"activeValueType",fields[2][number].GetType().FullName}};
   }).ToArray();
  }
  static int[] Filters(object raw){return ArrayValues(raw,3).Select(Bit).ToArray();}
  internal static Dictionary<string,object> Read(Dictionary<string,object> expected,DateTime expires,IContext context) {
   var result=new Dictionary<string,object>{{"state","native_levels_read_rejected"},{"contextChangeAttempted",false},
    {"geometryMutationAttempted",false},{"cadSaveAttempted",false},{"nativeLayoutQualified",false}};
   try {
    EntitySets.CheckObserved(expected,expected,new int[0][],expires,DateTime.UtcNow);context.ConfirmDocument();
    var windowsBefore=context.Windows();string windows=DocumentBindings.WindowDigest(windowsBefore);var before=context.Observation();
    EntitySets.CheckObserved(expected,before,new int[0][],expires,DateTime.UtcNow);
    var filters=Filters(context.LevelFilters());var rows=Rows(context.LevelFields());
    foreach(int number in new[]{0,5,997,999}) {
     var sample=ArrayValues(context.LevelSample(number),3);
     Require(sample[0] is string&&Equals(sample[0],rows[number]["name"])&&Bit(sample[1])==Bit(rows[number]["usedRaw"])&&Bit(sample[2])==Bit(rows[number]["activeRaw"]),"Batch/scalar level mapping differs.");
    }
    var occupied=EntitySets.Inventory(expected).Select(entity=>(int)entity["levelNumber"]).Distinct().ToArray();
    Require(occupied.All(number=>number>=0&&number<=999),"Inventory level outside native slot range.");
    var observedLevels=((IEnumerable)expected["levelsContainingEntities"]).Cast<object>().Select(item=>item as Dictionary<string,object>).ToArray();
    Require(observedLevels.All(level=>level!=null&&level.ContainsKey("number")&&level["number"] is int)&&observedLevels.Select(level=>(int)level["number"]).OrderBy(number=>number).SequenceEqual(occupied.OrderBy(number=>number)),"Observed occupied level coverage differs.");
    foreach(var level in observedLevels) {
     int number=(int)level["number"];Require(occupied.Contains(number)&&Equals(level["name"],rows[number]["name"])&&Bit(level["activeRaw"])==Bit(rows[number]["activeRaw"]),"Observed occupied level metadata differs.");
    }
    var again=Rows(context.LevelFields());Require(EntitySets.Canonical(rows)==EntitySets.Canonical(again),"Native level slots changed between getters.");
    Require(filters.SequenceEqual(Filters(context.LevelFilters())),"Level filter settings changed during passive read.");
    var after=context.Observation();EntitySets.CheckObserved(expected,after,new int[0][],expires,DateTime.UtcNow);
    context.ConfirmDocument();Require(windows==DocumentBindings.WindowDigest(context.Windows())&&DateTime.UtcNow<expires,"Native levels document/window/lifetime changed.");
    result["levelSlots"]=rows;result["slotCount"]=1000;
    result["filterFlagsRaw"]=filters;result["filterOrder"]=new[]{"used","named","on"};
    result["observedPropertiesSha256"]=EntitySets.ObservedDigest(after);result["windowContextSha256"]=windows;
    result["observedDocument"]=after["model"];result["processId"]=after["processId"];result["processStartedUtc"]=after["processStartedUtc"];result["windowsSessionId"]=after["windowsSessionId"];
    result["before"]=before;result["after"]=after;result["windowsBefore"]=windowsBefore;result["windowsAfter"]=context.Windows();
    Require(windows==DocumentBindings.WindowDigest((Dictionary<string,object>)result["windowsAfter"])&&DateTime.UtcNow<expires,"Window context/lifetime changed while finalizing read.");
    result["qualification"]="Current native slot names/used/active flags only; usedRaw does not establish allocation, ownership or authorization to rename/delete/reuse a slot. No complete topology or continuous identity proof.";
    result["state"]="native_levels_read_unqualified";
   }catch(Exception error){result["error"]=error.Message;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  internal static Dictionary<string,object> Run() {
   using(var context=new DocumentBindings.ComContext(Program.ReadConfig())) {
    var expected=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(context.Observation()));
    return Read(expected,DateTime.UtcNow.AddSeconds(90),context);
   }
  }
 }
}

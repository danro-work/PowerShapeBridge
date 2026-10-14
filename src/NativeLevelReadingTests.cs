using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class NativeLevelReadingTests {
  static Dictionary<string,object> Clone(object value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  internal sealed class Context : NativeLevelReading.IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal string Fault;internal int FieldsReads,FilterReads,Observations,WindowReads;
   internal Context(OwnedOperationPlanTests.Fixture fixture){Snapshot=Clone(fixture.Snapshot);Listing=Clone(fixture.Listing);Snapshot["levelsContainingEntities"]=new[]{new Dictionary<string,object>{{"number",0},{"name","General"},{"activeRaw",1}}};}
   public Dictionary<string,object> Observation(){Observations++;var result=Clone(Snapshot);if(Fault=="document"&&Observations>1)((Dictionary<string,object>)result["model"])["id"]=99;return result;}
   public Dictionary<string,object> Windows(){WindowReads++;var result=Clone(Listing);if((Fault=="windows"&&Observations>1)||(Fault=="windows_final"&&WindowReads>=3))result["processId"]=99;return result;}
   public void ConfirmDocument(){if(Fault=="retained")throw new InvalidOperationException("Retained document changed.");}
   public object LevelFilters(){FilterReads++;if(Fault=="null_filters")return null;if(Fault=="wrong_filter_count")return new[]{0,0};if(Fault=="boolean_filter")return new object[]{true,0,0};if(Fault=="double_flags")return new[]{0.0,0.0,0.0};return new[]{0,0,Fault=="filter_drift"&&FilterReads>1?1:0};}
   public object LevelFields(){
    FieldsReads++;if(Fault=="null_fields")return null;
    object[] names=Enumerable.Repeat((object)"",1000).ToArray(),used=Enumerable.Repeat((object)0,1000).ToArray(),active=Enumerable.Repeat((object)0,1000).ToArray();
    names[0]="General";names[5]="Named unused";used[0]=active[0]=1;
    if(Fault=="wrong_name_type")names[2]=7;if(Fault=="null_name")names[2]=null;if(Fault=="long_name")names[2]=new string('a',4097);
    if(Fault=="double_flags"){used=used.Select(value=>(object)(double)(int)value).ToArray();active=active.Select(value=>(object)(double)(int)value).ToArray();}
    if(Fault=="wrong_used_type")used[5]="0";if(Fault=="fractional_flag")used[5]=0.5;if(Fault=="nan_flag")used[5]=double.NaN;if(Fault=="infinite_flag")used[5]=double.PositiveInfinity;if(Fault=="bad_active_bit")active[5]=2;if(Fault=="level_drift"&&FieldsReads>1)names[5]="Changed";
    if(Fault=="null_names")return new object[]{null,used,active};if(Fault=="bad_rank")return new object[]{new string[10,100],used,active};if(Fault=="wrong_count")return new object[]{names.Take(999).ToArray(),used,active};
    if(Fault=="lower_bound"){var shifted=Array.CreateInstance(typeof(object),new[]{1000},new[]{5});for(int i=0;i<1000;i++)shifted.SetValue(names[i],i+5);return new object[]{shifted,used,active};}
    return new object[]{names,used,active};
   }
   public object LevelSample(int number){return new object[]{Fault=="scalar_drift"&&number==5?"Different":number==0?"General":number==5?"Named unused":"",number==0?1:0,number==0?1:0};}
   public void Dispose(){}
  }
  internal static int Run(){
   if(!File.Exists(Path.Combine(Program.Root,".native-levels-fixture")))throw new InvalidOperationException("Explicit isolated fixture marker required; no CAD.");
   var fixture=new OwnedOperationPlanTests.Fixture(true,true);var rows=new List<Dictionary<string,object>>();
   foreach(string fault in new[]{"control","lower_bound","double_flags","null_fields","null_names","bad_rank","wrong_count","wrong_name_type","null_name","long_name","wrong_used_type","fractional_flag","nan_flag","infinite_flag","bad_active_bit","null_filters","wrong_filter_count","boolean_filter","filter_drift","level_drift","scalar_drift","observed_level_mismatch","missing_observed_levels","foreign_level","document","windows","windows_final","expired","retained"}){
    var context=new Context(fixture){Fault=fault};if(fault=="observed_level_mismatch")context.Snapshot["levelsContainingEntities"]=new[]{new Dictionary<string,object>{{"number",0},{"name","Other"},{"activeRaw",1}}};if(fault=="missing_observed_levels")context.Snapshot["levelsContainingEntities"]=new object[0];if(fault=="foreign_level")((Dictionary<string,object>)((IList)context.Snapshot["entities"])[0])["levelNumber"]=1000;
    var result=NativeLevelReading.Read(Clone(context.Snapshot),DateTime.UtcNow.AddSeconds(fault=="expired"?-1:150),context);bool control=fault=="control"||fault=="lower_bound"||fault=="double_flags";
    if(!Equals(result["state"],control?"native_levels_read_unqualified":"native_levels_read_rejected"))throw new InvalidOperationException(fault+": "+Program.Json.Serialize(result));
    foreach(string flag in new[]{"contextChangeAttempted","geometryMutationAttempted","cadSaveAttempted","nativeLayoutQualified"})if(!Equals(result[flag],false))throw new InvalidOperationException("Passive read acquired authority.");
    if(control){var slots=(Dictionary<string,object>[])result["levelSlots"];if(slots.Length!=1000||!Equals(slots[5]["name"],"Named unused")||Convert.ToDouble(slots[5]["usedRaw"])!=0||context.FieldsReads!=2||context.FilterReads!=2||context.Observations!=2)throw new InvalidOperationException("Incomplete unfiltered slot read.");}
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"state",result["state"]},{"error",result.ContainsKey("error")?result["error"]:null},{"fieldsReads",context.FieldsReads},{"filterReads",context.FilterReads},{"observations",context.Observations},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","native-levels-fixture.json"),rows);Console.WriteLine("Native levels fixture passed "+rows.Count+" cases; no CAD.");return rows.Count;
  }
 }
}

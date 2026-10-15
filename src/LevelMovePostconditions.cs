using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class LevelMovePostconditions {
  static void Need(bool value,string reason){if(!value)throw new InvalidDataException(reason);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  internal static void Bounds(double[] before,double[] after) {
   Need(before!=null&&after!=null&&before.Length==6&&after.Length==6,"Six direct bound coordinates required.");
   Need(before.Concat(after).All(value=>!double.IsNaN(value)&&!double.IsInfinity(value))&&Enumerable.Range(0,3).All(axis=>before[axis*2]<=before[axis*2+1]&&after[axis*2]<=after[axis*2+1]),"Finite ordered direct bounds required.");
   Need(OwnedTranslation.Near(before,after),"Level transfer changed direct geometry bounds.");
  }
  internal static void Check(Dictionary<string,object> before,Dictionary<string,object> after,Dictionary<string,object> levelsBefore,Dictionary<string,object> levelsAfter,int nativeId,int target) {
   var expected=Clone(before);var actual=Clone(after);
   var original=EntitySets.Inventory(expected).Single(item=>Equals(item["nativeTypeCode"],40)&&Equals(item["nativeId"],nativeId));int source=(int)original["levelNumber"];
   original["levelNumber"]=target;
   var ctx=(IList)expected["contextAfter"];Need(ctx.Count==2&&ctx[1] is int,"Explicit original workplane required.");
   expected["contextBefore"]=expected["contextAfter"]=new[]{0,(int)ctx[1]};expected["selectionBefore"]=expected["selectionAfter"]=new object[0];
   Need(Equals(levelsBefore["state"],"native_levels_read_unqualified")&&Equals(levelsAfter["state"],"native_levels_read_unqualified"),"Successful passive level reads required.");
   foreach(string key in new[]{"contextChangeAttempted","geometryMutationAttempted","cadSaveAttempted","nativeLayoutQualified"})Need(Equals(levelsBefore[key],false)&&Equals(levelsAfter[key],false),"Level getter changed context.");
   Need(EntitySets.ObservedDigest((Dictionary<string,object>)levelsBefore["before"])==EntitySets.ObservedDigest(before)&&EntitySets.ObservedDigest((Dictionary<string,object>)levelsAfter["before"])==EntitySets.ObservedDigest(after)&&EntitySets.ObservedDigest((Dictionary<string,object>)levelsAfter["after"])==EntitySets.ObservedDigest(after),"Level getter and model observations disagree.");
   foreach(string key in new[]{"filterFlagsRaw","filterOrder","windowsBefore","windowsAfter"})Need(EntitySets.Canonical(levelsBefore[key])==EntitySets.Canonical(levelsAfter[key]),"Level filters or windows changed.");
   var oldRows=(IList)levelsBefore["levelSlots"];var newRows=(IList)levelsAfter["levelSlots"];Need(oldRows.Count==1000&&newRows.Count==1000,"Complete level slots required.");
   for(int number=0;number<1000;number++) {
    var oldRow=(Dictionary<string,object>)oldRows[number];var newRow=Clone((Dictionary<string,object>)newRows[number]);
    Need(Equals(oldRow["number"],number)&&Equals(newRow["number"],number),"Native level order differs.");
    // USED may change on the two affected slots; it is not an allocation proof.
    if(number==source||number==target){double used=Convert.ToDouble(newRow["usedRaw"]);Need(used==0||used==1,"Invalid affected USED flag.");newRow["usedRaw"]=oldRow["usedRaw"];}
    Need(EntitySets.Canonical(oldRow)==EntitySets.Canonical(newRow),"Unplanned level metadata change.");
   }
   var occupied=EntitySets.Inventory(expected).Select(item=>(int)item["levelNumber"]).Distinct().OrderBy(number=>number);
   expected["levelsContainingEntities"]=occupied.Select(number=>{var row=(Dictionary<string,object>)newRows[number];return new Dictionary<string,object>{{"number",number},{"name",row["name"]},{"activeRaw",row["activeRaw"]}};}).ToArray();
   Need(EntitySets.ObservedDigest(expected)==EntitySets.ObservedDigest(actual),"Target assignment, other objects, geometry, selection, workplane or units differ.");
  }
 }
}

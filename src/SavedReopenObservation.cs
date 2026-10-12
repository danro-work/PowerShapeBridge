using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace PowerShapeBridge {
 // Observable multiset comparison across documents. Never maps old entity IDs.
 // Numeric tolerance applies only to available geometry/workplane measurements.
 internal static class SavedReopenObservation {
  internal const double AbsoluteTolerance=1e-9,RelativeTolerance=1e-9;
  internal const string Scope="Observable inventory metadata, available world-coordinate properties, workplanes and raw units; absolute/relative measurement tolerance 1e-9. Missing values remain missing. No native-ID correspondence or full topology claim.";
  static bool Number(object value){return value is int||value is long||value is double||value is float||value is decimal;}
  static bool Same(object left,object right,bool approximate,int depth=0) {
   if(depth>32)throw new ArgumentException("Measurement nesting exceeds limit.");
   if(left==null||right==null)return left==right;
   if(Number(left)&&Number(right)&&approximate){double a=Program.StrictDouble(left),b=Program.StrictDouble(right);return Math.Abs(a-b)<=AbsoluteTolerance+RelativeTolerance*Math.Max(Math.Abs(a),Math.Abs(b));}
   var l=left as Dictionary<string,object>;var r=right as Dictionary<string,object>;
   if(l!=null||r!=null)return l!=null&&r!=null&&l.Count==r.Count&&l.All(p=>r.ContainsKey(p.Key)&&Same(p.Value,r[p.Key],approximate&&p.Key!="surfaces"&&p.Key!="nlinks_open",depth+1));
   var la=left as IList;var ra=right as IList;if(la!=null||ra!=null)return la!=null&&ra!=null&&la.Count==ra.Count&&Enumerable.Range(0,la.Count).All(i=>Same(la[i],ra[i],approximate,depth+1));
   return EntitySets.Canonical(left)==EntitySets.Canonical(right);
  }
  static Dictionary<string,object> WithoutId(Dictionary<string,object> value){var result=new Dictionary<string,object>(value);result.Remove("nativeId");return result;}
  static void Multiset(Dictionary<string,object>[] before,Dictionary<string,object>[] after,bool geometryOnly) {
   if(before.Length!=after.Length)throw new InvalidOperationException("Reopened observation count changed.");
   if(before.Select(EntitySets.Canonical).OrderBy(v=>v,StringComparer.Ordinal).SequenceEqual(after.Select(EntitySets.Canonical).OrderBy(v=>v,StringComparer.Ordinal)))return;
   Func<Dictionary<string,object>,Dictionary<string,object>,bool> equal=(left,right)=>geometryOnly?
    left.Count==right.Count&&left.All(p=>right.ContainsKey(p.Key)&&Same(p.Value,right[p.Key],p.Key=="geometry")) : Same(left,right,true);
   int[] matches=Enumerable.Repeat(-1,after.Length).ToArray();
   Func<int,bool[],bool> augment=null;augment=(row,seen)=>{for(int col=0;col<after.Length;col++)if(!seen[col]&&equal(before[row],after[col])){seen[col]=true;if(matches[col]<0||augment(matches[col],seen)){matches[col]=row;return true;}}return false;};
   for(int row=0;row<before.Length;row++) {
    // Prefer an unmatched compatible record; use augmenting paths only when
    // needed to preserve a complete match for duplicate/near-equal observations.
    int free=-1;for(int col=0;col<after.Length;col++)if(matches[col]<0&&equal(before[row],after[col])){free=col;break;}
    if(free>=0)matches[free]=row;else if(!augment(row,new bool[after.Length]))throw new InvalidOperationException("Reopened observable metadata/geometry multiset differs.");
   }
  }
  internal static void Check(Dictionary<string,object> before,Dictionary<string,object> after) {
   before=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(before));after=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(after));
   EntitySets.ObservedDigest(before);EntitySets.ObservedDigest(after);
   foreach(string key in new[]{"entityCount","levelsContainingEntities","lengthUnitsRaw","lengthUnitNameRaw","lengthUnitFactorRaw","areaUnitNameRaw","areaUnitFactorRaw","volumeUnitNameRaw","volumeUnitFactorRaw","generalToleranceRaw"})
    if(!Same(before[key],after[key],false))throw new InvalidOperationException("Reopened raw units/inventory metadata differ: "+key);
   foreach(var data in new[]{before,after}) {
    foreach(string key in new[]{"selectionBefore","selectionAfter"})if(((IList)data[key]).Count!=0)throw new InvalidOperationException("Empty selection required for saved reopen observation.");
    foreach(string key in new[]{"contextBefore","contextAfter"}){var context=(IList)data[key];if(context.Count!=2||!Equals(context[0],0)||!Equals(context[1],0))throw new InvalidOperationException("Saved reopen currently requires qualified world observation context.");}
   }
   Multiset(EntitySets.Inventory(before).Select(WithoutId).ToArray(),EntitySets.Inventory(after).Select(WithoutId).ToArray(),true);
   Multiset(((IList)before["workplanes"]).Cast<Dictionary<string,object>>().Select(WithoutId).ToArray(),((IList)after["workplanes"]).Cast<Dictionary<string,object>>().Select(WithoutId).ToArray(),false);
  }
 }
}

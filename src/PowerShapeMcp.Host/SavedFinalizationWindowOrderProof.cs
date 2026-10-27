using System.Text.Json.Nodes;
namespace PowerShapeMcp;
internal static class SavedFinalizationWindowOrderProof
{
 internal static JsonObject Policy()=>new(){["schemaVersion"]=1,["kind"]="remove_owned_target_preserve_survivor_order_v1",["windowNumbersMayReindex"]=true,["nativeWindowIdsNotPersistent"]=true,["logicalFieldsExact"]=new JsonArray("type","modelId","modelName","modelPath")};
 static void Need(bool yes){if(!yes)throw new ReferenceRejected("ordered_finalization_window_proof_differs");}
 static int I(JsonNode? v)=>SurfaceReadTools.Integer(v);
 static JsonObject Key(JsonObject r)=>new(){["type"]=r["type"]!.DeepClone(),["modelId"]=r["modelId"]!.DeepClone(),["modelName"]=r["modelName"]!.DeepClone(),["modelPath"]=r["modelPath"]!.DeepClone()};
 static JsonObject[] Rows(JsonObject listing){var rows=listing["windows"]!.AsArray().Select(v=>v!.AsObject()).ToArray();Need(rows.Length is >=1 and <=32&&I(listing["windowCount"])==rows.Length);foreach(var r in rows){Need(ArtifactProof.Fields(r,"windowNumber","nativeWindowId","type","selectedRaw","modelId","modelName","modelPath")&&I(r["windowNumber"])>0&&I(r["selectedRaw"])is 0 or 1);_=I(r["nativeWindowId"]);_=I(r["modelId"]);foreach(var k in new[]{"type","modelName","modelPath"})_=r[k]!.GetValue<string>();}foreach(var k in new[]{"windowNumber","nativeWindowId","modelId"})Need(rows.Select(r=>I(r[k])).Distinct().Count()==rows.Length);Need(rows.Count(r=>I(r["selectedRaw"])==1)==1&&rows.Count(r=>JsonNode.DeepEquals(r,listing["activeWindow"])&&I(r["selectedRaw"])==1)==1);return rows.OrderBy(r=>I(r["windowNumber"])).ToArray();}
 internal static void Check(JsonObject before,JsonObject after,int target){foreach(var k in new[]{"processId","processStartedUtc","windowsSessionId"})Need(JsonNode.DeepEquals(before[k],after[k]));var old=Rows(before);var current=Rows(after);Need(old.Length>=2&&current.Length==old.Length-1&&old.Count(r=>I(r["modelId"])==target)==1&&!current.Any(r=>I(r["modelId"])==target)&&I(before["activeWindow"]!["modelId"])==target);var survivors=old.Where(r=>I(r["modelId"])!=target).ToArray();for(int i=0;i<survivors.Length;i++)Need(JsonNode.DeepEquals(Key(survivors[i]),Key(current[i])));}
}

using System.Text.Json.Nodes;
using PowerShapeMcp;
internal static class SavedFinalizationOrderedCheck
{
 internal static int Run(string input,string output){var r=JsonNode.Parse(File.ReadAllText(input))!.AsObject();if(!JsonNode.DeepEquals(r["policy"],SavedFinalizationWindowOrderProof.Policy()))throw new InvalidDataException("Native/SDK frozen window policy differs.");int cases=0;foreach(var row in r["rows"]!.AsArray()){bool rejected=false;try{SavedFinalizationWindowOrderProof.Check(row!["before"]!.AsObject(),row["after"]!.AsObject(),202);}catch(ReferenceRejected){rejected=true;}if(rejected!=row!["rejected"]!.GetValue<bool>())throw new InvalidDataException("Native/SDK survivor proof differs: "+row["fault"]);cases++;}Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,new JsonObject{["state"]="passed",["cases"]=cases,["nativePolicyMatchesSdk"]=true,["cadConnectionAttempted"]=false}.ToJsonString(new(){WriteIndented=true}));Console.WriteLine($"PASS: {cases} independent Framework/SDK ordered window proofs; no CAD.");return 0;}
}

using System.Text.Json.Nodes;
using PowerShapeMcp;

if(args.Length==4&&args[0]=="--recovery-inspect")
{
    var recoveryRoot=Path.GetFullPath(args[1]);var recoveryOutput=Path.GetFullPath(args[3]);
    var inspected=new RecoveryInspection(recoveryRoot).Read(args[2],TimeSpan.FromSeconds(30));
    Directory.CreateDirectory(Path.GetDirectoryName(recoveryOutput)!);File.WriteAllText(recoveryOutput,inspected.ToJsonString(new(){WriteIndented=true}));
    Console.WriteLine($"Recovery recorded inspection: {inspected["state"]}; {inspected["assessment"]}; no CAD, artifact repair or replay.");return inspected["state"]?.GetValue<string>()=="recovery_inspected_recorded"?0:1;
}
if(args.Length!=2)throw new ArgumentException("Provide oracle JSON and result path.");
var oracle=JsonNode.Parse(File.ReadAllText(args[0]))!.AsObject();
var stage=Path.GetFullPath(oracle["stage"]!.GetValue<string>());
var checkedCases=new JsonArray();var mismatches=0;var accepted=0;
foreach(var node in oracle["cases"]!.AsArray())
{
    var item=node!.AsObject();var root=Path.GetFullPath(item["root"]!.GetValue<string>());
    if(!root.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Fixture root is outside supplied stage.");
    var receipt=item["receipt"]!.AsObject();var expected=item["accepted"]!.GetValue<bool>();var actual=false;string? error=null;
    try
    {
        switch(item["kind"]!.GetValue<string>())
        {
            case "binding":new DocumentBindingStore(root,new(),new()).VerifyReceipt(receipt);break;
            case "packet":new OwnedOperationStore(root,new OperationPlanStore(root,new()),new DocumentBindingStore(root,new(),new())).Receipt(receipt);break;
            case "execution":OwnedExecutionProof.TranslationReceipt(root,receipt,item["parameters"]!.AsObject());break;
            case "save_target":new OwnedSaveTargetStore(root,new DocumentBindingStore(root,new(),new())).VerifyReceipt(receipt);break;
            case "saved_result":new OwnedSavedResultStore(root,new OwnedSaveTargetStore(root,new DocumentBindingStore(root,new(),new())),new()).VerifyReceipt(receipt,item["parameters"]!.AsObject());break;
            case "saved_reopen":new OwnedReopenStore(root,new OwnedSavedResultStore(root,new OwnedSaveTargetStore(root,new DocumentBindingStore(root,new(),new())),new())).VerifyReceipt(receipt);break;
            case "saved_reopen_verified":using(var session=new BridgeSession(new(root,TimeSpan.FromSeconds(30),false)))OwnedReopenProof.Verification(session,receipt,item["parameters"]!.AsObject());break;
            default:throw new InvalidDataException("Unknown fixed fixture kind.");
        }
        actual=true;
    }
    catch(Exception ex)when(ex is ReferenceRejected or IOException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException){error=ex.Message;}
    if(actual!=expected)mismatches++;
    if(actual)accepted++;
    checkedCases.Add(new JsonObject{["root"]=root,["kind"]=item["kind"]!.DeepClone(),["expectedAccepted"]=expected,["actualAccepted"]=actual,["error"]=error});
}
if(checkedCases.Count<5||accepted==0)throw new InvalidDataException("Insufficient actual Framework fixture coverage.");
var result=new JsonObject{["state"]=mismatches==0?"passed":"failed",["cases"]=checkedCases.Count,["accepted"]=accepted,["mismatches"]=mismatches,["cadConnectionAttempted"]=false,["checks"]=checkedCases};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);File.WriteAllText(args[1],result.ToJsonString(new(){WriteIndented=true}));
Console.WriteLine($"Native fixture compatibility: {checkedCases.Count} cases; {accepted} accepted; {mismatches} mismatches; no CAD.");
return mismatches==0?0:1;

using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class SolidScaleRoutesTests
{
    static readonly string[] Names=["prepare_owned_solid_scale","execute_owned_solid_scale","verify_owned_solid_scale"];
    static Fixture Config(bool enabled=true){var f=new Fixture();foreach(var flag in new[]{"entitySetsExperimental","operationPlansExperimental","workingCopiesExperimental","documentBindingsExperimental","uniformScalePlansExperimental","ownedUniformScalePlansExperimental","levelReadingExperimental","recoveryInspectionExperimental"})f.Config[flag]=true;f.Config["ownedSolidScalesExperimental"]=enabled;f.SaveConfig();return f;}
    [Theory][InlineData(false,true,true)][InlineData(true,true,false)][InlineData(false,false,false)]
    public void FeatureAndReadOnlyPolicyGateAllThreeTools(bool readOnly,bool enabled,bool present)
    {
        var f=Config(enabled);using var session=new BridgeSession(f.Options(readOnly));var tools=ToolCatalog.Create(session);foreach(var name in Names)Assert.Equal(present,tools.Any(t=>t.ProtocolTool.Name==name));if(readOnly)Assert.Equal(8,tools.Length);
        Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));
    }
    [Theory][InlineData("ownedUniformScalePlansExperimental")][InlineData("uniformScalePlansExperimental")][InlineData("operationPlansExperimental")][InlineData("workingCopiesExperimental")][InlineData("documentBindingsExperimental")]
    public void MissingDependencyRefusesBeforeTransport(string dependency){var f=Config();f.Config[dependency]=false;f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
    [Fact]
    public void CoercedFlagIsRejected(){var f=Config();f.Config["ownedSolidScalesExperimental"]="true";f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
    [Fact]
    public async Task PassiveOrUnknownHandlesCannotSubmitMutation()
    {
        var f=Config();using var session=new BridgeSession(f.Options());var tools=new SolidScaleTools(session);var response=await tools.Execute(new string('a',32),CancellationToken.None);Assert.True(response.IsError);var value=JsonNode.Parse(response.StructuredContent!.Value.GetRawText())!;Assert.Equal("solid_scale_rejected_before_submission",value["state"]!.GetValue<string>());Assert.False(value["intentConsumed"]!.GetValue<bool>());Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));
    }
    [Fact]
    public void SeparateConnectionStoreCannotAcceptAnotherScope(){var f=Config();using var session=new BridgeSession(f.Options());Assert.Throws<ReferenceRejected>(()=>session.SolidScales.Consume(new string('b',32),CancellationToken.None));session.RevokeCadLeases(true);Assert.Throws<ReferenceRejected>(()=>session.SolidScales.Consume(new string('b',32),CancellationToken.None));}
    static OwnedUniformScaleFixture Source()=>new(sourceSetup:o=>{
        o["entities"]=new JsonArray(o["entities"]![0]!.DeepClone());o["entityCount"]=1;o["busy"]=false;o["generalToleranceRaw"]=0.01;o["lengthUnitsRaw"]="mm";o["lengthUnitFactorRaw"]=1;
        o["entities"]![0]!["geometry"]=JsonNode.Parse("""{"coordinateFrame":"world","cogRaw":[5,10,15],"volumeRaw":0.8,"areaRaw":2400,"surfaces":6,"nlinks_open":0,"closedRaw":1,"connectedRaw":1,"watertightRaw":1,"trimming_validRaw":1}""");
    });
    static (string Handle,JsonObject Proof) Qualified(OwnedUniformScaleFixture f,SolidScaleStore store)
    {
        var id=Guid.NewGuid().ToString("N");var reply=f.Native(id);var packet=reply["ownedScale"]!.AsObject();var rows=new JsonArray();for(int i=0;i<8;i++)rows.Add(new JsonObject{["key"]="corner-"+i,["faceObserved"]="face-"+i,["positionWorldMm"]=new JsonArray((i&1)==0?0:10,(i&2)==0?0:20,(i&4)==0?0:30)});
        var measurement=new JsonObject{["schemaVersion"]=1,["profile"]="solid_uniform_scale_membership_document_tolerance_v1",["entityKey"]=packet["entityKeys"]![0]!.DeepClone(),["sourceCompositeId"]=40,["sourceObservationSha256"]=packet["nativeObservedPropertiesSha256"]!.DeepClone(),["windowContextSha256"]=packet["windowContextSha256"]!.DeepClone(),["sourceBoundsWorldMmPaired"]=new JsonArray(0,10,0,20,0,30),["sourceWitnesses"]=rows,["sourceWitnessSha256"]=new string('a',64),["qualification"]="Synthetic pinned points; no CAD"};
        var bundle=new JsonObject{["schemaVersion"]=1,["qualificationScope"]=SolidScaleProof.Scope,["ownedScaleSha256"]=reply["ownedScaleSha256"]!.DeepClone(),["measurement"]=measurement};foreach(var k in new[]{"ownedScaleId","bindingId","bindingSha256","copyId","ownershipSha256","workingSha256","windowContextSha256"})bundle[k]=packet[k]!.DeepClone();
        reply["membershipBundleSha256"]=ArtifactProof.Freeze(f.Root,"solidscalebundles/"+id+".json",bundle);reply["qualificationScope"]=SolidScaleProof.Scope;ArtifactProof.Freeze(f.Root,"responses/"+id+".json",reply);
        var handle=f.Store.Issue(reply,f.Parameters,f.Plan,f.Binding)["ownedScaleHandle"]!.GetValue<string>();var proof=SolidScaleProof.Preparation(f.Root,f.Store.ValidationParameters(handle));store.Pin(handle,proof);return(handle,proof);
    }
    [Fact]
    public void QualifiedPreparationPinsReceiptAndConsumesBothLeasesOnce(){var f=Source();var store=new SolidScaleStore(f.Root,f.Store);var p=Qualified(f,store);Assert.True(JsonNode.DeepEquals(p.Proof,store.Consume(p.Handle,CancellationToken.None)));Assert.Throws<ReferenceRejected>(()=>store.Consume(p.Handle,CancellationToken.None));Assert.Throws<ReferenceRejected>(()=>f.Store.ValidationParameters(p.Handle));}
    [Fact]
    public void ChangedQualifiedBundleCannotAuthorizeSubmission(){var f=Source();var store=new SolidScaleStore(f.Root,f.Store);var p=Qualified(f,store);File.AppendAllText(Path.Combine(f.Root,"solidscalebundles",p.Proof["ownedScaleId"]!.GetValue<string>()+".json")," ");Assert.Throws<ReferenceRejected>(()=>store.Consume(p.Handle,CancellationToken.None));Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
    [Fact]
    public void PassiveHandleAloneIsInsufficient(){var f=Source();var store=new SolidScaleStore(f.Root,f.Store);var native=f.Native(Guid.NewGuid().ToString("N"));var h=f.Store.Issue(native,f.Parameters,f.Plan,f.Binding)["ownedScaleHandle"]!.GetValue<string>();Assert.Throws<ReferenceRejected>(()=>store.Consume(h,CancellationToken.None));}
}

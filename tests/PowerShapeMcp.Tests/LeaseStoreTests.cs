using System.Text.Json;
using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class LeaseStoreTests
{
    private static JsonObject Snapshot()
    {
        using var fixture=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","observations.json")));
        var data=JsonNode.Parse(fixture.RootElement[0].GetProperty("snapshot").GetRawText())!.AsObject();
        data["timestampUtc"]="2026-10-12T10:00:00Z";
        return data;
    }
    private static DateTimeOffset Utc=>new(2026,10,12,10,0,0,TimeSpan.Zero);
    private static (string Doc,string Entity) ReadIds(JsonObject result)=>(result["documentReference"]!.GetValue<string>(),result["entities"]![0]!["entityReference"]!.GetValue<string>());
    private static (string Doc,string Entity) CandidateIds(JsonObject result)=>(result["candidateDocumentHandle"]!.GetValue<string>(),result["entities"]![0]!["candidateEntityHandle"]!.GetValue<string>());

    [Fact]
    public void ReferencesResolveWithoutExtendingLifetimeOrModifyingSource()
    {
        double time=100;var store=new ReferenceStore(()=>time);var source=Snapshot();
        var ids=ReadIds(store.Issue(source));time=399;
        var result=store.Resolve(ids.Doc,[ids.Entity],source);
        Assert.Equal("entity_references_resolved_observed",result["state"]!.GetValue<string>());
        Assert.Null(source["entities"]![0]!["entityReference"]);
        time=400;
        Assert.Equal("document_reference_unknown_or_expired",Assert.Throws<ReferenceRejected>(()=>store.Require(ids.Doc,[ids.Entity])).Code);
    }
    [Theory]
    [InlineData("process")]
    [InlineData("start")]
    [InlineData("model")]
    [InlineData("path")]
    [InlineData("units")]
    [InlineData("name")]
    [InlineData("level")]
    [InlineData("geometry")]
    [InlineData("workplane")]
    public void ObservedChangesInvalidateOldReferencesEvenAfterSwitchBack(string change)
    {
        var store=new ReferenceStore(()=>100);var source=Snapshot();var ids=ReadIds(store.Issue(source));var fresh=source.DeepClone().AsObject();
        switch(change)
        {
            case "process":fresh["processId"]=101;break;
            case "start":fresh["processStartedUtc"]="2026-10-12T11:00:00Z";break;
            case "model":fresh["model"]!["id"]=10;break;
            case "path":fresh["model"]!["path"]="D:/Parts/other.psmodel";break;
            case "units":fresh["lengthUnitFactorRaw"]=JsonNode.Parse("25.4");break;
            case "name":fresh["entities"]![0]!["name"]="Renamed";break;
            case "level":fresh["entities"]![0]!["levelNumber"]=2;break;
            case "geometry":fresh["entities"]![0]!["geometry"]!["volumeRaw"]=JsonNode.Parse("2.0");break;
            case "workplane":fresh["contextBefore"]=new JsonArray(0,7);break;
        }
        Assert.Throws<ReferenceRejected>(()=>store.Resolve(ids.Doc,[ids.Entity],fresh));
        store.Issue(source);
        Assert.Throws<ReferenceRejected>(()=>store.Require(ids.Doc,[ids.Entity]));
    }
    [Fact]
    public void ReferencesRejectCrossDocumentCrossConnectionAndExpiryDuringRead()
    {
        Func<double>? forced=null;var store=new ReferenceStore(()=>forced?.Invoke()??100);var source=Snapshot();var first=ReadIds(store.Issue(source));var second=ReadIds(store.Issue(source));
        Assert.Equal("entity_reference_unknown_or_wrong_document",Assert.Throws<ReferenceRejected>(()=>store.Require(second.Doc,[first.Entity])).Code);
        Assert.Throws<ReferenceRejected>(()=>new ReferenceStore(()=>100).Require(first.Doc,[first.Entity]));
        var ticks=new Queue<double>([100,401]);forced=()=>ticks.Dequeue();
        Assert.Equal("document_reference_unknown_or_expired",Assert.Throws<ReferenceRejected>(()=>store.Resolve(first.Doc,[first.Entity],source)).Code);
    }
    [Fact]
    public void FailedInspectionClearsReadReferences()
    {
        var source=Snapshot();var store=new ReferenceStore(()=>100);var ids=ReadIds(store.Issue(source));source["state"]="failed";
        Assert.Throws<ReferenceRejected>(()=>store.Observe(source));
        Assert.Throws<ReferenceRejected>(()=>store.Require(ids.Doc,[ids.Entity]));
    }
    [Fact]
    public void ReadReferencesNeverBecomeCandidatesAndLeaseUsesNativeReceiptHash()
    {
        double time=100;var source=Snapshot();var reads=new ReferenceStore(()=>time);var sets=new CandidateStore(()=>time,()=>Utc);
        var read=ReadIds(reads.Issue(source));var candidate=CandidateIds(sets.Issue(source,new string('b',64)));
        Assert.Throws<ReferenceRejected>(()=>sets.PrepareParameters(read.Doc,[read.Entity]));
        Assert.Throws<ReferenceRejected>(()=>sets.PrepareParameters(candidate.Doc,[read.Entity]));
        Assert.Throws<ReferenceRejected>(()=>reads.Require(candidate.Doc,[candidate.Entity]));
        var parameters=sets.PrepareParameters(candidate.Doc,[candidate.Entity]);
        Assert.Equal(new string('b',64),parameters["inspectionSha256"]!.GetValue<string>());
        Assert.Equal("2026-10-12T10:05:00+00:00",parameters["expiresUtc"]!.GetValue<string>());
        Assert.Equal(40,parameters["entityKeys"]![0]![0]!.GetValue<long>());
        Assert.Null(source["entities"]![0]!["candidateEntityHandle"]);
        time=400;Assert.Throws<ReferenceRejected>(()=>sets.DocumentParameters(candidate.Doc));
    }
    [Fact]
    public void CandidateLeasesRejectDuplicatesWrongDocumentAndUnsupportedTypes()
    {
        var source=Snapshot();var store=new CandidateStore(()=>100,()=>Utc);var first=CandidateIds(store.Issue(source,new string('b',64)));var second=CandidateIds(store.Issue(source,new string('b',64)));
        Assert.Throws<ReferenceRejected>(()=>store.PrepareParameters(first.Doc,[first.Entity,first.Entity]));
        Assert.Throws<ReferenceRejected>(()=>store.PrepareParameters(second.Doc,[first.Entity]));
        Assert.Throws<ReferenceRejected>(()=>new CandidateStore(()=>100,()=>Utc).DocumentParameters(first.Doc));
        source["entities"]![0]!["nativeTypeCode"]=54;source["entities"]![0]!["type"]="WORKPLANE";
        var unsupported=store.Issue(source,new string('b',64));var ids=CandidateIds(unsupported);
        Assert.False(unsupported["entities"]![0]!["entitySetSupported"]!.GetValue<bool>());
        Assert.Equal("entity_set_type_unsupported",Assert.Throws<ReferenceRejected>(()=>store.PrepareParameters(ids.Doc,[ids.Entity])).Code);
        Assert.Throws<ReferenceRejected>(()=>store.DocumentParameters(first.Doc));
    }
    [Fact]
    public void DocumentHandlesAreOneUseAndUnsupportedWindowsHaveNoHandle()
    {
        double time=100;var store=new DocumentHandleStore(()=>time,()=>Utc);
        var source=new JsonObject { ["state"]="documents_listed_verified",["bridgeRequestId"]=new string('a',32),["timestampUtc"]="2026-10-12T10:00:00Z",
            ["documents"]=new JsonArray(new JsonObject{["modelId"]=9,["activationSupported"]=true,["windowNumber"]=4},new JsonObject{["modelId"]=10,["activationSupported"]=false}) };
        var result=store.Issue(source,new string('b',64));var handle=result["documents"]![0]!["documentHandle"]!.GetValue<string>();
        Assert.Null(result["documents"]![1]!["documentHandle"]);Assert.Null(source["documents"]![0]!["documentHandle"]);
        Assert.Throws<ReferenceRejected>(()=>new DocumentHandleStore(()=>time,()=>Utc).Consume(handle));
        var parameters=store.Consume(handle);Assert.Equal(4,parameters["windowNumber"]!.GetValue<int>());
        Assert.Equal("2026-10-12T10:05:00+00:00",parameters["expiresUtc"]!.GetValue<string>());
        Assert.Throws<ReferenceRejected>(()=>store.Consume(handle));
        var later=store.Issue(source,new string('b',64));var expired=later["documents"]![0]!["documentHandle"]!.GetValue<string>();time=400;
        Assert.Throws<ReferenceRejected>(()=>store.Consume(expired));
    }
    [Theory]
    [InlineData("2026-10-12T10:00:00.1234567Z","2026-10-12T10:00:00.123456+00:00")]
    [InlineData("2026-10-12T13:00:00+03:00","2026-10-12T13:00:00+03:00")]
    public void LeaseTimeUsesPythonMicrosecondPrecision(string source,string expected)=>Assert.Equal(expected,LeaseTime.Format(LeaseTime.Parse(source,"invalid")));
}

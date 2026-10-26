using System.Text.Json.Nodes;
using ModelContextProtocol;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class OwnedOpenAccessRestoreTests
{
    static Fixture Config(bool enabled=true){var f=new Fixture();foreach(var k in new[]{"workingCopiesExperimental","entitySetsExperimental","documentBindingsExperimental","workingCopyOpenExperimental"})f.Config[k]=true;f.Config["ownedOpenAccessRestoreExperimental"]=enabled;f.SaveConfig();return f;}
    [Theory][InlineData(false,false,true)][InlineData(true,false,false)][InlineData(false,true,false)]
    public void OnlyEnabledFullCatalogContainsObserver(bool readOnly,bool disabled,bool expected){var f=Config(!disabled);using var s=new BridgeSession(f.Options(readOnly));Assert.Equal(expected,ToolCatalog.Create(s).Any(t=>t.ProtocolTool.Name=="restore_verified_owned_open"));Assert.Equal(expected,s.PreparedEnabled("restore_verified_owned_open"));}
    [Theory][InlineData("workingCopiesExperimental")][InlineData("entitySetsExperimental")][InlineData("documentBindingsExperimental")][InlineData("workingCopyOpenExperimental")]
    public void MissingGuardedDependenciesRefused(string dependency){var f=Config();f.Config[dependency]=false;f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
    [Fact]public void CoercedGateRefused(){var f=Config();f.Config["ownedOpenAccessRestoreExperimental"]="true";f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
    [Fact]public async Task UnknownHistoryRefusedBeforeQueue(){var f=Config();using var s=new BridgeSession(f.Options());var reply=await new OwnedOpenAccessRestoreTools(s).Restore(new('a',32),new('b',64),new('c',32),new('d',32),CancellationToken.None);Assert.True(reply.IsError);Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
    [Fact]public async Task ReadOnlyDirectCallRefusedBeforeQueue(){var f=Config();using var s=new BridgeSession(f.Options(true));await Assert.ThrowsAsync<McpProtocolException>(()=>new OwnedOpenAccessRestoreTools(s).Restore(new('a',32),new('b',64),new('c',32),new('d',32),CancellationToken.None));Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
    [Fact]public void UnqualifiedObservationCannotIssueCopyLease(){var copies=new WorkingCopyStore();Assert.Throws<ReferenceRejected>(()=>copies.IssueRestoredOpenAccess(new JsonObject{["state"]="owned_open_access_observed_unqualified",["nativeProviderQualified"]=false}));}
    [Fact]public void RestoreSchemaHasExactOriginalVerifierAndFreshChoices(){var s=OwnedOpenAccessRestoreTools.Schema();Assert.Equal(4,s["properties"]!.AsObject().Count);Assert.Equal("^[0-9a-f]{64}$",s["properties"]!["verificationReceiptSha256"]!["pattern"]!.GetValue<string>());Assert.False(s["additionalProperties"]!.GetValue<bool>());}
}

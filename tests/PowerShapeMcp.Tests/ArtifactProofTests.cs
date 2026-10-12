using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class ArtifactProofTests
{
    [Fact]
    public void PythonIndentedBytesAreStableAndFreezeIsOneUse()
    {
        var fixture=new Fixture();
        using var doc=JsonDocument.Parse("{\"schemaVersion\":1,\"enabled\":false,\"notes\":[\"Тест 😀\",1.0,{},[]]}");
        var value=JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();
        var expected="{\n  \"schemaVersion\": 1,\n  \"enabled\": false,\n  \"notes\": [\n    \"Тест 😀\",\n    1.0,\n    {},\n    []\n  ]\n}";
        var sha=ArtifactProof.Freeze(fixture.Root,"operationplans/"+new string('a',32)+".json",value);
        Assert.Equal(expected,File.ReadAllText(Path.Combine(fixture.Root,"operationplans",new string('a',32)+".json")));
        Assert.Equal(FileArtifacts.Sha256(Encoding.UTF8.GetBytes(expected)),sha);
        Assert.True(JsonNode.DeepEquals(value,ArtifactProof.Verified(fixture.Root,"operationplans",new string('a',32),sha,65536)));
        Assert.Throws<IOException>(()=>ArtifactProof.Freeze(fixture.Root,"operationplans/"+new string('a',32)+".json",value));
    }
    [Fact]
    public void VerifiedArtifactHashesOriginalBytesIncludingBomAndWhitespace()
    {
        var fixture=new Fixture();var id=new string('a',32);
        var bytes=new byte[]{0xef,0xbb,0xbf}.Concat(Encoding.UTF8.GetBytes("{ \"executionEnabled\": false }\r\n")).ToArray();
        Directory.CreateDirectory(Path.Combine(fixture.Root,"operationplans"));
        File.WriteAllBytes(Path.Combine(fixture.Root,"operationplans",id+".json"),bytes);
        var sha=FileArtifacts.Sha256(bytes);
        Assert.False(ArtifactProof.Verified(fixture.Root,"operationplans",id,sha,65536)["executionEnabled"]!.GetValue<bool>());
        Assert.Equal("operation_artifact_changed",Assert.Throws<ReferenceRejected>(()=>ArtifactProof.Verified(fixture.Root,"operationplans",id,new string('b',64),65536)).Code);
        Assert.Throws<InvalidDataException>(()=>ArtifactProof.Verified(fixture.Root,"operationplans",id,sha,1));
    }
}

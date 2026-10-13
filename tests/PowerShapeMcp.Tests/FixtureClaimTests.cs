using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class FixtureClaimTests
{
    [Fact]
    public async Task ExclusiveReadLockDoesNotRepeatClaimOrChangeEnvelope()
    {
        var fixture=new Fixture();fixture.Write("requests/"+Guid.NewGuid().ToString("N")+".json","opaque fake request");
        var request=await fixture.ClaimRequest();var path=request+".processing";var sha=FileArtifacts.Sha256(File.ReadAllBytes(path));
        var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None);
        Task<string> read;
        try{read=fixture.ReadClaimedRequest(request);Assert.False(read.IsCompleted);}
        finally{held.Dispose();}
        Assert.Equal("opaque fake request",await read);Assert.False(File.Exists(request));
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Root,"requests")));Assert.Equal(sha,FileArtifacts.Sha256(File.ReadAllBytes(path)));
    }
}

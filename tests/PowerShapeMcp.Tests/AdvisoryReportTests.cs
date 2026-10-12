using System.Text;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class AdvisoryReportTests
{
    [Fact]
    public void PublisherCanReplaceReportWhileReaderRetainsOriginalCompleteBytes()
    {
        var fixture=new Fixture();fixture.Write("reports/panel-status.json","{\"state\":\"old\"}");
        var path=Path.Combine(fixture.Root,"reports","panel-status.json");
        using var reader=FileArtifacts.OpenAdvisoryReport(fixture.Root,"panel-status.json");
        File.WriteAllText(path+".tmp","{\"state\":\"new\"}");File.Replace(path+".tmp",path,null);
        using var text=new StreamReader(reader,Encoding.UTF8);Assert.Equal("{\"state\":\"old\"}",text.ReadToEnd());
        Assert.Equal("new",FileArtifacts.ReadAdvisoryReport(fixture.Root,"panel-status.json").GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("../bridge.config.json")]
    [InlineData("operationplans/a.json")]
    public void SharedModeCannotBeSelectedForProofsOrConfiguration(string name)
    {
        var fixture=new Fixture();Assert.Throws<InvalidDataException>(()=>FileArtifacts.OpenAdvisoryReport(fixture.Root,name));
    }
}

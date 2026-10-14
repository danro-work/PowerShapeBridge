using PowerShapeGeometry;
using System.Security.Cryptography;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class DgkBundleVerifierTests
{
    private static DgkBundleVerifier.Artifact Primary(string sha="")=>new("sample.dgk","geometry_dgk",true,sha);
    private static DgkBundleVerifier.Plan Plan(params DgkBundleVerifier.Artifact[] extra)=>new("sample",[Primary(),..extra]);
    private static KeyValuePair<string,Stream> File(string name,byte[] bytes)=>new(name,new MemoryStream(bytes));
    private static byte[] Mesh()
    {
        using var output=new MemoryStream();using var writer=new BinaryWriter(output);
        writer.Write("fixture\0"u8.ToArray());writer.Write((ushort)1000);writer.Write(0u);
        writer.Write(1u);writer.Write(3u);writer.Write(1u);writer.Write(0u);writer.Write(3u);writer.Write(1u);
        foreach(var point in new double[]{0,0,0,1,0,0,0,1,0})writer.Write(point);
        writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)2);writer.Write((ushort)1000);
        return output.ToArray();
    }
    [Fact]
    public void PrimaryOnlyIsAByteWitnessAndLeavesTheBorrowedStreamOpen()
    {
        var file=File("sample.dgk",[1,2,3]);var result=DgkBundleVerifier.Verify(Plan(),[file]);
        var primary=Assert.Single(result.Artifacts);Assert.Equal(3,result.TotalBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[]{1,2,3})),primary.Sha256);
        Assert.False(primary.ExpectedHashMatched);Assert.Equal("opaque_bytes_content_unqualified",primary.ContentQualification);
        Assert.False(result.CadGeometryVerified);Assert.False(result.ExportSettingsQualified);
        Assert.False(result.CompanionExecutionAttempted);Assert.True(file.Value.CanRead);
    }
    [Fact]
    public void BothDocumentedDmtNamesAndExplicitFeatureNamesAreSupported()
    {
        var plan=Plan(new DgkBundleVerifier.Artifact("sample.dmt","triangles_dmt",true),new("sample.dgk.dmt","triangles_dmt",true),
            new("explicit_holes.mac","features_mac",true),new("explicit_features.xml","features_xml",true));
        var files=new[]{File("sample.dgk",[11]),File("sample.dmt",Mesh()),File("sample.dgk.dmt",Mesh()),
            File("explicit_holes.mac","malformed MAC is only opaque bytes"u8.ToArray()),
            File("explicit_features.xml","<!DOCTYPE x SYSTEM 'file:///sensitive'><x/>"u8.ToArray())};
        var report=DgkBundleVerifier.Verify(plan,files);Assert.Equal(5,report.Artifacts.Count);
        Assert.All(report.Artifacts.Where(x=>x.Role=="triangles_dmt"),x=>{Assert.Equal(1,x.TriangleCount);Assert.Equal(3,x.VertexCount);Assert.Equal("unknown",x.LengthUnits);});
        Assert.All(report.Artifacts.Where(x=>x.Role.StartsWith("features_")),x=>Assert.Equal("opaque_bytes_content_unqualified",x.ContentQualification));
    }
    [Fact]
    public void MissingOptionalCompanionIsExplicitlyReported()
    {
        var report=DgkBundleVerifier.Verify(Plan(new DgkBundleVerifier.Artifact("sample.dmt","triangles_dmt",false)),[File("sample.dgk",[1])]);
        Assert.Equal("sample.dmt",Assert.Single(report.AbsentOptionalArtifacts));
    }
    [Fact]
    public void ManifestDetachesCallerCollectionAndReportsAreImmutable()
    {
        var list=new List<DgkBundleVerifier.Artifact>{Primary()};var plan=new DgkBundleVerifier.Plan("sample",list);
        list.Clear();Assert.Single(plan.Artifacts);
        Assert.Throws<NotSupportedException>(()=>((IList<DgkBundleVerifier.Artifact>)plan.Artifacts).Clear());
        var report=DgkBundleVerifier.Verify(plan,[File("sample.dgk",[1])]);
        Assert.Throws<NotSupportedException>(()=>((IList<DgkBundleVerifier.ObservedArtifact>)report.Artifacts).Clear());
    }
    [Theory]
    [InlineData("../sample")][InlineData("sample/x")][InlineData("sample\\x")]
    [InlineData("C:sample")][InlineData("sample ")][InlineData(".sample")]
    [InlineData("sample.")][InlineData("CON")][InlineData("lpt1")][InlineData("com0")]
    [InlineData("sample;QUIT")][InlineData("sample\nQUIT")][InlineData("образец")]
    public void AmbiguousPathCommandAndDeviceStemsAreRejected(string stem)=>
        Assert.Throws<InvalidDataException>(()=>new DgkBundleVerifier.Plan(stem,[new(stem+".dgk","geometry_dgk",true)]));
    [Theory]
    [InlineData("sample.dgk","geometry_dgk",false)]
    [InlineData("other.dgk","geometry_dgk",true)]
    [InlineData("other.dmt","triangles_dmt",true)]
    [InlineData("features.exe","features_mac",true)]
    [InlineData("features.mac","features_xml",true)]
    [InlineData("sample.dmt","unknown",true)]
    public void RoleAndCompanionPolicyMustBeExplicit(string name,string role,bool required)=>
        Assert.Throws<InvalidDataException>(()=>new DgkBundleVerifier.Plan("sample",[new(name,role,required)]));
    [Fact]
    public void DuplicateBasenamesEvenWithDifferentCaseAreRefused()=>
        Assert.Throws<InvalidDataException>(()=>Plan(new DgkBundleVerifier.Artifact("sample.dmt","triangles_dmt",true),new("SAMPLE.DMT","triangles_dmt",true)));
    [Fact]
    public void DuplicateFeatureRolesAreNotSilentlyMerged()=>
        Assert.Throws<InvalidDataException>(()=>Plan(new DgkBundleVerifier.Artifact("one.mac","features_mac",true),new("two.mac","features_mac",false)));
    [Fact]
    public void MissingRequiredCompanionRefusesWholeBundle()=>
        Assert.Throws<InvalidDataException>(()=>DgkBundleVerifier.Verify(Plan(new DgkBundleVerifier.Artifact("sample.dmt","triangles_dmt",true)),[File("sample.dgk",[1])]));
    [Fact]
    public void MissingPrimaryRefusesWholeBundle()=>
        Assert.Throws<InvalidDataException>(()=>DgkBundleVerifier.Verify(Plan(),[]));
    [Theory][InlineData("stray.xml")][InlineData("SAMPLE.DGK")][InlineData("../sample.dgk")]
    public void UndeclaredOrDifferentlySpelledFileIsRefused(string name)=>
        Assert.Throws<InvalidDataException>(()=>DgkBundleVerifier.Verify(Plan(),[File("sample.dgk",[1]),File(name,[2])]));
    [Fact]
    public void DuplicateObservedFilesAreRefused()=>
        Assert.Throws<InvalidDataException>(()=>DgkBundleVerifier.Verify(Plan(),[File("sample.dgk",[1]),File("sample.dgk",[1])]));
    [Fact]
    public void ExpectedHashIsIndependentlyMatchedAndTamperingIsRefused()
    {
        var hash=Convert.ToHexStringLower(SHA256.HashData(new byte[]{1}));
        var plan=new DgkBundleVerifier.Plan("sample",[Primary(hash)]);
        Assert.True(Assert.Single(DgkBundleVerifier.Verify(plan,[File("sample.dgk",[1])]).Artifacts).ExpectedHashMatched);
        Assert.Throws<InvalidDataException>(()=>DgkBundleVerifier.Verify(plan,[File("sample.dgk",[2])]));
        Assert.Throws<InvalidDataException>(()=>new DgkBundleVerifier.Plan("sample",[Primary("bad")]));
    }
    [Fact]
    public void InvalidDmtCannotHideBehindItsValidHash()=>
        Assert.Throws<InvalidDataException>(()=>DgkBundleVerifier.Verify(Plan(new DgkBundleVerifier.Artifact("sample.dgk.dmt","triangles_dmt",true)),[File("sample.dgk",[1]),File("sample.dgk.dmt",[1,2,3])]));
    [Fact]
    public void EmptyPrimaryIsNotAValidByteWitness()=>
        Assert.Throws<InvalidDataException>(()=>DgkBundleVerifier.Verify(Plan(),[File("sample.dgk",[])]));
    [Fact]
    public void ClosedStreamIsRefusedBeforeReading()
    {
        var file=File("sample.dgk",[1]);file.Value.Dispose();
        Assert.Throws<InvalidDataException>(()=>DgkBundleVerifier.Verify(Plan(),[file]));
    }
    [Fact]
    public void OneStreamCannotRepresentTwoArtifacts()
    {
        using var stream=new MemoryStream(Mesh());
        Assert.Throws<InvalidDataException>(()=>DgkBundleVerifier.Verify(Plan(new DgkBundleVerifier.Artifact("sample.dmt","triangles_dmt",true)),
            [new("sample.dgk",stream),new("sample.dmt",stream)]));
    }
    private sealed class ChangingStream(byte[] bytes,Action changed):MemoryStream(bytes)
    {
        private bool invoked;
        public override int Read(byte[] buffer,int offset,int count)
        {
            if(!invoked){invoked=true;changed();}return base.Read(buffer,offset,count);
        }
    }
    [Fact]
    public void ChangeToAnEarlierArtifactDuringCompanionReadIsDetected()
    {
        var primaryBytes=new byte[]{1};using var primary=new MemoryStream(primaryBytes);
        using var companion=new ChangingStream("MAC"u8.ToArray(),()=>primaryBytes[0]=2);
        Assert.Throws<InvalidDataException>(()=>DgkBundleVerifier.Verify(Plan(new DgkBundleVerifier.Artifact("holes.mac","features_mac",true)),
            [new("sample.dgk",primary),new("holes.mac",companion)]));
    }
    private sealed class OversizedStream:MemoryStream
    {
        public override long Length=>DgkBundleVerifier.MaxFileBytes+1;
    }
    [Fact]
    public void ByteLimitIsCheckedBeforePayloadAllocation()
    {
        using var stream=new OversizedStream();Assert.Throws<InvalidDataException>(()=>DgkBundleVerifier.Verify(Plan(),[new("sample.dgk",stream)]));
    }
}

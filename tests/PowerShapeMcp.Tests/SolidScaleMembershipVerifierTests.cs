using PowerShapeGeometry;
using Xunit;
using Reading=PowerShapeGeometry.SolidScaleMembershipVerifier.Reading;
using Witness=PowerShapeGeometry.SolidScaleMembershipVerifier.Witness;

namespace PowerShapeMcp.Tests;

public sealed class SolidScaleMembershipVerifierTests
{
    private static readonly string Context=new('a',64),Metadata=new('b',64),Bundle=new('c',64);
    private static Reading Original()=>new(Context,Metadata,Bundle,40,5,"world","mm",1,0.01,new('d',64),new('e',64),600,1000,[0,10,0,10,0,10],[5,5,5],[6,0,1,1,1,1],Enumerable.Range(0,8).Select(i=>new Witness("corner-"+i,"face-"+i,[(i&1)==0?0:10,(i&2)==0?0:10,(i&4)==0?0:10])).ToArray());
    private static Reading Copy(Reading r,double? tolerance=null,string? context=null,string? metadata=null,string? bundle=null,string? units=null,int? type=null,double? area=null,double? volume=null,double[]? bounds=null,double[]? cog=null,int[]? invariants=null,Witness[]? witnesses=null,double? lengthFactor=null,string? areaUnits=null)=>new(context??r.ContextSha256,metadata??r.MetadataSha256,bundle??r.SourceWitnessSha256,type??r.NativeType,r.NativeId,r.Frame,units??r.LengthUnits,lengthFactor??r.LengthFactor,tolerance??r.DocumentToleranceMm,areaUnits??r.AreaUnitIdentitySha256,r.VolumeUnitIdentitySha256,area??r.AreaRaw,volume??r.VolumeRaw,bounds??r.Bounds.ToArray(),cog??r.Cog.ToArray(),invariants??r.Invariants.ToArray(),witnesses??r.Witnesses.ToArray());
    private static Reading Scaled(Reading r,double f,double[] c)=>Copy(r,area:r.AreaRaw*f*f,volume:r.VolumeRaw*f*f*f,bounds:r.Bounds.Select((v,i)=>c[i/2]+f*(v-c[i/2])).ToArray(),cog:r.Cog.Select((v,i)=>c[i]+f*(v-c[i])).ToArray(),witnesses:r.Witnesses.Select(w=>new Witness(w.Key,"new-"+w.FaceObserved,w.Position.Select((v,i)=>c[i]+f*(v-c[i])).ToArray())).ToArray());

    [Theory][InlineData(2)][InlineData(0.5)]
    public void ExplicitPivotScaleCanChangeObservedFacesWithoutClaimingUvCorrespondence(double factor)
    {
        var before=Original();double[] centre=[2,3,4];var after=Scaled(before,factor,centre);
        var result=SolidScaleMembershipVerifier.Verify(before,after,factor,centre);
        Assert.Equal(8,result.ChangedObservedFaceNames);Assert.Equal(0,result.MaximumMembershipCoordinateErrorMm);Assert.Equal(0.01,result.MembershipBudgetMm);
    }
    [Fact]
    public void SeparateDocumentProfileAcceptsSubToleranceMembershipWithoutChangingOldPredicate()
    {
        var before=Original();double[] centre=[2,3,4];var after=Scaled(before,2,centre);var points=after.Witnesses.ToArray();var p=points[4].Position.ToArray();p[0]+=0.000422;points[4]=new(points[4].Key,points[4].FaceObserved,p);
        var result=SolidScaleMembershipVerifier.Verify(before,Copy(after,witnesses:points),2,centre);
        Assert.InRange(result.MaximumMembershipCoordinateErrorMm,0.0004219,0.0004221);Assert.True(result.MaximumMembershipCoordinateErrorMm>5e-6);
        // Legacy UniformScaleSampleVerifier has no tolerance or correspondence changes.
        Assert.Equal("solid_uniform_scale_membership_document_tolerance_v1",SolidScaleMembershipVerifier.Profile);
    }
    [Theory][InlineData("point")][InlineData("cog")][InlineData("bounds")][InlineData("area")][InlineData("volume")][InlineData("invariants")][InlineData("anchors")]
    public void IndependentGeometricAndWitnessFaultsAreRejected(string fault)
    {
        var before=Original();double[] centre=[2,3,4];var after=Scaled(before,2,centre);
        if(fault=="point"){var w=after.Witnesses.ToArray();var p=w[0].Position.ToArray();p[0]+=0.02;w[0]=new(w[0].Key,w[0].FaceObserved,p);after=Copy(after,witnesses:w);}
        if(fault=="cog"){var p=after.Cog.ToArray();p[0]+=0.001;after=Copy(after,cog:p);}
        if(fault=="bounds"){var b=after.Bounds.ToArray();b[0]-=0.001;after=Copy(after,bounds:b);}
        if(fault=="area")after=Copy(after,area:before.AreaRaw*2);
        if(fault=="volume")after=Copy(after,volume:before.VolumeRaw*4);
        if(fault=="invariants")after=Copy(after,invariants:[7,0,1,1,1,1]);
        if(fault=="anchors")after=Copy(after,witnesses:after.Witnesses.Reverse().ToArray());
        Assert.Throws<ArgumentException>(()=>SolidScaleMembershipVerifier.Verify(before,after,2,centre));
    }
    [Theory][InlineData("context")][InlineData("metadata")][InlineData("bundle")][InlineData("tolerance")][InlineData("units")][InlineData("factor")][InlineData("raw_units")][InlineData("type")]
    public void ProofUnitsAndToleranceCannotDrift(string field)
    {
        var before=Original();double[] centre=[2,3,4];var after=Scaled(before,2,centre);
        after=field switch{"context"=>Copy(after,context:new('d',64)),"metadata"=>Copy(after,metadata:new('d',64)),"bundle"=>Copy(after,bundle:new('d',64)),"tolerance"=>Copy(after,tolerance:0.005),"units"=>Copy(after,units:"inch"),"factor"=>Copy(after,lengthFactor:25.4),"raw_units"=>Copy(after,areaUnits:new('f',64)),_=>Copy(after,type:21)};
        Assert.Throws<ArgumentException>(()=>SolidScaleMembershipVerifier.Verify(before,after,2,centre));
    }
    [Fact]
    public void DocumentToleranceIsAThreeDimensionalDistanceBudget()
    {
        var before=Original();double[] centre=[2,3,4];var after=Scaled(before,2,centre);var w=after.Witnesses.ToArray();var p=w[0].Position.Select(v=>v+0.008).ToArray();w[0]=new(w[0].Key,w[0].FaceObserved,p);
        Assert.Throws<ArgumentException>(()=>SolidScaleMembershipVerifier.Verify(before,Copy(after,witnesses:w),2,centre));
    }
    [Fact]
    public void LargeWorldOriginCannotIncreaseAbsoluteMembershipBudget()
    {
        var before=Original();double[] centre=[100000,100000,100000];var after=Scaled(before,2,centre);var w=after.Witnesses.ToArray();var p=w[0].Position.ToArray();p[0]+=0.02;w[0]=new(w[0].Key,w[0].FaceObserved,p);
        Assert.Throws<ArgumentException>(()=>SolidScaleMembershipVerifier.Verify(before,Copy(after,witnesses:w),2,centre));
    }
    [Theory][InlineData(1)][InlineData(-1)][InlineData(101)][InlineData(0.001)]
    public void UnsupportedScaleCannotBeVerified(double f)
    {
        var b=Original();Assert.Throws<ArgumentException>(()=>SolidScaleMembershipVerifier.Verify(b,b,f,[2,3,4]));
    }
    [Fact]
    public void SubToleranceChangeCannotBeCalledAnObservedScale()
    {
        var b=Original();var a=Scaled(b,1.000001,[2,3,4]);Assert.Throws<ArgumentException>(()=>SolidScaleMembershipVerifier.Verify(b,a,1.000001,[2,3,4]));
    }
}

using PowerShapeGeometry;
using Xunit;
using S=PowerShapeGeometry.RotationSampleVerifier.Samples;
using W=PowerShapeGeometry.RotationSampleVerifier.Witness;

namespace PowerShapeMcp.Tests;

public sealed class RotationSampleVerifierTests
{
    private static readonly double[] Axis=[10,20,30,0,0,1];
    private static S Before(int type=40)=>new(type,5,"world","document_raw",new string('a',64),
        [new W("p0",[11,20,30]),new W("p1",[10,22,30]),new W("p2",[10,20,33])],
        [new W("normal0",[1,0,0])],[6,12],[10,11,20,22,30,33]);
    private static S After(int type=40)=>new(type,5,"world","document_raw",new string('a',64),
        [new W("p0",[10,21,30]),new W("p1",[8,20,30]),new W("p2",[10,20,33])],
        [new W("normal0",[0,1,0])],[6,12],[8,10,20,21,30,33]);
    [Theory]
    [InlineData(40)][InlineData(21)][InlineData(59)]
    public void KnownOffOriginQuarterTurnChecksSamplesDirectionsAndPairedBounds(int type)=>RotationSampleVerifier.Verify(Before(type),After(type),90,Axis,1e-6,1e-9);
    [Fact]
    public void OppositeAxisAndNegativeAngleDescribeTheSameMotion()=>RotationSampleVerifier.Verify(Before(),After(),-90,[10,20,30,0,0,-1],1e-6,1e-9);
    [Theory]
    [InlineData("no_effect")][InlineData("wrong_angle")][InlineData("wrong_pivot")]
    [InlineData("direction_translated")][InlineData("invariant")][InlineData("descriptor")]
    [InlineData("sample_order")][InlineData("native_identity")][InlineData("units")]
    [InlineData("nonfinite")][InlineData("bounds")][InlineData("duplicate_anchor")]
    public void WrongOrAmbiguousEvidenceCannotConfirmRigidMotion(string fault)
    {
        var before=Before();var after=After();int angle=90;var axis=Axis.ToArray();
        if(fault=="no_effect")after=before;
        if(fault=="wrong_angle")angle=-90;
        if(fault=="wrong_pivot")axis[0]=0;
        if(fault=="direction_translated")after.Directions[0].Value[0]=10;
        if(fault=="invariant")after.Invariants[0]=7;
        if(fault=="descriptor")after=new(40,5,"world","document_raw",new string('b',64),after.Positions,after.Directions,after.Invariants,after.BoundsPaired);
        if(fault=="sample_order")Array.Reverse(after.Positions);
        if(fault=="native_identity")after=new(40,6,"world","document_raw",new string('a',64),after.Positions,after.Directions,after.Invariants,after.BoundsPaired);
        if(fault=="units")after=new(40,5,"world","mm",new string('a',64),after.Positions,after.Directions,after.Invariants,after.BoundsPaired);
        if(fault=="nonfinite")after.Positions[0].Value[0]=double.NaN;
        if(fault=="bounds")after.BoundsPaired[0]=7;
        if(fault=="duplicate_anchor")after=new(40,5,"world","document_raw",new string('a',64),[after.Positions[0],after.Positions[0]],after.Directions,after.Invariants,after.BoundsPaired);
        Assert.Throws<ArgumentException>(()=>RotationSampleVerifier.Verify(before,after,angle,axis,1e-6,1e-9));
    }
    [Fact]
    public void ArbitraryAngleCircleUsesContainmentInsteadOfFalseExactEnvelopeEquality()
    {
        var root=Math.Sqrt(.5);double[] axis=[0,0,0,0,0,1];
        var before=new S(59,1,"world","document_raw",new string('a',64),[new W("p0",[1,0,0]),new W("p1",[0,1,0])],[],[2*Math.PI],[-1,1,-1,1,0,0]);
        var after=new S(59,1,"world","document_raw",new string('a',64),[new W("p0",[root,root,0]),new W("p1",[-root,root,0])],[],[2*Math.PI],[-1,1,-1,1,0,0]);
        RotationSampleVerifier.Verify(before,after,45,axis,1e-6,1e-9);Assert.False(RotationSampleVerifier.ExactBounds(45,axis));
        Assert.True(RotationSampleVerifier.Envelope(before.BoundsPaired,45,axis)[1]>1);
        after.BoundsPaired[0]=after.BoundsPaired[1]=after.BoundsPaired[2]=after.BoundsPaired[3]=0;
        Assert.Throws<ArgumentException>(()=>RotationSampleVerifier.Verify(before,after,45,axis,1e-6,1e-9));
    }
    [Theory]
    [InlineData(0)][InlineData(2)][InlineData(double.NaN)]
    public void UnboundedOrMissingToleranceCannotAuthorizeVerification(double tolerance)=>Assert.Throws<ArgumentException>(()=>RotationSampleVerifier.Verify(Before(),After(),90,Axis,tolerance,1e-9));
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObliqueQuarterTurnChecksIndependentMatrixSamplesWithoutClaimingExactBox(bool wrongDirection)
    {
        double[] axis=[2,3,4,.6,.8,0];
        var before=new S(40,5,"world","document_raw",new string('a',64),[new W("p0",[3,3,4]),new W("p1",[2,4,4]),new W("p2",[2,3,5])],[new W("n0",[1,0,0])],[1,2],[1,3,2,4,3,5]);
        var after=new S(40,5,"world","document_raw",new string('a',64),[new W("p0",[2.36,3.48,3.2]),new W("p1",[2.48,3.64,4.6]),new W("p2",[2.8,2.4,4])],[new W("n0",[.36,.48,-.8])],[1,2],[1,3,2,4,3,5]);
        Assert.False(RotationSampleVerifier.ExactBounds(90,axis));
        if(wrongDirection){after.Directions[0].Value[2]=.8;Assert.Throws<ArgumentException>(()=>RotationSampleVerifier.Verify(before,after,90,axis,1e-6,1e-9));}
        else RotationSampleVerifier.Verify(before,after,90,axis,1e-6,1e-9);
    }
    [Fact]
    public void FiniteEndpointsCannotTurnOverflowedBoundsSpanIntoInfiniteTolerance()
    {
        var before=Before();var after=After();before.BoundsPaired[0]=-double.MaxValue;before.BoundsPaired[1]=double.MaxValue;
        Assert.Throws<ArgumentException>(()=>RotationSampleVerifier.Verify(before,after,90,Axis,1e-6,1e-9));
    }
}

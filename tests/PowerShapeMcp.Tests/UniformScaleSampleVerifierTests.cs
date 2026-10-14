using PowerShapeGeometry;
using Xunit;
using S=PowerShapeGeometry.UniformScaleSampleVerifier.Samples;
using W=PowerShapeGeometry.UniformScaleSampleVerifier.Witness;
using M=PowerShapeGeometry.UniformScaleSampleVerifier.Metric;

namespace PowerShapeMcp.Tests;

public sealed class UniformScaleSampleVerifierTests
{
    private static readonly double[] Centre=[10,20,30];
    private static Dictionary<string,double> Tolerances()=>new(){["length"]=1e-6,["area"]=1e-6,["volume"]=1e-6,["count"]=1e-6};
    private static S Before(int type=40)=>new(type,5,"world","document_raw",new string('a',64),
        [new W("p0",[11,20,30]),new W("p1",[10,22,33])],
        [new W("normal",[1,0,0],0),new W("tangentDerivative",[2,0,0],1)],
        [new M("length",1,4),new M("area",2,6),new M("volume",3,8),new M("count",0,3)],
        [10,11,20,22,30,33]);
    // Hand-calculated result, independent of the production Transform method.
    private static S Double(int type=40)=>new(type,5,"world","document_raw",new string('a',64),
        [new W("p0",[12,20,30]),new W("p1",[10,24,36])],
        [new W("normal",[1,0,0],0),new W("tangentDerivative",[4,0,0],1)],
        [new M("length",1,8),new M("area",2,24),new M("volume",3,64),new M("count",0,3)],
        [10,12,20,24,30,36]);
    private static S Replace(S source,W[]? positions=null,W[]? directions=null,M[]? metrics=null,double[]? bounds=null,
        string? descriptor=null,int? id=null,string? units=null)=>new(source.Type,id??source.NativeId,source.Frame,
        units??source.Units,descriptor??source.DescriptorSha256,positions??source.Positions.ToArray(),
        directions??source.Directions.ToArray(),metrics??source.Metrics.ToArray(),bounds??source.BoundsPaired.ToArray());
    private static void Check(S before,S after,double factor=2,double[]? centre=null,
        Dictionary<string,double>? tolerances=null,double absolute=1e-6,double relative=1e-9)=>
        UniformScaleSampleVerifier.Verify(before,after,factor,centre??Centre,absolute,relative,tolerances??Tolerances());

    [Theory][InlineData(40)][InlineData(21)][InlineData(59)]
    public void OffOriginDoublingChecksCoordinatesDirectionsMetricsAndBounds(int type)=>Check(Before(type),Double(type));
    [Fact]
    public void ShrinkingChecksSquaredAndCubedMeasures()=>Check(Double(),Before(),.5);
    [Fact]
    public void ExactRelativeToleranceCanBeZero()=>Check(Before(),Double(),relative:0);
    [Theory]
    [InlineData("no_effect")][InlineData("wrong_centre")][InlineData("nonuniform")]
    [InlineData("normal_scaled")][InlineData("derivative_unscaled")][InlineData("direction_translated")]
    [InlineData("area_linear")][InlineData("volume_square")][InlineData("count_scaled")]
    [InlineData("units")][InlineData("identity")][InlineData("descriptor")]
    [InlineData("missing_position")][InlineData("order")][InlineData("duplicate")]
    [InlineData("metric_degree")][InlineData("missing_metric_tolerance")][InlineData("extra_metric_tolerance")]
    [InlineData("nonfinite")][InlineData("bounds")][InlineData("position_outside_bounds")]
    public void IncorrectOrAmbiguousResultIsRejected(string fault)
    {
        var before=Before();var after=Double();var centre=Centre.ToArray();var tolerances=Tolerances();
        if(fault=="no_effect")after=before;
        if(fault=="wrong_centre")centre[0]=0;
        if(fault=="nonuniform")after=Replace(after,positions:[after.Positions[0],new W("p1",[10,22,36])]);
        if(fault=="normal_scaled")after=Replace(after,directions:[new W("normal",[2,0,0],0),after.Directions[1]]);
        if(fault=="derivative_unscaled")after=Replace(after,directions:[after.Directions[0],new W("tangentDerivative",[2,0,0])]);
        if(fault=="direction_translated")after=Replace(after,directions:[after.Directions[0],new W("tangentDerivative",[14,20,30])]);
        if(fault=="area_linear")after=Replace(after,metrics:[after.Metrics[0],new M("area",2,12),after.Metrics[2],after.Metrics[3]]);
        if(fault=="volume_square")after=Replace(after,metrics:[after.Metrics[0],after.Metrics[1],new M("volume",3,32),after.Metrics[3]]);
        if(fault=="count_scaled")after=Replace(after,metrics:[after.Metrics[0],after.Metrics[1],after.Metrics[2],new M("count",0,6)]);
        if(fault=="units")after=Replace(after,units:"mm");
        if(fault=="identity")after=Replace(after,id:6);
        if(fault=="descriptor")after=Replace(after,descriptor:new string('b',64));
        if(fault=="missing_position")after=Replace(after,positions:[]);
        if(fault=="order")after=Replace(after,positions:after.Positions.Reverse().ToArray());
        if(fault=="duplicate")after=Replace(after,positions:[after.Positions[0],after.Positions[0]]);
        if(fault=="metric_degree")after=Replace(after,metrics:[new M("length",0,8),..after.Metrics.Skip(1)]);
        if(fault=="missing_metric_tolerance")tolerances.Remove("area");
        if(fault=="extra_metric_tolerance")tolerances.Add("unused",1e-6);
        if(fault=="nonfinite")after=Replace(after,positions:[new W("p0",[double.NaN,20,30]),after.Positions[1]]);
        if(fault=="bounds")after=Replace(after,bounds:[9,12,20,24,30,36]);
        if(fault=="position_outside_bounds")after=Replace(after,bounds:[10,11,20,24,30,36]);
        Assert.Throws<ArgumentException>(()=>Check(before,after,centre:centre,tolerances:tolerances));
    }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(1)][InlineData(double.NaN)][InlineData(double.PositiveInfinity)]
    public void InvalidNoopOrReflectionFactorCannotConfirmPositiveUniformScaling(double factor)=>
        Assert.Throws<ArgumentException>(()=>Check(Before(),Double(),factor));
    [Fact]
    public void FactorTooCloseToOneCannotConfirmAChangeWithUnchangedEvidence()=>
        Assert.Throws<ArgumentException>(()=>Check(Before(),Before(),1+1e-12));
    [Fact]
    public void VolumePowerOverflowAndUnderflowAreRejected()
    {
        foreach(var factor in new[]{1e200,1e-200})
        {
            var before=new S(40,5,"world","document_raw",new string('a',64),[new W("p0",[0,0,0])],[],[new M("volume",3,1)],[0,0,0,0,0,0]);
            Assert.Throws<ArgumentException>(()=>UniformScaleSampleVerifier.Verify(before,before,factor,[0,0,0],1e-6,0,new Dictionary<string,double>{{"volume",1e-6}}));
        }
    }
    [Fact]
    public void SpatialOverflowCannotTurnToleranceInfinite()=>Assert.Throws<ArgumentException>(()=>
        UniformScaleSampleVerifier.Bounds([-double.MaxValue,double.MaxValue,0,0,0,0],2,[0,0,0]));
    [Fact]
    public void SamplesOwnTheirInputsAndExposeReadOnlyCollections()
    {
        double[] point=[11,20,30];W[] positions=[new W("p",point)];double[] bounds=[10,11,20,22,30,33];
        var sample=new S(40,5,"world","document_raw",new string('a',64),positions,[],[],bounds);
        point[0]=999;positions[0]=new W("replacement",[0,0,0]);bounds[0]=999;
        Assert.Equal(11,sample.Positions[0].Value[0]);Assert.Equal("p",sample.Positions[0].Key);Assert.Equal(10,sample.BoundsPaired[0]);
        Assert.Throws<NotSupportedException>(()=>((IList<double>)sample.Positions[0].Value)[0]=999);
    }
    [Fact]
    public void ZeroMeasuresAreAllowedWithoutProvingTopology()
    {
        var before=Replace(Before(),metrics:[new M("area",2,0)]);var after=Replace(Double(),metrics:[new M("area",2,0)]);
        Check(before,after,tolerances:new(){["area"]=1e-6});
    }
    [Fact]
    public void DimensionlessUnitDirectionsUseTheirOwnTolerance()
    {
        var after=Replace(Double(),directions:[new W("normal",[0,1,0],0),Double().Directions[1]]);
        // Large length tolerance must never permit a 90-degree normal change.
        Assert.Throws<ArgumentException>(()=>Check(Before(),after,absolute:100));
    }
}

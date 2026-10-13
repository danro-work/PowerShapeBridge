using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class RotationGeometryTests
{
    private static void Near(double[] expected,double[] actual)=>Assert.All(expected.Zip(actual),(pair)=>Assert.InRange(Math.Abs(pair.First-pair.Second),0,1e-9));
    [Theory]
    [InlineData(90,0,1)]
    [InlineData(-90,0,-1)]
    [InlineData(450,0,1)]
    public void ExplicitPivotAndRightHandReferenceGiveKnownPositions(int degrees,double x,double y)
    {
        var rotation=new RotationDefinition(degrees,[10,20,30],[0,0,1]);
        Near([10+x,20+y,35],RotationGeometry.RotatePoint(rotation,[11,20,35]));
        Near([10,20,99],RotationGeometry.RotatePoint(rotation,[10,20,99]));
    }
    [Fact]
    public void ReversingDirectionMatchesReversingReferenceAngle()
    {
        var point=new[]{2d,3d,4d};var origin=new[]{1d,-2d,7d};
        Near(RotationGeometry.RotatePoint(new(90,origin,[0,0,-1]),point),RotationGeometry.RotatePoint(new(-90,origin,[0,0,1]),point));
    }
    [Fact]
    public void ObliqueAxisPreservesDistanceAndAxialProjection()
    {
        var axis=new[]{1/Math.Sqrt(3),1/Math.Sqrt(3),1/Math.Sqrt(3)};var rotation=new RotationDefinition(37,[0,0,0],axis);var input=new[]{5d,-2d,8d};var output=RotationGeometry.RotatePoint(rotation,input);
        Assert.InRange(Math.Abs(input.Sum(value=>value*value)-output.Sum(value=>value*value)),0,1e-10);
        Assert.InRange(Math.Abs(input.Zip(axis,(a,b)=>a*b).Sum()-output.Zip(axis,(a,b)=>a*b).Sum()),0,1e-10);
        Assert.False(RotationGeometry.HasExactBoundsMapping(rotation));
    }
    [Fact]
    public void QuarterTurnHasExactAxisAlignedBoundsMapping()
    {
        var rotation=new RotationDefinition(90,[0,0,0],[0,0,1]);
        Near([-3,0,0,0,2,4],RotationGeometry.ConservativeBounds(rotation,[0,0,0,2,3,4]));
        Assert.True(RotationGeometry.HasExactBoundsMapping(rotation));
    }
    [Fact]
    public void RotatedCircleDemonstratesWhyBoxCornersAreNotExactShapeBounds()
    {
        var rotation=new RotationDefinition(45,[0,0,0],[0,0,1]);
        var envelope=RotationGeometry.ConservativeBounds(rotation,[-1,-1,0,1,1,0]);
        // A unit circle about Z still has [-1,+1] XY bounds after any rotation.
        Assert.InRange(envelope[3],Math.Sqrt(2)-1e-12,Math.Sqrt(2)+1e-12);
        Assert.True(envelope[3]>1);Assert.False(RotationGeometry.HasExactBoundsMapping(rotation));
    }
    [Theory]
    [InlineData("bad_axis")]
    [InlineData("bad_point")]
    [InlineData("bad_bounds")]
    public void InvalidGeometryCannotBecomeVerificationEvidence(string fault)
    {
        var rotation=new RotationDefinition(90,[0,0,0],fault=="bad_axis"?[0,0,2]:[0,0,1]);
        if(fault=="bad_bounds")Assert.Throws<ArgumentException>(()=>RotationGeometry.ConservativeBounds(rotation,[2,0,0,1,3,4]));
        else Assert.Throws<ArgumentException>(()=>RotationGeometry.RotatePoint(rotation,fault=="bad_point"?[double.NaN,0,0]:[1,2,3]));
    }
}

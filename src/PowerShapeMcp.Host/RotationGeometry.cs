namespace PowerShapeMcp;

// Reference geometry for future verification. Native sign/frame conventions still need qualification.
public static class RotationGeometry
{
    private static double[] Point(double[] point)
    {
        if(point.Length!=3||point.Any(value=>!double.IsFinite(value)))throw new ArgumentException("Finite 3D point required.");
        return point;
    }
    public static double[] RotatePoint(RotationDefinition definition,double[] point)
    {
        Point(point);Point(definition.Origin);Point(definition.UnitDirection);
        var axis=definition.UnitDirection;var norm=axis.Sum(value=>value*value);
        if(Math.Abs(norm-1)>1e-12)throw new ArgumentException("Unit direction required.");
        var relative=point.Zip(definition.Origin,(coordinate,origin)=>coordinate-origin).ToArray();
        var radians=(definition.AngleDegrees%360)*Math.PI/180;var sine=Math.Sin(radians);var cosine=Math.Cos(radians);
        var dot=relative.Zip(axis,(coordinate,direction)=>coordinate*direction).Sum();
        var cross=new[]{axis[1]*relative[2]-axis[2]*relative[1],axis[2]*relative[0]-axis[0]*relative[2],axis[0]*relative[1]-axis[1]*relative[0]};
        var rotated=Enumerable.Range(0,3).Select(index=>definition.Origin[index]+relative[index]*cosine+cross[index]*sine+axis[index]*dot*(1-cosine)).ToArray();
        if(rotated.Any(value=>!double.IsFinite(value)))throw new ArgumentException("Rotation coordinate overflow.");
        return rotated;
    }
    public static double[] ConservativeBounds(RotationDefinition definition,double[] bounds)
    {
        if(bounds.Length!=6||bounds.Any(value=>!double.IsFinite(value))||Enumerable.Range(0,3).Any(index=>bounds[index]>bounds[index+3]))throw new ArgumentException("Finite minXYZ/maxXYZ bounds required.");
        var corners=Enumerable.Range(0,8).Select(mask=>RotatePoint(definition,Enumerable.Range(0,3).Select(index=>bounds[index+((mask&(1<<index))==0?0:3)]).ToArray())).ToArray();
        return Enumerable.Range(0,3).Select(index=>corners.Min(point=>point[index])).Concat(Enumerable.Range(0,3).Select(index=>corners.Max(point=>point[index]))).ToArray();
    }
    // When the rotation is a signed coordinate permutation, rotating the AABB is exact.
    // For other rotations only containment in ConservativeBounds may be checked.
    public static bool HasExactBoundsMapping(RotationDefinition definition)
    {
        var origin=definition.Origin;var columns=Enumerable.Range(0,3).Select(index=>RotatePoint(definition,Enumerable.Range(0,3).Select(axis=>origin[axis]+(axis==index?1:0)).ToArray()).Zip(origin,(value,pivot)=>value-pivot).ToArray()).ToArray();
        return columns.All(column=>column.Count(value=>Math.Abs(Math.Abs(value)-1)<=1e-10)==1&&column.Count(value=>Math.Abs(value)<=1e-10)==2)&&
               Enumerable.Range(0,3).All(row=>columns.Count(column=>Math.Abs(Math.Abs(column[row])-1)<=1e-10)==1);
    }
}

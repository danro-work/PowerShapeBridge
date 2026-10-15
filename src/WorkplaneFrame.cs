using System;
using System.Linq;

namespace PowerShape.Shared {
 // Autodesk's nine-value layout is origin XYZ, Z vector, Y vector (not X).
 internal static class WorkplaneFrame {
  internal static void Validate(double[] value) {
   if(value==null||value.Length!=9||value.Any(x=>double.IsNaN(x)||double.IsInfinity(x)))throw new ArgumentException("Nine finite world-frame values required.");
   if(value.Take(3).Any(x=>Math.Abs(x)>1000000000))throw new ArgumentException("World origin outside supported +/-1e9 document-raw range.");
   double zz=0,yy=0,yz=0;for(int i=0;i<3;i++){zz+=value[i+3]*value[i+3];yy+=value[i+6]*value[i+6];yz+=value[i+3]*value[i+6];}
   if(Math.Abs(zz-1)>1e-9||Math.Abs(yy-1)>1e-9||Math.Abs(yz)>1e-9)throw new ArgumentException("Z and Y must be orthogonal unit vectors; no implicit normalization.");
  }
  internal static bool Same(double[] expected,double[] actual) {
   Validate(expected);Validate(actual);
   return expected.Zip(actual,(a,b)=>Math.Abs(a-b)<=1e-9+1e-12*Math.Max(Math.Abs(a),Math.Abs(b))).All(x=>x);
  }
 }
}

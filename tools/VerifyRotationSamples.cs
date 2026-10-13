using System;
using PowerShapeGeometry;
using S=PowerShapeGeometry.RotationSampleVerifier.Samples;
using W=PowerShapeGeometry.RotationSampleVerifier.Witness;

// Standalone Framework compiler/runtime check, same shared predicate; no CAD/COM/filesystem fixture.
internal static class VerifyRotationSamples {
 static int Main() {
  try {
   var before=new S(21,5,"world","document_raw",new string('a',64),new[]{new W("p",new double[]{11,20,30})},new[]{new W("n",new double[]{1,0,0})},new double[]{1},new double[]{10,11,20,22,30,33});
   var after=new S(21,5,"world","document_raw",new string('a',64),new[]{new W("p",new double[]{10,21,30})},new[]{new W("n",new double[]{0,1,0})},new double[]{1},new double[]{8,10,20,21,30,33});
   var axis=new double[]{10,20,30,0,0,1};RotationSampleVerifier.Verify(before,after,90,axis,1e-6,1e-9);
   RotationSampleVerifier.Verify(before,after,-90,new double[]{10,20,30,0,0,-1},1e-6,1e-9);
   bool denied=false;try{RotationSampleVerifier.Verify(before,before,90,axis,1e-6,1e-9);}catch(ArgumentException){denied=true;}if(!denied)throw new Exception("No-op claimed verified.");
   Console.WriteLine("PASS: shared rotation witness predicate on .NET Framework; 2 analytic controls/no-op refusal; no CAD.");return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 3;}
 }
}

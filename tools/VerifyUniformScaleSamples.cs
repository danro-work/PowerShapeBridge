using System;
using System.Collections.Generic;
using PowerShapeGeometry;
using S=PowerShapeGeometry.UniformScaleSampleVerifier.Samples;
using W=PowerShapeGeometry.UniformScaleSampleVerifier.Witness;
using M=PowerShapeGeometry.UniformScaleSampleVerifier.Metric;

// Standalone cross-runtime analytic checks. No CAD, queue or MCP registration.
internal static class VerifyUniformScaleSamples {
 static void Denied(Action action){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Incorrect scale evidence accepted.");}
 static S Sample(int type,bool doubled,bool wrongVolume=false) {
  return new S(type,5,"world","document_raw",new string('a',64),
   new[]{new W("p",doubled?new double[]{12,24,36}:new double[]{11,22,33})},
   new[]{new W("normal",new double[]{0,0,1},0),new W("derivative",new double[]{doubled?4:2,0,0})},
   new[]{new M("length",1,doubled?8:4),new M("area",2,doubled?24:6),new M("volume",3,doubled?(wrongVolume?32:64):8)},
   doubled?new double[]{10,12,20,24,30,36}:new double[]{10,11,20,22,30,33});
 }
 static void Check(S before,S after,double factor,double[] centre) {
  UniformScaleSampleVerifier.Verify(before,after,factor,centre,1e-6,1e-9,
   new Dictionary<string,double>{{"length",1e-6},{"area",1e-6},{"volume",1e-6}});
 }
 static int Main() {
  try {
   var centre=new double[]{10,20,30};
   foreach(int type in new[]{40,21,59})Check(Sample(type,false),Sample(type,true),2,centre);
   Check(Sample(40,true),Sample(40,false),.5,centre);
   Denied(()=>Check(Sample(40,false),Sample(40,false),2,centre));
   Denied(()=>Check(Sample(40,false),Sample(40,true,true),2,centre));
   Denied(()=>Check(Sample(40,false),Sample(40,true),2,new double[]{0,0,0}));
   Denied(()=>Check(Sample(40,false),Sample(40,false),1+1e-12,centre));
   Denied(()=>Check(Sample(40,false),Sample(40,true),-2,centre));
   Console.WriteLine("PASS: shared uniform-scale predicate; 4 analytic controls/5 refusals on Framework; no CAD.");return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 3;}
 }
}

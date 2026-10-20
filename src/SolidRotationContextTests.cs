using System;
using System.IO;
using System.Linq;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 internal static class SolidRotationContextTests {
  static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
  internal static int Run(){
   Need(File.Exists(Path.Combine(Program.Root,".solid-rotation-context-fixture")),"Explicit own rotation adapter fixture marker required; no COM.");
   int checks=0;Action<bool> check=ok=>{Need(ok,"Adapter check failed: "+checks);checks++;};
   Action<Action> rejects=action=>{bool failed=false;try{action();}catch(InvalidDataException){failed=true;}catch(IOException){failed=true;}catch(ArgumentException){failed=true;}catch(InvalidOperationException){failed=true;}check(failed);};
   var c=new SolidFaceMeasurementTests.Context();var expected=c.Observation();var d=SolidFaceMeasurements.Capture(expected,5,c);string windows=DocumentBindings.WindowDigest(c.Windows());
   var execution=new SolidRotationContext.ExecutionContext(c,expected,d,windows);var before=execution.Measurements(40);double[] axis={0,0,0,0,0,1};
   check(execution.Rotate(40,90,axis)==1&&c.RotateCalls==1&&c.LastAngle==90&&c.LastAxis.SequenceEqual(axis));
   rejects(()=>execution.Rotate(40,90,axis));check(c.RotateCalls==1);
   using(var fresh=new SolidRotationContext.ReadContext(c,expected,d,windows)){RotationSampleVerifier.Verify(before,fresh.Measurements(40),90,axis,1e-6,1e-9);check(c.ClosestCalls==8&&c.NearCalls==8);rejects(()=>fresh.Composite(40,999));Need(!fresh.IsNativeProvider,"Fake adapter must never gain native qualification/fence authority.");Need(!OwnedRotationVerification.NativeQuarterTurnScope(fresh,new System.Collections.Generic.Dictionary<string,object>{{"angleDegrees",90},{"nativeAxis",axis}},new[]{new[]{40,5}},expected),"Fake samples must not release fences.");}
   check(typeof(SolidRotationContext.ReadContext).GetMethods().All(m=>m.Name!="Rotate"));
   foreach(string fault in new[]{"throw","zero"}){
    var f=new SolidFaceMeasurementTests.Context();var o=f.Observation();var descriptor=SolidFaceMeasurements.Capture(o,5,f);var adapter=new SolidRotationContext.ExecutionContext(f,o,descriptor,DocumentBindings.WindowDigest(f.Windows()));f.Fault=fault;
    if(fault=="throw")rejects(()=>adapter.Rotate(40,90,axis));else check(adapter.Rotate(40,90,axis)==0);
    rejects(()=>adapter.Rotate(40,90,axis));check(f.RotateCalls==1);
   }
   var foreign=new SolidFaceMeasurementTests.Context();var source=foreign.Observation();var desc=SolidFaceMeasurements.Capture(source,5,foreign);var wrong=new SolidRotationContext.ExecutionContext(foreign,source,desc,DocumentBindings.WindowDigest(foreign.Windows()));
   rejects(()=>wrong.Rotate(999,90,axis));rejects(()=>wrong.Rotate(40,90,new double[]{0,0,0,0,0,2}));check(foreign.RotateCalls==0);
   foreign.Snapshot["processId"]=99;rejects(()=>wrong.Rotate(40,90,axis));check(foreign.RotateCalls==0);
   var selected=new SolidFaceMeasurementTests.Context();var selectedSnapshot=selected.Observation();selectedSnapshot["contextBefore"]=selectedSnapshot["contextAfter"]=new[]{1,0};rejects(()=>new SolidRotationContext.ReadContext(selected,selectedSnapshot,desc,DocumentBindings.WindowDigest(selected.Windows())));
   execution.Dispose();rejects(()=>execution.Measurements(40));
   Console.WriteLine("PASS: "+checks+" guarded internal rotation adapter checks; fake callbacks only, no CAD.");return checks;
  }
 }
}

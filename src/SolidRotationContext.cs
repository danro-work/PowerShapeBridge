using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 // Internal adapter for the separately qualified one-SOLID WORLD Z quarter turns.
 // No queue route, MCP tool or independent
 // execution authority. Ownership/checkpoint/journal/claim/fence remain mandatory
 // in OwnedRotationExecution. The retained context is borrowed from its caller.
 internal static class SolidRotationContext {
  internal interface IWriteContext : SolidFaceMeasurements.IContext {
   int Rotate(int composite,int integerDegrees,double[] pointAndUnitDirection);
  }
  static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  internal sealed class ReadContext : OwnedRotationVerification.IContext {
   readonly SolidFaceMeasurements.IContext context;
   readonly Dictionary<string,object> expected,descriptor;
   readonly string windows;readonly int nativeId,compositeId;bool disposed;
   internal ReadContext(SolidFaceMeasurements.IContext context,Dictionary<string,object> expected,Dictionary<string,object> descriptor,string windowContextSha256){
    this.context=context;this.expected=Clone(expected);this.descriptor=Clone(descriptor);windows=windowContextSha256;
    Need(EntitySets.Hex(windows,64),"Pinned window digest required.");
    var keys=EntitySets.Keys(new[]{this.descriptor["entityKey"]});Need(keys.Length==1&&keys[0][0]==40,"Exactly one SOLID descriptor required.");nativeId=keys[0][1];
    Need(EntitySets.Canonical(this.expected["contextBefore"])=="[0,0]"&&EntitySets.Canonical(this.expected["contextAfter"])=="[0,0]"&&EntitySets.Canonical(this.expected["selectionBefore"])=="[]"&&EntitySets.Canonical(this.expected["selectionAfter"])=="[]","Rotation context requires WORLD and empty selection.");
    Need(EntitySets.Inventory(this.expected).Count(e=>Equals(e["nativeTypeCode"],40)&&Equals(e["nativeId"],nativeId))==1,"Descriptor target absent from expected observation.");
    compositeId=context.Composite(40,nativeId);Need(compositeId>0,"Positive explicit composite mapping required.");Guard();
   }
   void Target(int composite){Need(composite==compositeId,"Foreign rotation composite target.");}
   internal bool IsNativeProvider {get{return context is DocumentBindings.ComContext;}}
   internal string PinnedDescriptor {get{return EntitySets.Canonical(descriptor);}}
   void ReadGuard(out Dictionary<string,object> observation,out Dictionary<string,object> windowObservation){
    Need(!disposed,"Rotation read adapter was disposed.");context.ConfirmDocument();
    observation=context.Observation();OwnedRotationExecution.PreserveOthers(expected,observation,new[]{new[]{40,nativeId}});
    windowObservation=context.Windows();Need(DocumentBindings.WindowDigest(windowObservation)==windows&&context.Composite(40,nativeId)==compositeId,"Pinned rotation document/window/mapping changed.");context.ConfirmDocument();
   }
   internal void Guard(){Dictionary<string,object> observation,windowObservation;ReadGuard(out observation,out windowObservation);}
   // Return this call's guarded snapshot. Do not read an unguarded second copy,
   // and never cache either snapshot across separate calls or commands.
   public Dictionary<string,object> Observation(){Dictionary<string,object> observation,windowObservation;ReadGuard(out observation,out windowObservation);return observation;}
   public Dictionary<string,object> Windows(){Dictionary<string,object> observation,windowObservation;ReadGuard(out observation,out windowObservation);return windowObservation;}
   public void ConfirmDocument(){Guard();}
   public int Composite(int type,int id){Need(type==40&&id==nativeId,"Only the pinned SOLID key is accepted.");Guard();return compositeId;}
   public double[] Bounds(int composite){Target(composite);Guard();return context.Bounds(composite);}
   public RotationSampleVerifier.Samples Measurements(int composite){Target(composite);Guard();var result=SolidFaceMeasurements.Read(descriptor,context);Guard();return result;}
   public void Dispose(){disposed=true;}
  }
  internal sealed class ExecutionContext : OwnedRotationExecution.IContext {
   readonly ReadContext reader;readonly IWriteContext writer;bool consumed;
   internal bool IsNativeProvider {get{return reader.IsNativeProvider;}}
   internal ExecutionContext(IWriteContext context,Dictionary<string,object> expected,Dictionary<string,object> descriptor,string windowContextSha256){writer=context;reader=new ReadContext(context,expected,descriptor,windowContextSha256);}
   public Dictionary<string,object> Observation(){return reader.Observation();}
   public Dictionary<string,object> Windows(){return reader.Windows();}
   public void ConfirmDocument(){reader.ConfirmDocument();}
   public int Composite(int type,int id){return reader.Composite(type,id);}
   public double[] Bounds(int composite){return reader.Bounds(composite);}
   public RotationSampleVerifier.Samples Measurements(int composite){return reader.Measurements(composite);}
   public int Rotate(int composite,int angle,double[] axis){
    Need(!consumed,"Rotation callback already consumed; no replay after any return or exception.");
    Need(axis!=null&&axis.Length==6&&axis.All(v=>!double.IsNaN(v)&&!double.IsInfinity(v)&&Math.Abs(v)<=1000000)&&Math.Abs(axis.Skip(3).Sum(v=>v*v)-1)<=1e-12&&Math.Abs((long)angle)<=1000000&&angle%360!=0,"Explicit integer angle and normalized finite WORLD axis required.");
    reader.Bounds(composite);reader.ConfirmDocument();var pinned=axis.ToArray();consumed=true;
    return writer.Rotate(composite,angle,pinned);
   }
   public void Dispose(){reader.Dispose();}
  }
 }
}

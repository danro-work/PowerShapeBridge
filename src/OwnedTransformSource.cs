using System;
using System.Collections.Generic;
using System.Linq;
namespace PowerShapeBridge {
 // Source adapter for the existing single-call lifecycle. No alternate command,
 // ownership policy or fallback: each preparation keeps its original namespace.
 internal sealed class OwnedTransformSource {
  internal readonly Dictionary<string,object> Packet,Parameters,Plan,Binding,Expected;
  internal readonly DateTime Expires;internal readonly int[][] Keys;internal readonly string Digest,PlanIdKey,PlanShaKey,OwnedIdKey,OwnedShaKey,PlanFolder;
  readonly OwnedOperationPlans.Dependencies translation;readonly OwnedRotationPlans.Dependencies rotation;
  internal OwnedTransformSource(string root,Dictionary<string,object> args,OwnedOperationPlans.Proofs proofs,bool surface,bool rotate,bool curve=false){
   PlanIdKey=rotate?"rotationPlanId":"operationPlanId";PlanShaKey=rotate?"rotationPlanSha256":"operationPlanSha256";OwnedIdKey=rotate?"ownedRotationId":"ownedOperationId";OwnedShaKey=rotate?"ownedRotationSha256":"ownedOperationSha256";PlanFolder=rotate?"rotationplans":"operationplans";
   var basic=curve?OwnedCurveTranslation.Validate(args):rotate?OwnedSurfaceRotation.Validate(args):surface?OwnedSurfaceTranslation.Validate(args):args;
   Packet=rotate?OwnedRotationExecution.Packet(root,basic,proofs):OwnedOperationPlans.Packet(root,basic,proofs);Parameters=new[]{PlanIdKey,PlanShaKey,"bindingId","bindingSha256"}.ToDictionary(k=>k,k=>Packet[k]);
   if(rotate){rotation=new OwnedRotationPlans.Dependencies(root,Parameters,proofs);Plan=rotation.Plan;Binding=rotation.Binding;Expected=rotation.Expected;Expires=rotation.Expires;Keys=rotation.Keys;Digest=EntitySets.ObservedDigest(Expected);}
   else {translation=new OwnedOperationPlans.Dependencies(root,Parameters,proofs);Plan=translation.Plan;Binding=translation.Binding;Expected=translation.Expected;Expires=translation.Expires;Keys=translation.Keys;Digest=translation.Digest;}
  }
  internal int Target(bool surface,bool curve=false){return curve?OwnedCurveTranslation.Target(translation):rotation!=null?OwnedSurfaceRotation.Target(this):surface?OwnedSurfaceTranslation.Target(translation):OwnedTranslation.Target(translation);}
  internal void Check(OwnedCopies.OwnershipLease lease,DocumentBindings.IContext c,Dictionary<string,object> result){if(rotation!=null)rotation.Check(lease,c,result);else translation.Check(lease,c,result);}
 }
}

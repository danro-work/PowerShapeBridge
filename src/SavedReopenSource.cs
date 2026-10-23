using System;
using System.Collections.Generic;
using System.IO;

namespace PowerShapeBridge {
 // Explicit source union: legacy successful SaveAs vs a current observation.
 // No manufactured SaveAs receipt; the original proof remains separate.
 internal sealed class SavedReopenSource : IDisposable {
  internal readonly Dictionary<string,object> Verified,Plan,Snapshot,Saved;
  internal readonly FileStream File;
  internal readonly DateTime Expires;
  internal readonly bool Current;
  readonly OwnedSavedSource legacy;
  readonly CurrentSavedResultSource observed;
  internal static bool IsCurrent(Dictionary<string,object> args){return args.ContainsKey("observationRequestId");}
  internal static void Validate(Dictionary<string,object> args,bool allowCurrent=false){
   if(IsCurrent(args)){if(!allowCurrent)throw new InvalidOperationException("Current saved-result sources disabled.");CurrentSavedResultSource.Validate(args);}
   else OwnedSavedSource.Validate(args);
  }
  internal SavedReopenSource(string root,Dictionary<string,object> args,bool historical=false,bool allowCurrent=false){
   Validate(args,allowCurrent);Current=IsCurrent(args);
   try{
    if(Current){observed=new CurrentSavedResultSource(root,args,historical);Verified=observed.Report;Plan=observed.Original.Plan;Snapshot=observed.Snapshot;Saved=observed.Original.Saved;File=observed.Original.File;Expires=observed.Expires;}
    else{legacy=new OwnedSavedSource(root,args,historical);Verified=legacy.Verified;Plan=legacy.Plan;Snapshot=legacy.Snapshot;Saved=legacy.Saved;File=legacy.File;Expires=legacy.Expires;}
   }catch{Dispose();throw;}
  }
  internal void Recheck(){if(Current)observed.Recheck();else legacy.Recheck();}
  public void Dispose(){if(observed!=null)observed.Dispose();if(legacy!=null)legacy.Dispose();}
 }
}

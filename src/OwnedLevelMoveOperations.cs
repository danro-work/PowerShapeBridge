using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedLevelMoveOperations {
  internal static readonly string[] Recipes={"prepare_owned_level_move","execute_owned_level_move","verify_owned_level_move"};
  internal static void Validate(string recipe,Dictionary<string,object> args) {
   if(!Recipes.Contains(recipe))throw new ArgumentException("Unknown owned level move recipe.");
   if(recipe=="verify_owned_level_move")OwnedLevelMoveExecution.ValidateVerification(args);
   else OwnedLevelMovePlans.ValidateArguments(args,recipe=="prepare_owned_level_move");
  }
  internal static Dictionary<string,object> Run(string id,string recipe,Dictionary<string,object> args) {
   var config=Program.ReadConfig();if(!config.IsGeneralCore||!config.ownedLevelMovesExperimental)throw new ArgumentException("Owned level moves disabled.");
   Validate(recipe,args);Directory.CreateDirectory(Path.Combine(Program.Root,"operations"));
   // Helpers own executor.lock; never take it twice here. No second panel/worker.
   using(var context=new DocumentBindings.ComContext(config)) {
    if(recipe=="prepare_owned_level_move")return OwnedLevelMovePlans.Check(Program.Root,id,args,true,context);
    if(recipe=="execute_owned_level_move")return OwnedLevelMoveExecution.Execute(Program.Root,id,args,context);
    return OwnedLevelMoveExecution.Verify(Program.Root,args,context);
   }
  }
 }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedWorkplaneOperations {
  internal static readonly string[] Recipes={"prepare_owned_workplane","execute_owned_workplane","verify_owned_workplane"};
  internal static void Validate(string recipe,Dictionary<string,object> args) {
   if(!Recipes.Contains(recipe))throw new ArgumentException("Unknown owned workplane recipe.");
   if(recipe=="verify_owned_workplane")OwnedTranslation.ValidateVerification(args);
   else OwnedWorkplanePlans.ValidateArguments(args,recipe=="prepare_owned_workplane");
  }
  internal static Dictionary<string,object> Run(string id,string recipe,Dictionary<string,object> args) {
   var config=Program.ReadConfig();if(!config.IsGeneralCore||!config.ownedWorkplanesExperimental)throw new ArgumentException("Owned workplanes disabled.");
   Validate(recipe,args);Directory.CreateDirectory(Path.Combine(Program.Root,"operations"));
   using(var context=new DocumentBindings.ComContext(config)) {
    if(recipe=="prepare_owned_workplane")return OwnedWorkplanePlans.Check(Program.Root,id,args,true,context);
    if(recipe=="execute_owned_workplane")return OwnedWorkplaneExecution.Execute(Program.Root,id,args,context);
    return OwnedWorkplaneExecution.Verify(Program.Root,args,context);
   }
  }
 }
}

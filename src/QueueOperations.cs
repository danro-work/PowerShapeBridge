using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
namespace PowerShapeBridge {
 public static class QueueOperations {
 public static void ValidateRequest(string id,Dictionary<string,object> r) {
  Config c=Program.ReadConfig();
  if(r.Count!=3||!Equals(r["requestId"],id)||!r.ContainsKey("arguments"))throw new ArgumentException("Invalid envelope.");
  var a=r["arguments"] as Dictionary<string,object>;if(a==null)throw new ArgumentException("Invalid arguments.");string op=(string)r["operation"];
  if(op=="recipe.prepare"&&a.ContainsKey("recipeId")&&Equals(a["recipeId"],OwnedScaleRestoration.Recipe)) {
   if(!c.IsGeneralCore||!c.ownedSolidScaleRestoreExperimental||a.Count!=2||!a.ContainsKey("parameters")||!(a["parameters"] is Dictionary<string,object>))throw new ArgumentException("Scale restoration disabled or invalid envelope.");OwnedScaleRestoration.Common((Dictionary<string,object>)a["parameters"]);return;
  }
  if(op=="recipe.prepare"&&a.ContainsKey("recipeId")&&OwnedSavedResultClose.Recipes.Contains(a["recipeId"] as string)) {
   if(!c.IsGeneralCore||!c.ownedSavedResultCloseExperimental||a.Count!=2||!a.ContainsKey("parameters")||!(a["parameters"] is Dictionary<string,object>))throw new ArgumentException("Saved Result close disabled or invalid envelope.");OwnedSavedResultClose.Validate((Dictionary<string,object>)a["parameters"],Equals(a["recipeId"],OwnedSavedResultClose.Recipes[1]));return;
  }
  if(op=="recipe.prepare"&&a.ContainsKey("recipeId")&&OwnedSavedFinalization.Recipes.Contains(a["recipeId"] as string)) {
   if(!c.IsGeneralCore||!c.ownedSavedFinalizationExperimental||a.Count!=2||!a.ContainsKey("parameters")||!(a["parameters"] is Dictionary<string,object>))throw new ArgumentException("Saved Result close disabled or invalid envelope.");OwnedSavedFinalization.Validate((Dictionary<string,object>)a["parameters"],Equals(a["recipeId"],OwnedSavedFinalization.Recipes[1]));return;
  }
  if(op=="recipe.prepare"&&a.ContainsKey("recipeId")&&OwnedSavedCopyClose.Recipes.Contains(a["recipeId"] as string)) {
   if(!c.IsGeneralCore||!c.ownedSavedCopyCloseExperimental||a.Count!=2||!a.ContainsKey("parameters")||!(a["parameters"] is Dictionary<string,object>))throw new ArgumentException("Saved-copy close disabled or invalid envelope.");
   if(Equals(a["recipeId"],OwnedSavedCopyClose.Recipes[0]))OwnedSavedCopyClose.Validate((Dictionary<string,object>)a["parameters"]);else OwnedSavedCopyClose.ValidateVerification((Dictionary<string,object>)a["parameters"]);return;
  }
  if(op=="recipe.prepare"&&a.ContainsKey("recipeId")&&Equals(a["recipeId"],SavedResultObservation.Recipe)) {
   if(!c.IsGeneralCore||!c.ownedSavedResultRestoreExperimental||a.Count!=2||!a.ContainsKey("parameters")||!(a["parameters"] is Dictionary<string,object>))throw new ArgumentException("Saved-result restoration disabled or invalid envelope.");
   SavedResultObservation.Validate((Dictionary<string,object>)a["parameters"]);return;
  }
  if(op=="recipe.prepare"&&a.ContainsKey("recipeId")&&(Equals(a["recipeId"],OwnedOpenAdoption.Recipe)||Equals(a["recipeId"],OwnedOpenAdoption.RefreshRecipe))) {
   if(!c.IsGeneralCore||!c.ownedOpenAdoptionExperimental||a.Count!=2||!a.ContainsKey("parameters")||!(a["parameters"] is Dictionary<string,object>))throw new ArgumentException("Owned OPEN adoption disabled or invalid envelope.");
   OwnedOpenAdoption.Validate((Dictionary<string,object>)a["parameters"],Equals(a["recipeId"],OwnedOpenAdoption.RefreshRecipe));return;
  }
  if(op=="recipe.prepare"&&a.ContainsKey("recipeId")&&OwnedSolidScale.Recipes.Contains(a["recipeId"] as string)) {
   if(!c.IsGeneralCore||!c.ownedSolidScalesExperimental||a.Count!=2||!a.ContainsKey("parameters")||!(a["parameters"] is Dictionary<string,object>))throw new ArgumentException("Owned SOLID scale disabled or invalid envelope.");
   OwnedSolidScale.Validate((string)a["recipeId"],(Dictionary<string,object>)a["parameters"]);return;
  }
  if(op=="recipe.prepare"&&a.ContainsKey("recipeId")&&OwnedSolidQuarterTurn.RotationRecipes.Contains(a["recipeId"] as string)) {
   if(!c.IsGeneralCore||!c.ownedSolidRotationsExperimental||a.Count!=2||!a.ContainsKey("parameters")||!(a["parameters"] is Dictionary<string,object>))throw new ArgumentException("Owned solid rotation disabled or invalid envelope.");
   OwnedSolidQuarterTurn.ValidateRotation((string)a["recipeId"],(Dictionary<string,object>)a["parameters"]);return;
  }
  if(op=="recipe.prepare"&&a.ContainsKey("recipeId")&&OwnedSolidQuarterTurn.Recipes.Contains(a["recipeId"] as string)) {
   if(!c.IsGeneralCore||!c.ownedSolidQuarterTurnsExperimental||a.Count!=2||!a.ContainsKey("parameters")||!(a["parameters"] is Dictionary<string,object>))throw new ArgumentException("Owned solid quarter turn disabled or invalid envelope.");
   OwnedSolidQuarterTurn.Validate((string)a["recipeId"],(Dictionary<string,object>)a["parameters"]);return;
  }
  if(op=="recipe.prepare"&&a.ContainsKey("recipeId")&&OwnedSelection.Recipes.Contains(a["recipeId"] as string)) {
    if(!c.IsGeneralCore||!c.ownedSelectionsExperimental||a.Count!=2||!a.ContainsKey("parameters")||!(a["parameters"] is Dictionary<string,object>))throw new ArgumentException("Owned selection disabled or invalid envelope.");OwnedSelection.Validate((string)a["recipeId"],(Dictionary<string,object>)a["parameters"]);return;
   }
   if(op=="recipe.prepare"&&a.ContainsKey("recipeId")&&OwnedWorkplaneActivation.Recipes.Contains(a["recipeId"] as string)) {
   if(!c.IsGeneralCore||!c.ownedWorkplaneActivationExperimental||a.Count!=2||!a.ContainsKey("parameters")||!(a["parameters"] is Dictionary<string,object>))throw new ArgumentException("Owned activation disabled or invalid envelope.");
   OwnedWorkplaneActivation.Validate((string)a["recipeId"],(Dictionary<string,object>)a["parameters"]);return;
  }
  if(op=="recipe.prepare"&&a.ContainsKey("recipeId")&&OwnedWorkplaneOperations.Recipes.Contains(a["recipeId"] as string)) {
   if(!c.IsGeneralCore||!c.ownedWorkplanesExperimental||a.Count!=2||!a.ContainsKey("parameters")||!(a["parameters"] is Dictionary<string,object>))throw new ArgumentException("Owned workplane disabled or invalid envelope.");
   OwnedWorkplaneOperations.Validate((string)a["recipeId"],(Dictionary<string,object>)a["parameters"]);return;
  }
  if(op=="recipe.prepare"&&a.ContainsKey("recipeId")&&OwnedLevelMoveOperations.Recipes.Contains(a["recipeId"] as string)) {
   if(!c.IsGeneralCore||!c.ownedLevelMovesExperimental||a.Count!=2||!a.ContainsKey("parameters")||!(a["parameters"] is Dictionary<string,object>))throw new ArgumentException("Owned level move disabled or invalid envelope.");
   OwnedLevelMoveOperations.Validate((string)a["recipeId"],(Dictionary<string,object>)a["parameters"]);return;
  }
  if(c.IsGeneralCore&&c.ownedRotationPlansExperimental&&op=="recipe.prepare"&&a.Count==2&&Equals(a.ContainsKey("recipeId")?a["recipeId"]:null,"prepare_owned_rotation")&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>){OwnedRotationPlans.Validate((Dictionary<string,object>)a["parameters"]);return;}
  if(c.IsGeneralCore&&c.ownedUniformScalePlansExperimental&&op=="recipe.prepare"&&a.Count==2&&Equals(a.ContainsKey("recipeId")?a["recipeId"]:null,"prepare_owned_uniform_scale")&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>){OwnedUniformScalePlans.Validate((Dictionary<string,object>)a["parameters"]);return;}
  if(c.IsGeneralCore&&c.ownedDgkTargetsExperimental&&op=="recipe.prepare"&&a.Count==2&&Equals(a.ContainsKey("recipeId")?a["recipeId"]:null,"prepare_owned_dgk_export")&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>){OwnedDgkTargets.Validate((Dictionary<string,object>)a["parameters"]);return;}
   if(c.IsGeneralCore&&c.ownedSavedReopenExperimental&&op=="recipe.prepare"&&a.Count==2&&a.ContainsKey("recipeId")&&Equals(a["recipeId"],"prepare_owned_saved_reopen")&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>){SavedReopenSource.Validate((Dictionary<string,object>)a["parameters"],c.ownedSavedResultRestoreExperimental);return;}
   if(c.IsGeneralCore&&c.checkpointRecoveryExperimental&&op=="recipe.prepare"&&a.Count==2&&a.ContainsKey("recipeId")&&new[]{"prepare_checkpoint_recovery","create_checkpoint_working_copy"}.Contains(a["recipeId"] as string)&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>){CheckpointRecovery.Validate((Dictionary<string,object>)a["parameters"],Equals(a["recipeId"],"create_checkpoint_working_copy"));return;}
   if(c.IsGeneralCore&&c.ownedSavedReopenExperimental&&op=="recipe.prepare"&&a.Count==2&&a.ContainsKey("recipeId")&&a["recipeId"] is string&&new[]{"execute_owned_saved_reopen","verify_owned_saved_reopen"}.Contains((string)a["recipeId"])&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>){if(Equals(a["recipeId"],"execute_owned_saved_reopen"))OwnedSavedReopenExecution.Validate((Dictionary<string,object>)a["parameters"]);else OwnedSavedReopenVerification.Validate((Dictionary<string,object>)a["parameters"]);return;}
   if(c.IsGeneralCore&&c.ownedSaveTargetsExperimental&&op=="recipe.prepare"&&a.Count==2&&a.ContainsKey("recipeId")&&Equals(a["recipeId"],"prepare_owned_save_as")&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>){DocumentBindings.ValidateHandle((Dictionary<string,object>)a["parameters"]);return;}
   if(c.IsGeneralCore&&c.ownedSavesExperimental&&op=="recipe.prepare"&&a.Count==2&&a.ContainsKey("recipeId")&&a["recipeId"] is string&&new[]{"execute_owned_save_as","verify_owned_save_as"}.Contains((string)a["recipeId"])&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>){if(Equals(a["recipeId"],"verify_owned_save_as"))OwnedSaveVerification.Validate((Dictionary<string,object>)a["parameters"]);else OwnedSaveAs.Validate((Dictionary<string,object>)a["parameters"]);return;}
  if(c.IsGeneralCore&&c.ownedTranslationsExperimental&&op=="recipe.prepare"&&a.Count==2&&a.ContainsKey("recipeId")&&a["recipeId"] is string&&new[]{"execute_owned_translation","verify_owned_translation"}.Contains((string)a["recipeId"])&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>){if(Equals(a["recipeId"],"verify_owned_translation"))OwnedTranslation.ValidateVerification((Dictionary<string,object>)a["parameters"]);else OwnedOperationPlans.ValidateParameters((Dictionary<string,object>)a["parameters"],false);return;}
  if(c.IsGeneralCore&&c.ownedOperationPlansExperimental&&op=="recipe.prepare"&&a.Count==2&&a.ContainsKey("recipeId")&&a["recipeId"] is string&&new[]{"prepare_owned_operation","validate_owned_operation"}.Contains((string)a["recipeId"])&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>){OwnedOperationPlans.ValidateParameters((Dictionary<string,object>)a["parameters"],Equals(a["recipeId"],"prepare_owned_operation"));return;}
  if(c.IsGeneralCore&&c.workingCopyOpenExperimental&&op=="recipe.prepare"&&a.Count==2&&Equals(a.ContainsKey("recipeId")?a["recipeId"]:null,"open_working_copy")&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>){WorkingCopyOpen.ValidateParameters((Dictionary<string,object>)a["parameters"]);return;}
  if(c.IsGeneralCore&&c.workingCopyOpenExperimental&&op=="recipe.prepare"&&a.Count==2&&Equals(a.ContainsKey("recipeId")?a["recipeId"]:null,"verify_working_copy_open")&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>){OwnedOpenVerification.ValidateParameters((Dictionary<string,object>)a["parameters"]);return;}
  if(c.IsGeneralCore&&c.documentBindingsExperimental&&op=="recipe.prepare"&&a.Count==2&&a.ContainsKey("recipeId")&&a["recipeId"] is string&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>) {
   string recipe=(string)a["recipeId"];var parameters=(Dictionary<string,object>)a["parameters"];
   if(recipe=="bind_working_document"){DocumentBindings.ValidatePrepare(parameters);return;}
   if(recipe=="validate_working_document_binding"){DocumentBindings.ValidateHandle(parameters);return;}
  }
  if(c.IsGeneralCore&&c.workingCopiesExperimental&&op=="recipe.prepare"&&a.Count==2&&a.ContainsKey("recipeId")&&a["recipeId"] is string&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>) {
   string recipe=(string)a["recipeId"];var parameters=(Dictionary<string,object>)a["parameters"];
   if(recipe=="prepare_working_copy"){OwnedCopies.ValidatePrepare(parameters);return;}
   if(recipe=="inspect_working_copy"){OwnedCopies.ValidateHandle(parameters);return;}
  }
  if(c.IsGeneralCore&&c.entitySetsExperimental&&op=="recipe.prepare"&&a.Count==2&&a.ContainsKey("recipeId")&&a["recipeId"] is string&&a.ContainsKey("parameters")&&a["parameters"] is Dictionary<string,object>) {
   string recipe=(string)a["recipeId"];var parameters=(Dictionary<string,object>)a["parameters"];
   if(recipe=="prepare_entity_set"){EntitySets.ValidatePrepare(parameters);return;}
   if(recipe=="validate_entity_set"){EntitySets.ValidateHandle(parameters);return;}
  }
  if(op=="recipe.prepare"&&a.Count==2&&a.ContainsKey("recipeId")&&Equals(a["recipeId"],"inspect_model_levels")&&a.ContainsKey("parameters")) {
   var parameters=a["parameters"] as Dictionary<string,object>;
   if(!c.IsGeneralCore||!c.levelReadingExperimental||parameters==null||parameters.Count!=0)throw new ArgumentException("Passive level reading disabled or unexpected parameters.");return;
  }
  if(op=="recipe.prepare" && a.Count==2 && a.ContainsKey("recipeId") && a["recipeId"] is string && a.ContainsKey("parameters") && a["parameters"] is Dictionary<string,object> &&
     new[]{"list_documents","inspect_active_model","activate_document"}.Contains((string)a["recipeId"])) {
      if(!c.CoreTransportEnabled)throw new ArgumentException("Core transport disabled.");return;
  }
  if(c.IsGeneralCore || !c.mcpMutationsPanel)throw new ArgumentException("Fixture operations disabled.");
  if(op.StartsWith("recipe.")&&!c.recipeOperationsPanel)throw new ArgumentException("Recipes disabled.");
  if(op=="recipe.prepare"){if(a.Count!=2||!a.ContainsKey("recipeId")||!(a["recipeId"] is string)||!a.ContainsKey("parameters")||!(a["parameters"] is Dictionary<string,object>))throw new ArgumentException("Invalid recipe envelope.");return;}
  if(op=="baseline.activate"){if(a.Count!=0)throw new ArgumentException("No arguments allowed.");return;}
  if(op=="translation.prepare"){PlannedTranslation.ParseInput(Program.Json.Serialize(a));return;}
  Guid g;string digest=a.ContainsKey("planSha256")?a["planSha256"] as string:null;
  if((op!="translation.execute"&&op!="recipe.execute")||a.Count!=2||!a.ContainsKey("operationId")||!(a["operationId"] is string)||!Guid.TryParseExact((string)a["operationId"],"N",out g)||digest==null||digest.Length!=64||digest.Any(x=>!((x>='0'&&x<='9')||(x>='a'&&x<='f'))))throw new ArgumentException("Invalid execution identity.");
 }
 public static Dictionary<string,object> Run(string id) {
 object app=null;
 try {
 Config c=Program.ReadConfig();Guid g;if(!Guid.TryParseExact(id,"N",out g))throw new ArgumentException("Invalid request ID.");
 var request=FileRequests.Parse(id,File.ReadAllText(Path.Combine(Program.Root,"requests",id+".json.processing"),Encoding.UTF8));
 var readArgs=(Dictionary<string,object>)request["arguments"];
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],SavedResultObservation.Recipe))return SavedResultObservation.Run(id,(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&OwnedSolidScale.Recipes.Contains(readArgs["recipeId"] as string))return OwnedSolidScale.Run(id,(string)readArgs["recipeId"],(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&OwnedSolidQuarterTurn.RotationRecipes.Contains(readArgs["recipeId"] as string))return OwnedSolidQuarterTurn.RunRotation(id,(string)readArgs["recipeId"],(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&OwnedSolidQuarterTurn.Recipes.Contains(readArgs["recipeId"] as string))return OwnedSolidQuarterTurn.Run(id,(string)readArgs["recipeId"],(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&OwnedSelection.Recipes.Contains(readArgs["recipeId"] as string))return OwnedSelection.Run(id,(string)readArgs["recipeId"],(Dictionary<string,object>)readArgs["parameters"]);
   if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&OwnedWorkplaneActivation.Recipes.Contains(readArgs["recipeId"] as string))return OwnedWorkplaneActivation.Run(id,(string)readArgs["recipeId"],(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&OwnedWorkplaneOperations.Recipes.Contains(readArgs["recipeId"] as string))return OwnedWorkplaneOperations.Run(id,(string)readArgs["recipeId"],(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&OwnedLevelMoveOperations.Recipes.Contains(readArgs["recipeId"] as string))return OwnedLevelMoveOperations.Run(id,(string)readArgs["recipeId"],(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&new[]{"prepare_checkpoint_recovery","create_checkpoint_working_copy"}.Contains(readArgs["recipeId"] as string))return CheckpointRecovery.Run(id,(Dictionary<string,object>)readArgs["parameters"],Equals(readArgs["recipeId"],"create_checkpoint_working_copy"));
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],"prepare_owned_save_as"))return OwnedSaveTargets.Run(id,(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],"prepare_owned_saved_reopen"))return OwnedSavedReopen.Run(id,(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],OwnedScaleRestoration.Recipe))return OwnedScaleRestoration.Run(id,(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&OwnedSavedResultClose.Recipes.Contains(readArgs["recipeId"] as string))return OwnedSavedResultClose.Run(id,(string)readArgs["recipeId"],(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&OwnedSavedFinalization.Recipes.Contains(readArgs["recipeId"] as string))return OwnedSavedFinalization.Run(id,(string)readArgs["recipeId"],(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&OwnedSavedCopyClose.Recipes.Contains(readArgs["recipeId"] as string))return OwnedSavedCopyClose.Run(id,(string)readArgs["recipeId"],(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],"execute_owned_saved_reopen"))return OwnedSavedReopenExecution.Run(id,(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],"verify_owned_saved_reopen"))return OwnedSavedReopenVerification.Run(id,(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&new[]{"execute_owned_save_as","verify_owned_save_as"}.Contains(readArgs["recipeId"] as string))return OwnedSaveAs.Run(id,(Dictionary<string,object>)readArgs["parameters"],Equals(readArgs["recipeId"],"verify_owned_save_as"));
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&new[]{"execute_owned_translation","verify_owned_translation"}.Contains((string)readArgs["recipeId"]))return OwnedTranslation.Run(id,(Dictionary<string,object>)readArgs["parameters"],Equals(readArgs["recipeId"],"verify_owned_translation"));
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&new[]{"prepare_owned_operation","validate_owned_operation"}.Contains((string)readArgs["recipeId"]))return OwnedOperationPlans.Run(id,(Dictionary<string,object>)readArgs["parameters"],Equals(readArgs["recipeId"],"prepare_owned_operation"));
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],"prepare_owned_rotation"))return OwnedRotationPlans.Run(id,(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],"prepare_owned_uniform_scale"))return OwnedUniformScalePlans.Run(id,(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],"prepare_owned_dgk_export"))return OwnedDgkTargets.Prepare(Program.Root,id,(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],"open_working_copy"))return WorkingCopyOpen.Run(id,(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&(Equals(readArgs["recipeId"],OwnedOpenAdoption.Recipe)||Equals(readArgs["recipeId"],OwnedOpenAdoption.RefreshRecipe)))return OwnedOpenAdoption.Run(id,(Dictionary<string,object>)readArgs["parameters"],Equals(readArgs["recipeId"],OwnedOpenAdoption.RefreshRecipe));
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],"verify_working_copy_open"))return OwnedOpenVerification.Run(id,(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&new[]{"bind_working_document","validate_working_document_binding"}.Contains((string)readArgs["recipeId"]))
  return DocumentBindings.Run(id,(Dictionary<string,object>)readArgs["parameters"],Equals(readArgs["recipeId"],"bind_working_document"));
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&new[]{"prepare_working_copy","inspect_working_copy"}.Contains((string)readArgs["recipeId"]))
  return OwnedCopies.Run(id,(Dictionary<string,object>)readArgs["parameters"],Equals(readArgs["recipeId"],"prepare_working_copy"));
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&new[]{"prepare_entity_set","validate_entity_set"}.Contains((string)readArgs["recipeId"]))
  return EntitySets.Run(id,(Dictionary<string,object>)readArgs["parameters"],Equals(readArgs["recipeId"],"prepare_entity_set"));
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],"list_documents")) {
  if(((Dictionary<string,object>)readArgs["parameters"]).Count!=0)throw new ArgumentException("Document listing takes no parameters.");return DocumentSession.List();
 }
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],"inspect_model_levels")) {
  if(!c.IsGeneralCore||!c.levelReadingExperimental||((Dictionary<string,object>)readArgs["parameters"]).Count!=0)throw new ArgumentException("Passive level reading disabled or unexpected parameters.");var levels=NativeLevelReading.Run();levels["bridgeRequestId"]=id;return levels;
 }
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],"activate_document"))return DocumentSession.Activate(id,(Dictionary<string,object>)readArgs["parameters"]);
 if(Equals(request["operation"],"recipe.prepare")&&readArgs.ContainsKey("recipeId")&&Equals(readArgs["recipeId"],"inspect_active_model"))
 {
     if(((Dictionary<string,object>)readArgs["parameters"]).Count!=0)throw new ArgumentException("Active model inspection takes no parameters.");
     return Probe.Run(false,false);
 }
 TranslationTest.ValidateSandbox(c);
 var qualification=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.GetFullPath(Path.Combine(Program.Root,"..","P2.4","reports","disk-verification-result.json")),Encoding.UTF8));
 if(!Equals(qualification["state"],"succeeded_disk_verified"))throw new InvalidOperationException("Disk qualification missing.");
 var a=(Dictionary<string,object>)request["arguments"];string op=(string)request["operation"];
 if(op=="translation.prepare")return PlannedTranslation.Run(false,PlannedTranslation.ParseInput(Program.Json.Serialize(a)));
 if(op=="translation.execute")return PlannedTranslation.Run(true,null,(string)a["operationId"],(string)a["planSha256"]);
 if(op=="recipe.prepare") {
 var args=(Dictionary<string,object>)a["parameters"];string recipe=(string)a["recipeId"];
 if(recipe=="qualify_read_document"){if(args.Count!=2||!args.ContainsKey("qualificationId")||!args.ContainsKey("phase"))throw new ArgumentException("Invalid read qualification parameters.");return ReadReferenceQualification.Run(id,(string)args["qualificationId"],(string)args["phase"]);}
 if(recipe=="inspect_batch_failure"){if(args.Count!=2||!args.ContainsKey("batchId")||!args.ContainsKey("failedRequestId"))throw new ArgumentException("Invalid failure inspection parameters.");return BatchSectionQualification.InspectFailed((string)args["batchId"],(string)args["failedRequestId"]);}
 if(recipe=="cleanup_batch_precreation_failure"){if(args.Count!=2||!args.ContainsKey("batchId")||!args.ContainsKey("failedRequestId"))throw new ArgumentException("Invalid precreation cleanup parameters.");return BatchSectionQualification.CleanupBeforePlanes((string)args["batchId"],(string)args["failedRequestId"]);}
 if(recipe=="qualify_batch_section_phase"){if(args.Count!=4||!args.ContainsKey("batchId")||!args.ContainsKey("sourceJobId")||!args.ContainsKey("sourceReceiptSha256")||!args.ContainsKey("phase"))throw new ArgumentException("Invalid batch qualification parameters.");return BatchSectionQualification.Run(id,(string)args["batchId"],(string)args["sourceJobId"],(string)args["sourceReceiptSha256"],(string)args["phase"]);}
 if(recipe=="inspect_surface_recovery"){if(args.Count!=2||!args.ContainsKey("jobId")||!args.ContainsKey("sourceRequestId"))throw new ArgumentException("Invalid recovery inspection.");return SurfaceSectionJob.InspectRecovery((string)args["jobId"],(string)args["sourceRequestId"]);}
 if(recipe=="test_surface_section_phase"){if(args.Count!=4||!args.ContainsKey("jobId")||!args.ContainsKey("planSha256")||!args.ContainsKey("phase")||!args.ContainsKey("index"))throw new ArgumentException("Invalid surface section phase.");return SurfaceSectionJob.Run(id,(string)args["jobId"],(string)args["planSha256"],(string)args["phase"],Program.StrictInt(args["index"]));}
 if(recipe=="test_contour_group_phase"){if(args.Count!=4||!args.ContainsKey("groupId")||!args.ContainsKey("planSha256")||!args.ContainsKey("phase")||!args.ContainsKey("index"))throw new ArgumentException("Invalid group phase.");return GroupedExport.Run(id,(string)args["groupId"],(string)args["planSha256"],(string)args["phase"],Program.StrictInt(args["index"]));}
 if(recipe=="qualify_test_dgk_roundtrip"){if(args.Count!=0)throw new ArgumentException("Qualification takes no parameters.");return ExchangeQualification.Run(id);}
 if(recipe=="verify_test_dgk_saved_copy"){if(args.Count!=2||!args.ContainsKey("qualificationId")||!args.ContainsKey("receiptSha256"))throw new ArgumentException("Invalid exchange verification parameters.");return ExchangeQualification.VerifySaved(id,(string)args["qualificationId"],(string)args["receiptSha256"]);}
 if(recipe=="cleanup_failed_test_dgk_source"){if(args.Count!=1||!args.ContainsKey("qualificationId"))throw new ArgumentException("Invalid cleanup parameters.");return ExchangeQualification.CleanupFailed(id,(string)args["qualificationId"]);}
 if(recipe=="qualify_test_section_z10"){if(args.Count!=0)throw new ArgumentException("Section qualification takes no parameters.");return SectionQualification.Run(id);}
 if(recipe=="qualify_test_section_z0"){if(args.Count!=0)throw new ArgumentException("Zero section qualification takes no parameters.");return SectionQualification.Run(id,0);}
 if(recipe=="slice_test_cube_xy")return PlannedTranslation.Run(false,null,null,null,null,recipe,null,null,null,SliceRecipes.Parse(args));
 if(recipe=="verify_existing_slice_batch"){if(args.Count!=2||!args.ContainsKey("sourceOperationId")||!args.ContainsKey("sourceReceiptSha256"))throw new ArgumentException("Invalid existing batch verification identity.");return SliceRecipes.VerifyExisting(id,(string)args["sourceOperationId"],(string)args["sourceReceiptSha256"]);}
 if(recipe=="convert_test_solid_to_surfaces"||recipe=="offset_test_top_surface_1mm"){if(args.Count!=0)throw new ArgumentException("No conversion parameters allowed.");return PlannedTranslation.Run(false,null,null,null,null,recipe);}
 if(recipe=="create_workplane_world")return PlannedTranslation.Run(false,null,null,null,null,recipe,null,PlannedTranslation.ParseWorkplane(args));
 if(recipe=="scale_uniform_world"){if(args.Count!=1||!args.ContainsKey("scaleFactor"))throw new ArgumentException("Only scaleFactor allowed.");double f=Program.StrictDouble(args["scaleFactor"]);PlannedTranslation.ValidateScale(f);return PlannedTranslation.Run(false,null,null,null,null,recipe,null,null,f);}
 if(recipe=="assign_test_level"){if(args.Count!=1||!args.ContainsKey("levelNumber"))throw new ArgumentException("Only levelNumber allowed.");int level=Program.StrictInt(args["levelNumber"]);PlannedTranslation.ValidateTestLevel(level);return PlannedTranslation.Run(false,null,null,null,null,recipe,level);}
 if(recipe=="inspect_failed_conversion"){if(args.Count!=1||!args.ContainsKey("operationId"))throw new ArgumentException("Invalid inspection.");return SurfaceRecipes.InspectFailed((string)args["operationId"]);}
 if(recipe!="rotate_z"||args.Count!=1||!args.ContainsKey("angleDegrees"))throw new ArgumentException("Unregistered recipe.");int angle=Program.StrictInt(args["angleDegrees"]);PlannedTranslation.ValidateAngle(angle);return PlannedTranslation.Run(false,null,null,null,angle,recipe);
 }
 if(op=="recipe.execute") {
 var plan=Program.Json.Deserialize<PlannedTranslation.Plan>(File.ReadAllText(Path.Combine(Program.Root,"plans",(string)a["operationId"]+".json"),Encoding.UTF8));
 if(plan.recipeId=="convert_test_solid_to_surfaces"||plan.recipeId=="offset_test_top_surface_1mm")return SurfaceRecipes.Execute((string)a["operationId"],(string)a["planSha256"]);
 if(plan.recipeId=="slice_test_cube_xy")return SliceRecipes.Execute((string)a["operationId"],(string)a["planSha256"]);
 if(!new[]{"rotate_z","assign_test_level","create_workplane_world","scale_uniform_world"}.Contains(plan.recipeId))throw new ArgumentException("Unregistered executor.");
 return PlannedTranslation.Run(true,null,(string)a["operationId"],(string)a["planSha256"],null,plan.recipeId);
 }
 using(var guard=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
 app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;int pid=Program.StrictInt(ps.Evaluate("app.pid"));
 if(!Process.GetProcessesByName("powershape").Any(x=>x.Id==pid&&x.SessionId==Process.GetCurrentProcess().SessionId)||Convert.ToBoolean(ps.Busy()))throw new InvalidOperationException("Unexpected/busy instance.");
 string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});if(version!=c.expectedVersion)throw new InvalidOperationException("Version mismatch.");
 string target=Path.GetFullPath(c.expectedProject);
 bool active=Convert.ToBoolean(ps.IsDocumentActive());
 if(!active||!CheckpointTest.ModelPath(ps).Equals(target,StringComparison.OrdinalIgnoreCase)) {
 int count=Program.StrictInt(ps.Evaluate("WINDOW.NUMBER")),seen=0,found=0;if(count<0||count>256)throw new InvalidOperationException("Window inventory limit.");
 for(int n=1;n<=1024&&seen<count;n++) {
 if(Program.StrictInt(ps.Evaluate("WINDOW["+n+"].EXISTS"))!=1)continue;seen++;
 if(!string.Equals(Convert.ToString(ps.Evaluate("WINDOW["+n+"].TYPE")),"MODEL",StringComparison.OrdinalIgnoreCase))continue;
 string name=Convert.ToString(ps.Evaluate("WINDOW["+n+"].MODEL"));if(string.IsNullOrWhiteSpace(name))continue;if(name.IndexOfAny(new[]{'\'','\r','\n'})>=0)throw new InvalidOperationException("Invalid model identity.");
 int model=Program.StrictInt(ps.Evaluate("MODEL['"+name+"'].ID"));string path=Convert.ToString(ps.Evaluate("MODEL[ID "+model+"].PATH"));
 if(string.IsNullOrWhiteSpace(path)||!Path.GetFullPath(path).Equals(target,StringComparison.OrdinalIgnoreCase))continue;if(found!=0)throw new InvalidOperationException("Ambiguous baseline.");found=n;
 }
 if(seen!=count)throw new InvalidOperationException("Incomplete window inventory.");
 if(found!=0)ps.Exec("Sel_Window @"+found+" Front");else {CheckpointTest.ValidatePath(target);ps.Exec("FILE OPENPATH");ps.Exec("PATHNAME "+target);ps.Exec("ACCEPT");}
 bool ready=false;for(int n=0;n<30;n++){if(Convert.ToBoolean(ps.IsDocumentActive())&&CheckpointTest.ModelPath(ps).Equals(target,StringComparison.OrdinalIgnoreCase)){ready=true;break;}System.Threading.Thread.Sleep(100);}if(!ready)throw new InvalidOperationException("Baseline did not activate.");
 }
 var measure=CheckpointTest.Measure(ps);TranslationTest.ValidateSandbox(c);if(!TranslationTest.Same(measure.boundsWorldMm,new[]{-10.0,10.0,-10.0,10.0,0.0,20.0}))throw new InvalidOperationException("Baseline geometry differs.");
 return new Dictionary<string,object>{{"state","baseline_active"},{"geometryMutationAttempted",false},{"activePath",target},{"measurement",measure}};
 }
 }catch(Exception ex){return new Dictionary<string,object>{{"state","rejected_or_stopped"},{"error",ex.Message},{"warning","Context may have changed; no automatic retry of editing operations."}};}
 finally{if(app!=null&&Marshal.IsComObject(app))Marshal.FinalReleaseComObject(app);}
 }
 }
}

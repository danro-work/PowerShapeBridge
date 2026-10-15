using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PowerShapeBridge
{
    public sealed class Config
    {
        public string expectedVersion { get; set; }
        public string expectedProject { get; set; }
        public string sourceStep { get; set; }
        public int timeoutSeconds { get; set; }
        public int operationTimeoutSeconds { get; set; }
        public int maxEntities { get; set; }
        public bool captureViewport { get; set; }
        public bool inspectUnitFactors { get; set; }
        public bool inspectSelectionBounds { get; set; }
        public bool inspectSelectionSignature { get; set; }
        public bool translationTestPanel { get; set; }
        public bool checkpointTestPanel { get; set; }
        public bool parameterizedTranslationPanel { get; set; }
        public bool diskVerificationPanel { get; set; }
        public bool mcpMutationsPanel { get; set; }
        public bool recipeOperationsPanel { get; set; }
        public string coreProfile { get; set; }
        public bool entitySetsExperimental { get; set; }
        public bool operationPlansExperimental { get; set; }
        public bool workingCopiesExperimental { get; set; }
        public bool documentBindingsExperimental { get; set; }
        public bool workingCopyOpenExperimental { get; set; }
        public bool ownedOperationPlansExperimental { get; set; }
        public bool ownedTranslationsExperimental { get; set; }
        public bool ownedSaveTargetsExperimental { get; set; }
        public bool ownedSavesExperimental { get; set; }
        public bool ownedSavedReopenExperimental { get; set; }
        public bool checkpointRecoveryExperimental { get; set; }
        public bool ownedRotationPlansExperimental { get; set; }
        public bool ownedUniformScalePlansExperimental { get; set; }
        public bool ownedDgkTargetsExperimental { get; set; }
        public bool levelReadingExperimental { get; set; }
        public bool IsGeneralCore { get { return coreProfile == "general-v1"; } }
        public bool CoreTransportEnabled { get { return IsGeneralCore || (mcpMutationsPanel && recipeOperationsPanel); } }
        public string qualificationOperationId { get; set; }
        public string qualificationReceiptSha256 { get; set; }
        public string qualificationReceiptRelease { get; set; }
        public string protectedProject { get; set; }
        public string protectedProjectSha256 { get; set; }
        public string testProjectSha256 { get; set; }
        public void Validate()
        {
            if (expectedVersion != "23.2.2") throw new ArgumentException("Unsupported target version.");
            if (coreProfile != null && !IsGeneralCore) throw new ArgumentException("Unknown core profile.");
            if(entitySetsExperimental&&!IsGeneralCore)throw new ArgumentException("Entity sets require general core.");
            if(operationPlansExperimental&&(!IsGeneralCore||!entitySetsExperimental))throw new ArgumentException("Operation plans require general core and entity sets.");
            if(workingCopiesExperimental&&!IsGeneralCore)throw new ArgumentException("Working copies require general core.");
            if(documentBindingsExperimental&&(!IsGeneralCore||!workingCopiesExperimental||!entitySetsExperimental))throw new ArgumentException("Document bindings require general core, working copies and entity candidates.");
            if(workingCopyOpenExperimental&&(!IsGeneralCore||!workingCopiesExperimental||!entitySetsExperimental||!documentBindingsExperimental))throw new ArgumentException("Owned open requires general core, working copies, candidates and bindings.");
            if(ownedOperationPlansExperimental&&(!IsGeneralCore||!operationPlansExperimental||!entitySetsExperimental||!workingCopiesExperimental||!documentBindingsExperimental))throw new ArgumentException("Coupled plans require general core, plans, sets, copies and bindings.");
            if(ownedTranslationsExperimental&&(!IsGeneralCore||!ownedOperationPlansExperimental))throw new ArgumentException("Owned translations require general coupled plans.");
            if(ownedSaveTargetsExperimental&&(!IsGeneralCore||!documentBindingsExperimental||!workingCopiesExperimental||!entitySetsExperimental))throw new ArgumentException("Owned SaveAs targets require general copies, candidates and bindings.");
            if(ownedSavesExperimental&&(!IsGeneralCore||!ownedSaveTargetsExperimental))throw new ArgumentException("Owned SaveAs execution requires prepared owned targets.");
            if(ownedSavedReopenExperimental&&(!IsGeneralCore||!ownedSavesExperimental||!workingCopyOpenExperimental))throw new ArgumentException("Saved reopen requires owned SaveAs and owned copy open.");
            if(checkpointRecoveryExperimental&&(!IsGeneralCore||!workingCopiesExperimental))throw new ArgumentException("Checkpoint recovery requires general owned working copies.");
            if(ownedRotationPlansExperimental&&(!IsGeneralCore||!entitySetsExperimental||!workingCopiesExperimental||!documentBindingsExperimental))throw new ArgumentException("Owned rotation preparation requires general sets, copies and bindings.");
            if(ownedUniformScalePlansExperimental&&(!IsGeneralCore||!entitySetsExperimental||!workingCopiesExperimental||!documentBindingsExperimental))throw new ArgumentException("Owned uniform scale preparation requires general sets, copies and bindings.");
            if(ownedDgkTargetsExperimental&&(!IsGeneralCore||!entitySetsExperimental||!workingCopiesExperimental||!documentBindingsExperimental))throw new ArgumentException("Owned DGK targets require general sets, copies and bindings.");
            if(levelReadingExperimental&&!IsGeneralCore)throw new ArgumentException("Passive level reading requires general core.");
            if (IsGeneralCore) {
                if (captureViewport || translationTestPanel || checkpointTestPanel || parameterizedTranslationPanel || diskVerificationPanel || mcpMutationsPanel || recipeOperationsPanel ||
                    !string.IsNullOrEmpty(expectedProject) || !string.IsNullOrEmpty(sourceStep) || !string.IsNullOrEmpty(protectedProject) || !string.IsNullOrEmpty(qualificationOperationId) ||
                    !string.IsNullOrEmpty(qualificationReceiptSha256) || !string.IsNullOrEmpty(qualificationReceiptRelease) || !string.IsNullOrEmpty(testProjectSha256) || !string.IsNullOrEmpty(protectedProjectSha256))
                    throw new ArgumentException("General core must not enable fixture operations, captures or fixture paths.");
            } else {
                if (!Path.IsPathRooted(expectedProject) || !File.Exists(expectedProject)) throw new ArgumentException("Project file is missing.");
                if (!Path.IsPathRooted(sourceStep) || !File.Exists(sourceStep)) throw new ArgumentException("STEP file is missing.");
            }
            if (timeoutSeconds < 1 || timeoutSeconds > 120 || maxEntities < 1 || maxEntities > 100000)
                throw new ArgumentException("Invalid limits.");
            if (operationTimeoutSeconds != 0 && (operationTimeoutSeconds < 1 || operationTimeoutSeconds > 110))
                throw new ArgumentException("Invalid operation timeout.");
        }
    }

    public static class Program
    {
        public const string ReleaseVersion = "0.34.6-dev";
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16000000 };
        public static string Root { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        public static Config ReadConfig()
        {
            return ParseConfig(File.ReadAllText(Path.Combine(Root, "bridge.config.json"), Encoding.UTF8));
        }
        internal static Config ParseConfig(string text)
        {
            var raw=Json.Deserialize<Dictionary<string,object>>(text);object enabled;
            if(raw!=null&&raw.TryGetValue("levelReadingExperimental",out enabled)) {
                if(!(enabled is bool))throw new ArgumentException("Level reading flag must be JSON boolean.");
                if((bool)enabled&&(!raw.ContainsKey("coreProfile")||!Equals(raw["coreProfile"],"general-v1")))throw new ArgumentException("Level reading requires general core.");
            }
            if(raw!=null&&raw.TryGetValue("ownedDgkTargetsExperimental",out enabled)) {
                if(!(enabled is bool))throw new ArgumentException("DGK target flag must be JSON boolean.");
                if((bool)enabled)foreach(string flag in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"}) {
                    object value;if(!raw.TryGetValue(flag,out value)||!(value is bool)||!(bool)value)throw new ArgumentException("DGK target dependencies must be true JSON booleans.");
                }
            }
            if(raw!=null&&raw.TryGetValue("ownedUniformScalePlansExperimental",out enabled)) {
                if(!(enabled is bool))throw new ArgumentException("Owned uniform scale flag must be JSON boolean.");
                if((bool)enabled)foreach(string flag in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"}) {
                    object value;if(!raw.TryGetValue(flag,out value)||!(value is bool)||!(bool)value)throw new ArgumentException("Owned uniform scale dependencies must be true JSON booleans.");
                }
            }
            Config c = Json.Deserialize<Config>(text);
            if (c == null) throw new ArgumentException("Empty configuration.");
            c.Validate(); return c;
        }
        [STAThread]
        public static int Main(string[] args)
        {
            // GUI applications launched from Explorer have no console handle.
            // Configure console encoding only in worker/CLI modes.
            bool cli = (args.Length==2&&new[]{"--mcp-operation","--checkpoint-wire-seed","--checkpoint-wire-request"}.Contains(args[0])) || (args.Length == 1 && (args[0] == "--component-info" || args[0] == "--probe" || args[0] == "--self-test" || args[0] == "--recovery-fault-test" || args[0] == "--activation-fault-test" || args[0] == "--checkpoint-recovery-test" || args[0] == "--checkpoint-recovered-open-test" || args[0] == "--translation-test" || args[0] == "--checkpoint-test" || args[0] == "--prepare-translation" || args[0] == "--execute-translation" || args[0] == "--verify-disk"));
            if (cli) Console.OutputEncoding = new UTF8Encoding(false);
            if(args.Length==1&&args[0]=="--component-info") {
                try {
                    Config config=ReadConfig();Console.WriteLine(Json.Serialize(new{state="component_configuration_validated",version=ReleaseVersion,configurationProfile=config.IsGeneralCore?"general-v1":"legacy_fixture",entitySetsExperimental=config.entitySetsExperimental,operationPlansExperimental=config.operationPlansExperimental,workingCopiesExperimental=config.workingCopiesExperimental,documentBindingsExperimental=config.documentBindingsExperimental,workingCopyOpenExperimental=config.workingCopyOpenExperimental,ownedOperationPlansExperimental=config.ownedOperationPlansExperimental,ownedTranslationsExperimental=config.ownedTranslationsExperimental,configurationSha256=Hash(Path.Combine(Root,"bridge.config.json")),powerShapeConnectionAttempted=false}));return 0;
                }catch(Exception error){Console.WriteLine(Json.Serialize(new{state="configuration_rejected",error=error.Message,powerShapeConnectionAttempted=false}));return 2;}
            }
            if(args.Length==1&&args[0]=="--recovery-fault-test") {
                try{RecoveryFaultTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.Message);return 3;}
            }
            if(args.Length==1&&args[0]=="--activation-fault-test") {
                try{ActivationFaultTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.Message);return 3;}
            }
            if(args.Length==1&&args[0]=="--checkpoint-recovery-test") {
                try{CheckpointRecoveryTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.Message);return 3;}
            }
            if(args.Length==1&&args[0]=="--checkpoint-recovered-open-test") {
                try{CheckpointRecoveredOpenTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.Message);return 3;}
            }
            if(args.Length==1&&args[0]=="--native-selection-reading-test") {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{NativeSelectionReadingTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==1&&args[0]=="--native-levels-reading-test") {
                try{NativeLevelReadingTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==1&&args[0]=="--owned-level-move-plan-test") {
                try { OwnedLevelMovePlanTests.Run(); return 0; }
                catch(Exception error) { Console.WriteLine("FAIL: " + error.Message); return 3; }
            }
            if(args.Length==1&&args[0]=="--owned-level-move-execution-test") {
                try { OwnedLevelMoveExecutionTests.Run(); return 0; }
                catch(Exception error) { Console.Error.WriteLine(error.ToString()); return 3; }
            }
            if(args.Length==1&&args[0]=="--geometry-export-verification-test") {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{OwnedGeometryExportVerificationTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==1&&args[0]=="--geometry-export-executor-test") {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{OwnedGeometryExportTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==1&&args[0]=="--geometry-export-target-test") {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{OwnedGeometryExportTargetTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==1&&args[0]=="--owned-rotation-plan-test") {
                try{OwnedRotationPlanTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==1&&args[0]=="--dgk-bundle-files-test") {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{DgkBundleFilesTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==1&&args[0]=="--owned-dgk-target-test") {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{OwnedDgkTargetTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==1&&args[0]=="--owned-dgk-export-test") {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{OwnedDgkExportTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==1&&args[0]=="--owned-dgk-verification-test") {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{OwnedDgkExportVerificationTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==1&&args[0]=="--owned-uniform-scale-plan-test") {
                try{OwnedUniformScalePlanTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==1&&args[0]=="--owned-rotation-execution-test") {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{OwnedRotationExecutionTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==1&&args[0]=="--rotation-measurement-test") {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{RotationMeasurementTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==1&&args[0]=="--rotation-measurement-record-test") {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{RotationMeasurementRecordTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==1&&args[0]=="--rotation-verification-test") {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{OwnedRotationVerificationTests.Run();return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==2&&new[]{"--rotation-wire-seed","--rotation-wire-request"}.Contains(args[0])) {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{Console.WriteLine(Json.Serialize(args[0]=="--rotation-wire-seed"?OwnedRotationWireFixture.Seed(args[1]):OwnedRotationWireFixture.Run(args[1])));return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==2&&new[]{"--uniform-scale-wire-seed","--uniform-scale-wire-request"}.Contains(args[0])) {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{Console.WriteLine(Json.Serialize(args[0]=="--uniform-scale-wire-seed"?OwnedUniformScaleWireFixture.Seed(args[1]):OwnedUniformScaleWireFixture.Run(args[1])));return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==2&&new[]{"--dgk-target-wire-seed","--dgk-target-wire-request"}.Contains(args[0])) {
                Console.OutputEncoding=new UTF8Encoding(false);
                try{Console.WriteLine(Json.Serialize(args[0]=="--dgk-target-wire-seed"?OwnedDgkTargetWireFixture.Seed(args[1]):OwnedDgkTargetWireFixture.Run(args[1])));return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if(args.Length==2&&new[]{"--checkpoint-wire-seed","--checkpoint-wire-request"}.Contains(args[0])) {
                try{Console.WriteLine(Json.Serialize(args[0]=="--checkpoint-wire-seed"?CheckpointRecoveryWireFixture.Seed(args[1]):CheckpointRecoveryWireFixture.Run(args[1])));return 0;}catch(Exception error){Console.Error.WriteLine(error.ToString());return 3;}
            }
            if (args.Length == 1 && args[0] == "--self-test")
            {
                try { return SelfTests.Run(); }
                catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 3; }
            }
            if (args.Length == 1 && args[0] == "--translation-test")
            {
                var result = TranslationTest.Run();
                Console.WriteLine(Json.Serialize(result));
                return (string)result["state"] == "succeeded_restored" ? 0 : 2;
            }
            if (args.Length == 1 && args[0] == "--checkpoint-test")
            {
                var result = CheckpointTest.Run();
                Console.WriteLine(Json.Serialize(result));
                return Equals(result["state"], "succeeded_saved_and_reopened") ? 0 : 2;
            }
            if (args.Length == 1 && (args[0] == "--prepare-translation" || args[0] == "--execute-translation"))
            {
                var result = PlannedTranslation.Run(args[0] == "--execute-translation");
                Console.WriteLine(Json.Serialize(result));
                return Equals(result["state"], "plan_ready") || Equals(result["state"], "succeeded_saved") ? 0 : 2;
            }
            if(args.Length==1&&args[0]=="--verify-disk")
            {
                var result=DiskVerification.Run();Console.WriteLine(Json.Serialize(result));
                return Equals(result["state"],"succeeded_disk_verified")?0:2;
            }
            if(args.Length==2&&args[0]=="--mcp-operation")
            {Console.WriteLine(Program.Json.Serialize(QueueOperations.Run(args[1])));return 0;}
            if (args.Length == 1 && args[0] == "--probe")
            {
                var result = Probe.Run();
                Console.WriteLine(Json.Serialize(result));
                return (string)result["state"] == "succeeded_verified" ? 0 : 2;
            }
            bool smoke = args.Length == 1 && args[0] == "--ui-smoke-test";
            if (args.Length != 0 && !smoke) return 64;
            bool ownsPanel;
            string rootKey;
            using(var hash=SHA256.Create())rootKey=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(Root).ToLowerInvariant()))).Replace("-", "");
            using(var panelMutex=new System.Threading.Mutex(true,"Local\\PowerShapeBridgePanel_"+rootKey,out ownsPanel))
            {
            if(!ownsPanel)return 0;
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e) { ReportStartupError(e.Exception); };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var panel = new Panel();
                if (smoke)
                {
                    panel.Shown += delegate {
                        Directory.CreateDirectory(Path.Combine(Root, "reports"));
                        File.WriteAllText(Path.Combine(Root, "reports", "ui-smoke-test.json"), Json.Serialize(new { state = "passed", formCreated = panel.IsHandleCreated, title = panel.Text, geometryConnectionAttempted = false }), new UTF8Encoding(false));
                        var timer = new System.Windows.Forms.Timer { Interval = 250 };
                        timer.Tick += delegate { timer.Stop(); timer.Dispose(); panel.Close(); };
                        timer.Start();
                    };
                }
                Application.Run(panel); return 0;
            }
            catch (Exception ex) { ReportStartupError(ex); return 3; }
            }
        }
        private static void ReportStartupError(Exception ex)
        {
            string path = Path.Combine(Root, "reports", "startup-error.log");
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.AppendAllText(path, DateTime.UtcNow.ToString("o") + "\r\n" + ex + "\r\n", Encoding.UTF8); }
            catch { }
            MessageBox.Show("Не удалось запустить панель.\r\n" + ex.Message + "\r\nЖурнал: " + path, "PowerShape Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        public static string Hash(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        internal static bool PublishPanelStatus(string temporary,string path)
        {
            try { if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);return true; }
            catch(IOException error) {
                // External readers may deny deletion briefly. Keep the last complete
                // heartbeat and let the next timer publish a fresh one; never turn a
                // status-file collision into a CAD retry or an application dialog.
                if(!new[]{32,33,1175,1176}.Contains(error.HResult&0xffff))throw;
                try { File.Delete(temporary); } catch(IOException) { }
                return false;
            }
        }
        public static object[] ArrayValues(object raw)
        {
            if (raw == null) return new object[0];
            Array array = raw as Array;
            if (array == null || array.Rank != 1) throw new FormatException("Expected one-dimensional COM array.");
            return array.Cast<object>().ToArray();
        }
        public static int StrictInt(object raw)
        {
            int value;
            if (raw == null || !int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Invalid integer response.");
            return value;
        }
        public static double StrictDouble(object raw)
        {
            double value;
            if (raw == null || !double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException("Invalid finite number response.");
            return value;
        }
        public static double[] Vector3(object raw)
        {
            var values = ArrayValues(raw);
            if (values.Length != 3) throw new FormatException("Expected a finite three-component vector.");
            return values.Select(StrictDouble).ToArray();
        }
        public static string TypeName(int code, string description)
        {
            switch (code)
            {
                case 40: return "SOLID"; case 21: return "SURFACE";
                case 49: return "POINT"; case 52: return "ARC";
                case 53: return "CURVE"; case 54: return "WORKPLANE";
                case 58: return "LINE"; case 59: return "COMPCURVE";
                case 61: return "ANNOTATION";
                case 60: return description == "Mesh" ? "MESH" : description == "Point" ? "POINT" : "UNKNOWN";
                default: return "UNKNOWN";
            }
        }
    }

    // Active document inspection never invokes selection, geometry, save or capture commands.
    // The legacy configured snapshot may additionally invoke the fixed Print-to-File capture.
    public static class Probe
    {
        private static void Stage(string phase)
        {
            string reports = Path.Combine(Program.Root, "reports"); Directory.CreateDirectory(reports);
            File.WriteAllText(Path.Combine(reports, "worker-stage.json"), Program.Json.Serialize(new { phase=phase, timestampUtc=DateTime.UtcNow.ToString("o"), workerPid=Process.GetCurrentProcess().Id }), new UTF8Encoding(false));
        }
        public static Dictionary<string, object> Run(bool requireConfiguredModel = true, bool allowViewportCapture = true)
        {
            return ReadInContext(null, null, requireConfiguredModel, allowViewportCapture);
        }
        // Borrowed RCWs remain owned by the caller. Binding checks never reattach or release them.
        internal static Dictionary<string, object> ReadInContext(object borrowedApplication, object borrowedDocument, bool requireConfiguredModel = false, bool allowViewportCapture = false)
        {
            object app = null, doc = null;
            var r = new Dictionary<string, object> {
                {"schemaVersion", 1}, {"bridgeVersion", Program.ReleaseVersion}, {"readerVersion", "0.10.0"},
                {"modelScope", requireConfiguredModel ? "configured_baseline" : "active_document_read_only"},
                {"timestampUtc", DateTime.UtcNow.ToString("o")}, {"state", "failed"},
                {"geometryCommandsEnabled", false}, {"backend", "COM-read-only"},
                {"warnings", new List<string>()}
            };
            var warnings = (List<string>)r["warnings"];
            try
            {
                Stage("configuration");
                Config config = Program.ReadConfig();
                if (config.IsGeneralCore && (requireConfiguredModel || allowViewportCapture)) throw new InvalidOperationException("fixture_snapshot_disabled_in_general_core");
                string stepBefore = config.IsGeneralCore ? null : Program.Hash(config.sourceStep), projectBefore = config.IsGeneralCore ? null : Program.Hash(config.expectedProject);
                r["filesBefore"] = new { stepSha256 = stepBefore, projectSha256 = projectBefore };
                // Never activate a server. GetActiveObject attaches to a registered running object only.
                var candidates = Process.GetProcessesByName("powershape").Where(p => p.SessionId == Process.GetCurrentProcess().SessionId).ToArray();
                r["candidateProcessIds"] = candidates.Select(p => p.Id).ToArray();
                if (candidates.Length == 0) throw new InvalidOperationException("no_powershape_in_this_session");
                Stage("attach");
                app = borrowedApplication ?? Marshal.GetActiveObject("PowerShape.Application");
                dynamic ps = app;
                int pid = Program.StrictInt(ps.Evaluate("app.pid"));
                r["processId"] = pid;
                using (var process = Process.GetProcessById(pid))
                {
                    r["processStartedUtc"] = process.StartTime.ToUniversalTime().ToString("o");
                    r["windowsSessionId"] = process.SessionId;
                }
                if (!candidates.Any(p => p.Id == pid) || !Process.GetProcessById(pid).ProcessName.Equals("powershape", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("unexpected_process");
                int[] versions = new int[] { Program.StrictInt(ps.Evaluate("VERSION.MAJOR")), Program.StrictInt(ps.Evaluate("VERSION.MINOR")), Program.StrictInt(ps.Evaluate("VERSION.REVISION")) };
                string version = string.Join(".", versions);
                r["productVersion"] = version;
                r["versionRaw"] = Convert.ToString(ps.Version, CultureInfo.InvariantCulture);
                if (version != config.expectedVersion) throw new InvalidOperationException("unsupported_build");
                bool busy = Convert.ToBoolean(ps.Busy(), CultureInfo.InvariantCulture);
                r["busy"] = busy;
                if (busy) throw new InvalidOperationException("busy");
                string modelName = Convert.ToString(ps.Evaluate("MODEL.SELECTED"), CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(modelName) || modelName.IndexOf('\'') >= 0 || modelName.IndexOf('\n') >= 0 || modelName.IndexOf('\r') >= 0)
                    throw new InvalidOperationException("model_identity_unavailable");
                int modelId = Program.StrictInt(ps.Evaluate("MODEL['" + modelName + "'].ID"));
                string modelPath = Convert.ToString(ps.Evaluate("MODEL[ID " + modelId + "].PATH"), CultureInfo.InvariantCulture);
                r["model"] = new { id = modelId, name = modelName, path = modelPath };
                if (requireConfiguredModel && (string.IsNullOrWhiteSpace(modelPath) || !Path.GetFullPath(modelPath).Equals(Path.GetFullPath(config.expectedProject), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("unexpected_model");
                Stage("context_before");
                int[] contextBefore = ReadContext(ps);
                r["contextBefore"] = contextBefore;
                string selectionBefore = config.inspectSelectionSignature ? ReadSelection(ps) : "[]";
                r["selectionBefore"] = Program.Json.DeserializeObject(selectionBefore);
                doc = borrowedDocument ?? ps.ActiveDocument;
                dynamic document = doc;
                Stage("inventory");
                object[] items = Program.ArrayValues(document.GetModelItems());
                if (items.Length > config.maxEntities) throw new InvalidOperationException("entity_limit_exceeded");
                object rawTypes = null, rawIds = null, rawNames = null, rawDescriptions = null, rawLevels = null;
                var entities = new List<Dictionary<string, object>>();
                if (items.Length > 0)
                {
                    document.GetItemData(items, out rawTypes, out rawIds, out rawNames, out rawDescriptions, out rawLevels);
                    object[][] data = new[] { Program.ArrayValues(rawTypes), Program.ArrayValues(rawIds), Program.ArrayValues(rawNames), Program.ArrayValues(rawDescriptions), Program.ArrayValues(rawLevels) };
                    if (data.Any(x => x.Length != items.Length)) throw new FormatException("Inconsistent GetItemData arrays.");
                    for (int i = 0; i < items.Length; i++)
                    {
                        int code = Program.StrictInt(data[0][i]), id = Program.StrictInt(data[1][i]), level = Program.StrictInt(data[4][i]);
                        if (id < 0 || level < 0 || level > 999) throw new FormatException("Invalid native entity ID/level.");
                        string description = Convert.ToString(data[3][i], CultureInfo.InvariantCulture);
                        string type = Program.TypeName(code, description);
                        entities.Add(new Dictionary<string, object> { {"nativeTypeCode",code}, {"nativeId",id}, {"type",type}, {"name",Convert.ToString(data[2][i],CultureInfo.InvariantCulture)}, {"description",description}, {"levelNumber",level} });
                    }
                }
                r["entities"] = entities;
                r["entityCount"] = entities.Count;
                r["countsByType"] = entities.GroupBy(x => (string)x["type"]).ToDictionary(g => g.Key, g => g.Count());
                var levels = new List<Dictionary<string, object>>();
                foreach (int number in entities.Select(x => (int)x["levelNumber"]).Distinct().OrderBy(x => x))
                {
                    string prefix = "LEVEL[" + number + "]";
                    var entry = new Dictionary<string, object> { {"number",number} };
                    Optional(ps, prefix + ".NAME", entry, "name", warnings);
                    Optional(ps, prefix + ".ACTIVE", entry, "activeRaw", warnings);
                    levels.Add(entry);
                }
                r["levelsContainingEntities"] = levels;
                Optional(ps, "unit[length].name", r, "lengthUnitsRaw", warnings);
                foreach (string unit in new[] { "length", "area", "volume" })
                {
                    if (config.inspectUnitFactors)
                    {
                        Stage("unit_" + unit + "_name");
                        Optional(ps, "unit[" + unit + "].name", r, unit + "UnitNameRaw", warnings);
                        Stage("unit_" + unit + "_factor");
                        Optional(ps, "unit[" + unit + "].factor", r, unit + "UnitFactorRaw", warnings);
                    }
                    else { r[unit + "UnitNameRaw"] = null; r[unit + "UnitFactorRaw"] = null; }
                }
                Stage("solid_properties");
                Optional(ps, "TOLERANCE.GENERAL", r, "generalToleranceRaw", warnings);
                foreach (var e in entities.Where(x => (string)x["type"] == "SOLID"))
                {
                    string prefix = "SOLID[ID " + e["nativeId"] + "]";
                    var measures = new Dictionary<string, object>();
                    foreach (string property in new[] { "VOLUME", "AREA", "SURFACES", "NLINKS_OPEN", "MAXIMUM_EDGE_TOL" })
                    {
                        string field = property.ToLowerInvariant() + (property == "VOLUME" || property == "AREA" ? "Raw" : "");
                        try { measures[field] = Program.StrictDouble(ps.Evaluate(prefix + "." + property)); }
                        catch (Exception ex) { measures[field] = null; warnings.Add("solid " + e["nativeId"] + " " + property + ": " + ex.Message); }
                    }
                    foreach (string property in new[] { "COG", "CLOSED", "CONNECTED", "WATERTIGHT", "TRIMMING_VALID" })
                        Optional(ps, prefix + "." + property, measures, property.ToLowerInvariant() + "Raw", warnings);
                    measures["lengthUnitsRaw"] = r["lengthUnitsRaw"];
                    measures["volumeUnitsRaw"] = r["volumeUnitNameRaw"];
                    measures["volumeUnitFactorRaw"] = r["volumeUnitFactorRaw"];
                    measures["coordinateFrame"] = contextBefore[1] == 0 ? "world" : "active_workplane_unqualified";
                    measures["qualification"] = "read_success_does_not_certify_accuracy";
                    e["geometry"] = measures;
                }
                var workplanes = new List<Dictionary<string, object>>();
                foreach (var e in entities.Where(x => (string)x["type"] == "WORKPLANE"))
                {
                    var wp = new Dictionary<string, object> { {"nativeId", e["nativeId"]} };
                    string prefix = "WORKPLANE[ID " + e["nativeId"] + "]";
                    foreach (string property in new[] { "ORIGIN", "XAXIS", "YAXIS", "ZAXIS" }) Optional(ps, prefix + "." + property, wp, property.ToLowerInvariant() + "Raw", warnings);
                    workplanes.Add(wp);
                }
                r["workplanes"] = workplanes;
                if (!requireConfiguredModel)
                {
                    foreach (var entity in entities.Where(x => (string)x["type"] == "SURFACE" || (string)x["type"] == "COMPCURVE"))
                    {
                        string type = (string)entity["type"], prefix = type + "[ID " + entity["nativeId"] + "]";
                        var geometry = new Dictionary<string,object>();
                        foreach (string property in type == "SURFACE" ? new[]{"AREA"} : new[]{"LENGTH","AREA"})
                        {
                            string field = property.ToLowerInvariant() + "Raw";
                            try { geometry[field] = Program.StrictDouble(ps.Evaluate(prefix + "." + property)); }
                            catch (Exception ex) { geometry[field] = null; warnings.Add(type + " " + entity["nativeId"] + " " + property + ": " + ex.Message); }
                        }
                        if (type == "COMPCURVE")
                        {
                            foreach (string property in new[]{"MIN_RANGE","MAX_RANGE"})
                            {
                                string field = property.ToLowerInvariant() + "Raw";
                                try { geometry[field] = Program.Vector3(ps.Evaluate(prefix + "." + property)); }
                                catch (Exception ex) { geometry[field] = null; warnings.Add(type + " " + entity["nativeId"] + " " + property + ": " + ex.Message); }
                            }
                            Optional(ps, prefix + ".CLOSED", geometry, "closedRaw", warnings);
                        }
                        geometry["coordinateFrame"] = contextBefore[1] == 0 ? "world" : "active_workplane_unqualified";
                        geometry["lengthUnitsRaw"] = r["lengthUnitsRaw"];
                        geometry["areaUnitsRaw"] = r["areaUnitNameRaw"];
                        geometry["qualification"] = "available_point_in_time_properties_only; missing_fields_are_null";
                        entity["geometry"] = geometry;
                    }
                    foreach (var entity in entities) if (!entity.ContainsKey("geometry")) entity["geometry"] = null;
                }
                if (config.inspectSelectionBounds && contextBefore[0] > 0)
                {
                    try
                    {
                        Stage("selection_bounds_min");
                        double[] min = Program.Vector3(ps.Evaluate("SELECTION.MIN_RANGE_EXACT"));
                        Stage("selection_bounds_max");
                        double[] max = Program.Vector3(ps.Evaluate("SELECTION.MAX_RANGE_EXACT"));
                        double[] size = max.Zip(min, (a,b) => a-b).ToArray();
                        if (size.Any(x => x < 0)) throw new FormatException("Invalid bounding range.");
                        r["selectionBounds"] = new { min=min, max=max, size=size, lengthUnitsRaw=r["lengthUnitsRaw"], frame=contextBefore[1] == 0 ? "world" : "active_workplane_unqualified", scope="current_selection_only", method="SELECTION.MIN_RANGE_EXACT/MAX_RANGE_EXACT" };
                    }
                    catch (Exception ex) { r["selectionBounds"] = null; warnings.Add("Selection bounds: " + ex.Message); }
                }
                else { r["selectionBounds"] = null; if (config.inspectSelectionBounds) warnings.Add("No selection; bounds not read. Selection was not changed."); }
                if (config.captureViewport && allowViewportCapture)
                {
                    try
                    {
                        Stage("viewport_capture");
                        string pictures = Path.Combine(Program.Root, "captures"); Directory.CreateDirectory(pictures);
                        string image = Path.Combine(pictures, "viewport-" + Guid.NewGuid().ToString("N") + ".jpg");
                        if (image.IndexOf('"') >= 0 || image.IndexOf('\n') >= 0 || image.IndexOf('\r') >= 0 || image.IndexOf(' ') >= 0 || File.Exists(image)) throw new InvalidOperationException("Invalid capture destination.");
                        // Fixed official PowerShape Print-to-File command; no arbitrary command input.
                        ps.Exec("FILE PRINT Filename FileDialog Write Validate " + image);
                        if (!File.Exists(image)) throw new IOException("PowerShape did not produce an image.");
                        using (var decoded = System.Drawing.Image.FromFile(image))
                        {
                            if (decoded.Width < 2 || decoded.Height < 2) throw new IOException("Invalid capture dimensions.");
                            r["viewportCapture"] = new { path=image, width=decoded.Width, height=decoded.Height, sha256=Program.Hash(image), method="PowerShape Print to File", qualification="image_decoded_content_not_yet_inspected" };
                        }
                    }
                    catch (Exception ex) { r["viewportCapture"] = null; warnings.Add("Viewport capture: " + ex.Message); }
                }
                Stage("context_after");
                int[] contextAfter = ReadContext(ps);
                r["contextAfter"] = contextAfter;
                string selectionAfter = config.inspectSelectionSignature ? ReadSelection(ps) : "[]";
                r["selectionAfter"] = Program.Json.DeserializeObject(selectionAfter);
                if (!contextBefore.SequenceEqual(contextAfter) || selectionBefore != selectionAfter || Convert.ToString(ps.Evaluate("MODEL.SELECTED"), CultureInfo.InvariantCulture) != modelName)
                    throw new InvalidOperationException("context_changed_during_read");
                int modelIdAfter = Program.StrictInt(ps.Evaluate("MODEL['" + modelName + "'].ID"));
                string modelPathAfter = Convert.ToString(ps.Evaluate("MODEL[ID " + modelIdAfter + "].PATH"), CultureInfo.InvariantCulture);
                if (modelIdAfter != modelId || modelPathAfter != modelPath)
                    throw new InvalidOperationException("document_changed_during_read");
                object[] itemsAfter = Program.ArrayValues(document.GetModelItems());
                if (Program.Json.Serialize(itemsAfter) != Program.Json.Serialize(items))
                    throw new InvalidOperationException("inventory_changed_during_read");
                if (itemsAfter.Length > 0)
                {
                    object finalTypes=null, finalIds=null, finalNames=null, finalDescriptions=null, finalLevels=null;
                    document.GetItemData(itemsAfter, out finalTypes, out finalIds, out finalNames, out finalDescriptions, out finalLevels);
                    if (Program.Json.Serialize(new object[]{finalTypes,finalIds,finalNames,finalDescriptions,finalLevels}) !=
                        Program.Json.Serialize(new object[]{rawTypes,rawIds,rawNames,rawDescriptions,rawLevels}))
                        throw new InvalidOperationException("entity_metadata_changed_during_read");
                }
                r["inventoryConsistency"] = "native_item_list_and_metadata_rechecked; geometry_properties_are_point_in_time_reads";
                string stepAfter = config.IsGeneralCore ? null : Program.Hash(config.sourceStep), projectAfter = config.IsGeneralCore ? null : Program.Hash(config.expectedProject);
                r["filesAfter"] = new { stepSha256 = stepAfter, projectSha256 = projectAfter };
                if (stepAfter != stepBefore || projectAfter != projectBefore) throw new InvalidOperationException("input_file_changed");
                r["state"] = "succeeded_verified";
                r["fileProtectionScope"] = config.IsGeneralCore ? "No fixture files required or read; this request performs no file writes to CAD documents." : "Configured fixture disk hashes checked.";
                r["verificationScope"] = requireConfiguredModel ?
                    (config.inspectSelectionSignature ? "Exact version/project, unchanged disk hashes, selection type/name sequence and active workplane; geometry accuracy and full UI context not certified." : "Exact version/project, unchanged disk hashes, selection count and active workplane; selection identity, geometry accuracy and full UI context not certified.") :
                    "Active document native ID/path and inventory metadata rechecked; selection/workplane checked. See fileProtectionScope for disk checks. Geometry properties are point-in-time observations, not a full geometry revision or a persistent object identity.";
                r["capabilities"] = new { attachOnly = "verified_this_run", inventory = "verified_this_run", geometryBounds = r.ContainsKey("selectionBounds") && r["selectionBounds"] != null ? "selection_only_read_this_run" : "unavailable_this_run", screenshot = r.ContainsKey("viewportCapture") && r["viewportCapture"] != null ? "decoded_this_run" : "unverified", mutations = "disabled_for_this_read_request", mcp = "external_stdio_adapter" };
                Stage("completed");
            }
            catch (Exception ex)
            {
                Stage("failed");
                Exception actual = ex;
                while (actual.InnerException != null) actual = actual.InnerException;
                r["error"] = new { code = actual is COMException ? "com_attach_or_read_error" : "probe_failed", message = actual.Message, hresult = "0x" + actual.HResult.ToString("X8") };
            }
            finally
            {
                // Release only our RCWs; never call Quit or dispose Autodesk's owning wrapper.
                if (borrowedDocument == null && doc != null && Marshal.IsComObject(doc)) Marshal.ReleaseComObject(doc);
                if (borrowedApplication == null && app != null && Marshal.IsComObject(app)) Marshal.ReleaseComObject(app);
            }
            return r;
        }
        private static int[] ReadContext(dynamic ps)
        {
            return new int[] { Program.StrictInt(ps.Evaluate("SELECTION.NUMBER")), Program.StrictInt(ps.ActiveDocument.ActiveWorkplane) };
        }
        private static string ReadSelection(dynamic ps)
        {
            int count = Program.StrictInt(ps.Evaluate("SELECTION.NUMBER"));
            if (count < 0 || count > 100000) throw new InvalidOperationException("Selection limit exceeded.");
            var selected = new List<object>();
            for (int i=0; i<count; i++) selected.Add(new { type=Convert.ToString(ps.Evaluate("SELECTION.TYPE["+i+"]"),CultureInfo.InvariantCulture), name=Convert.ToString(ps.Evaluate("SELECTION.NAME["+i+"]"),CultureInfo.InvariantCulture) });
            return Program.Json.Serialize(selected);
        }
        private static void Optional(dynamic ps, string expression, Dictionary<string, object> target, string field, List<string> warnings)
        {
            try
            {
                object value = ps.Evaluate(expression);
                if (value == null || (value is string && string.IsNullOrWhiteSpace((string)value))) throw new FormatException("Empty response.");
                Array a = value as Array;
                target[field] = a == null ? value : Program.ArrayValues(value);
            }
            catch (Exception ex) { target[field] = null; warnings.Add(field + ": " + ex.Message); }
        }
    }

    public sealed class Panel : Form
    {
        private readonly Button run = new Button { Text = "Проверить подключение", AutoSize = true };
        private readonly Label status = new Label { Text = "Первый прототип: только чтение", AutoSize = true };
        private readonly TextBox output = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = false };
        private readonly CheckBox incoming = new CheckBox { Text = "Принимать запросы чтения", Checked = true, AutoSize = true };
        private readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 1000 };
        public Panel()
        {
            Text = "PowerShape Bridge — чтение модели P1"; Width = 1050; Height = 720;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 55, Padding = new Padding(10) };
            bar.Controls.Add(run); bar.Controls.Add(incoming); bar.Controls.Add(status); Controls.Add(output); Controls.Add(bar);
            if(Program.ReadConfig().IsGeneralCore)
            {Text="PowerShape Bridge — общее ядро "+Program.ReleaseVersion;status.Text="Чтение активной модели и выбор открытых документов";incoming.Text="Принимать запросы общего ядра";}
            if(Program.ReadConfig().mcpMutationsPanel)
            {Text="PowerShape Bridge — MCP "+Program.ReleaseVersion;status.Text="Чтение моделей и квалифицированные тестовые операции";incoming.Text="Принимать MCP-запросы";bar.Height=90;}
            else if(Program.ReadConfig().diskVerificationPanel)
            {
                Text="PowerShape Bridge — проверка файлов с диска P2.4";
                incoming.Text="Принимать запросы проверки файлов";
                status.Text="Откроются копии checkpoint и результата; геометрия не изменяется";
                bar.Height=90;
                var audit=new Button{Text="Проверить checkpoint и результат с диска",AutoSize=true};bar.Controls.Add(audit);
                audit.Click+=async delegate{if(!run.Enabled)return;audit.Enabled=false;try{await Execute("--verify-disk");}finally{audit.Enabled=true;}};
            }
            else if (Program.ReadConfig().parameterizedTranslationPanel)
                PlannedTranslation.AddControls(this,bar,run,status,output);
            else if (Program.ReadConfig().translationTestPanel)
            {
                bool checkpoint = Program.ReadConfig().checkpointTestPanel;
                Text = checkpoint ? "PowerShape Bridge — сохранение и восстановление P2.2" : "PowerShape Bridge — тест перемещения P2";
                status.Text = "Только копия: +5 мм X и возврат; результат ещё не проверен";
                var test = new Button { Text = checkpoint ? "Тест сохранения и восстановления" : "Тест +5 мм X и возврат", AutoSize = true };
                bar.Height = 90; bar.Controls.Add(test);
                test.Click += async delegate {
                    if (!run.Enabled) return;
                    test.Enabled = false; run.Enabled = false;
                    try {
                        string text = await Task.Run(() => RunChild(checkpoint ? 60 : 30, checkpoint ? "--checkpoint-test" : "--translation-test"));
                        output.Text = text;
                        var result = Program.Json.Deserialize<Dictionary<string, object>>(text);
                        status.Text = Equals(result["state"], checkpoint ? "succeeded_saved_and_reopened" : "succeeded_restored") ? (checkpoint ? "Сохранение и повторное открытие проверены" : "Перемещение и возврат проверены") : "Остановлено — прочитайте отчёт";
                        Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));
                        File.WriteAllText(Path.Combine(Program.Root,"reports",checkpoint ? "checkpoint-test-result.json" : "translation-test-result.json"),text,new UTF8Encoding(false));
                    }
                    catch (Exception ex) { output.Text = ex.ToString(); status.Text = "Исход требует проверки"; }
                    finally { run.Enabled = true; test.Enabled = !File.Exists(Path.Combine(Program.Root,"reports",checkpoint ? "checkpoint-test.once" : "translation-test.once")); }
                };
            }
            run.Click += async delegate {
                if(Program.ReadConfig().IsGeneralCore) {
                    string id=Guid.NewGuid().ToString("N"),folder=Path.Combine(Program.Root,"requests");Directory.CreateDirectory(folder);
                    string pending=Path.Combine(folder,id+".json"),temporary=pending+".tmp";
                    File.WriteAllText(temporary,Program.Json.Serialize(new {requestId=id,operation="recipe.prepare",arguments=new {recipeId="inspect_active_model",parameters=new {}}}),new UTF8Encoding(false));
                    File.Move(temporary,pending);
                } else await Execute();
            };
            poll.Tick += async delegate { await PollRequest(); };
            Shown += delegate { WritePanelStatus(); };
            poll.Start();
            FormClosed += delegate { poll.Stop(); poll.Dispose(); };
            FormClosing += delegate(object sender,FormClosingEventArgs e) { if(!run.Enabled){e.Cancel=true;status.Text="Дождитесь завершения операции перед закрытием панели";} };
        }
        private async Task PollRequest()
        {
            WritePanelStatus();
            if(File.Exists(Path.Combine(Program.Root,"maintenance.json")))return;
            if (!incoming.Checked || !run.Enabled) return;
            string folder = Path.Combine(Program.Root, "requests");
            Directory.CreateDirectory(folder);
            string file = Directory.GetFiles(folder, "*.json").OrderBy(x => x).FirstOrDefault();
            if (file == null) return;
            string id = Path.GetFileNameWithoutExtension(file);
            string claim = file + ".processing";
            try
            {
                File.Move(file, claim);
                string response;
                try
                {
                    var req = FileRequests.Parse(id, File.ReadAllText(claim, Encoding.UTF8));
                    string operation=(string)req["operation"];
                    if(operation.StartsWith("translation.")||operation.StartsWith("recipe.")||operation=="baseline.activate")
                    {
                        response=await Execute("--mcp-operation "+id);
                    }
                    else response = await Execute(operation=="disk.verify"?"--verify-disk":"--probe");
                }
                catch (Exception ex) { response = Program.Json.Serialize(new { state = "rejected", error = ex.Message }); }
                // Response filename originates from the enumerated request basename, never JSON input.
                string responses = Path.Combine(Program.Root, "responses"); Directory.CreateDirectory(responses);
                string destination = Path.Combine(responses, id + ".json");
                string temporary = destination + ".tmp";
                if (!File.Exists(destination)) { File.WriteAllText(temporary, response, new UTF8Encoding(false)); File.Move(temporary, destination); }
                File.Move(claim, claim + ".done");
            }
            catch (Exception ex) { status.Text = "Ошибка канала чтения: " + ex.Message; }
        }
        private void WritePanelStatus()
        {
            string reports=Path.Combine(Program.Root,"reports");Directory.CreateDirectory(reports);
            string path=Path.Combine(reports,"panel-status.json"),temporary=path+".tmp";
            using(var process=Process.GetCurrentProcess()) {
                Config config=Program.ReadConfig();
                File.WriteAllText(temporary,Program.Json.Serialize(new{version=Program.ReleaseVersion,processId=process.Id,processStartedUtc=process.StartTime.ToUniversalTime().ToString("o"),windowsSessionId=process.SessionId,timestampUtc=DateTime.UtcNow.ToString("o"),bridgeRoot=Program.Root,configurationProfile=config.IsGeneralCore?"general-v1":"legacy_fixture",configurationSha256=Program.Hash(Path.Combine(Program.Root,"bridge.config.json")),acceptingRequests=incoming.Checked,workerIdle=run.Enabled}),new UTF8Encoding(false));
            }
            Program.PublishPanelStatus(temporary,path);
        }
        private async Task<string> Execute(string operation = "--probe")
        {
            run.Enabled = false; status.Text = "Чтение данных PowerShape…";
            try
            {
                Config config = Program.ReadConfig();
                string text = await Task.Run(() => RunChild(operation=="--verify-disk"||operation.StartsWith("--mcp-operation ")?(config.operationTimeoutSeconds==0?60:config.operationTimeoutSeconds):config.timeoutSeconds,operation));
                output.Text = text;
                var parsed = Program.Json.Deserialize<Dictionary<string, object>>(text);
                status.Text = Equals(parsed["state"],"succeeded_verified")||Equals(parsed["state"],"succeeded_disk_verified") ? "Проверка завершена" : "Подключение или чтение требует проверки";
                if(Equals(parsed["state"],"plan_ready"))status.Text="План подготовлен";
                if(Equals(parsed["state"],"baseline_active"))status.Text="Тестовая модель активна";
                if(Equals(parsed["state"],"succeeded_saved"))status.Text="Операция проверена, результат сохранён";
                if(Equals(parsed["state"],"succeeded_surface_section_phase"))status.Text="Этап нарезки проверен";
                string reports = Path.Combine(Program.Root, "reports"); Directory.CreateDirectory(reports);
                File.WriteAllText(Path.Combine(reports, "probe-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json"), text, new UTF8Encoding(false));
                if(operation=="--verify-disk")File.WriteAllText(Path.Combine(reports,"disk-verification-result.json"),text,new UTF8Encoding(false));
                return text;
            }
            catch (Exception ex) { status.Text = "Ошибка"; output.Text = ex.Message; return Program.Json.Serialize(new { state = "failed", error = ex.Message }); }
            finally { run.Enabled = true; }
        }
        public static string RunChild(int seconds, string operation = "--probe")
        {
            using (var child = new Process())
            {
                Guid queueId;bool queueOperation=operation.StartsWith("--mcp-operation ")&&Guid.TryParseExact(operation.Substring(16),"N",out queueId);
                if (operation != "--probe" && operation != "--translation-test" && operation != "--checkpoint-test" && operation != "--prepare-translation" && operation != "--execute-translation" && operation != "--verify-disk" && !queueOperation) throw new ArgumentException("Unknown worker operation.");
                child.StartInfo = new ProcessStartInfo(Path.Combine(Program.Root, "PSBridge.Worker.exe"), operation) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                child.Start();
                Task<string> stdout = child.StandardOutput.ReadToEndAsync(), stderr = child.StandardError.ReadToEndAsync();
                if (!child.WaitForExit(seconds * 1000))
                {
                    int workerPid=child.Id;
                    child.Kill(); child.WaitForExit();Task.WaitAll(stdout,stderr);
                    return Program.Json.Serialize(new { state = "timeout_outcome_unknown", workerPid=workerPid, workerTimeoutSeconds=seconds, operation=operation, error = new { code = "worker_timeout", message = "Остановлен только worker Bridge. PowerShape не закрывался." }, recoveryInstructions="Inspect the request response and operation journal before recovery. Do not repeat an uncertain mutation." });
                }
                Task.WaitAll(stdout, stderr);
                if (string.IsNullOrWhiteSpace(stdout.Result)) throw new InvalidOperationException("Worker returned no report: " + stderr.Result);
                return stdout.Result.Trim();
            }
        }
    }

    public static class FileRequests
    {
        // Transitional local transport; not an MCP implementation. Only one fixed read operation.
        public static Dictionary<string, object> Parse(string basename, string text)
        {
            Guid guid;
            if (!Guid.TryParseExact(basename, "N", out guid) || text.Length > 4096) throw new ArgumentException("Invalid request ID or size.");
            var r = Program.Json.Deserialize<Dictionary<string, object>>(text);
            if(r!=null&&r.ContainsKey("operation")&&(Equals(r["operation"],"translation.prepare")||Equals(r["operation"],"translation.execute")||Equals(r["operation"],"baseline.activate")||Equals(r["operation"],"recipe.prepare")||Equals(r["operation"],"recipe.execute")))
            {QueueOperations.ValidateRequest(basename,r);return r;}
            if (r == null || r.Count != 2 || !r.ContainsKey("requestId") || !r.ContainsKey("operation") || !Equals(r["requestId"], basename) ||
                !(Equals(r["operation"],"model.snapshot") || (Equals(r["operation"],"disk.verify") && Program.ReadConfig().diskVerificationPanel)))
                throw new ArgumentException("Only configured model.snapshot/disk.verify with matching requestId is allowed.");
            return r;
        }
    }

    public static class SelfTests
    {
        public sealed class TransientLevelReader
        {
            public int Attempts;
            public object Evaluate(string expression) { if(expression!="LEVEL[102].ACTIVE")throw new ArgumentException("Unexpected read");Attempts++;return Attempts<3?(object)new double[]{1,2}:1; }
        }
        public static int Run()
        {
            int count = 0;
            count += EntitySets.SelfTest();
            count += OwnedCopies.SelfTest();
            count += DocumentBindings.SelfTest();
            count += WorkingCopyOpen.SelfTest();
            count += OwnedOperationPlanTests.Run();
            count += OwnedTranslationTests.Run();
            count += OwnedSaveTargetTests.Run();
            count += OwnedSaveAsTests.Run();
            count += OwnedSavedReopenTests.Run();
            count += OwnedSavedReopenExecutionTests.Run();
            Action<bool> check = ok => { if (!ok) throw new Exception("Self-test failed at " + count); count++; };
            string statusRoot=Path.Combine(Program.Root,"work","status-self-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(statusRoot);
            string statusPath=Path.Combine(statusRoot,"panel-status.json"),statusTemporary=statusPath+".tmp";File.WriteAllText(statusPath,"old");File.WriteAllText(statusTemporary,"new");
            using(var reader=new FileStream(statusPath,FileMode.Open,FileAccess.Read,FileShare.Read))check(!Program.PublishPanelStatus(statusTemporary,statusPath)&&File.ReadAllText(statusPath)=="old"&&!File.Exists(statusTemporary));
            File.WriteAllText(statusTemporary,"new");check(Program.PublishPanelStatus(statusTemporary,statusPath)&&File.ReadAllText(statusPath)=="new");
            check(Program.StrictInt(0) == 0);
            var levelReader=new TransientLevelReader();check(SurfaceSectionJob.ReadLevelState(levelReader,102)==1&&levelReader.Attempts==3);
            bool rejected = false; try { Program.StrictInt("invalid"); } catch (FormatException) { rejected = true; } check(rejected);
            rejected = false; try { Program.StrictInt(null); } catch (FormatException) { rejected = true; } check(rejected);
            Array array = Array.CreateInstance(typeof(object), new[] { 2 }, new[] { 1 }); array.SetValue(10, 1); array.SetValue(20, 2);
            check(Program.ArrayValues(array).Select(Program.StrictInt).SequenceEqual(new[] { 10, 20 }));
            check(Program.TypeName(59, "") == "COMPCURVE" && Program.TypeName(123, "") == "UNKNOWN");
            var original = new Dictionary<string, object> { {"name", "Тест"}, {"value", 1.01}, {"missing", null} };
            var restored = Program.Json.Deserialize<Dictionary<string, object>>(Program.Json.Serialize(original));
            check((string)restored["name"] == "Тест" && restored["missing"] == null);
            Program.ReadConfig(); count++;
            var general=new Config{coreProfile="general-v1",expectedVersion="23.2.2",timeoutSeconds=20,operationTimeoutSeconds=80,maxEntities=10000};
            general.Validate();check(general.CoreTransportEnabled);
            general.operationPlansExperimental=true;rejected=false;try{general.Validate();}catch(ArgumentException){rejected=true;}check(rejected);
            general.entitySetsExperimental=true;general.Validate();check(general.CoreTransportEnabled);
            general.operationPlansExperimental=false;general.entitySetsExperimental=false;
            general.expectedProject="D:\\missing.psmodel";rejected=false;try{general.Validate();}catch(ArgumentException){rejected=true;}check(rejected);general.expectedProject=null;
            general.recipeOperationsPanel=true;rejected=false;try{general.Validate();}catch(ArgumentException){rejected=true;}check(rejected);general.recipeOperationsPanel=false;
            general.captureViewport=true;rejected=false;try{general.Validate();}catch(ArgumentException){rejected=true;}check(rejected);
            if(Program.ReadConfig().IsGeneralCore) {
                string coreId=new string('a',32);
                foreach(string recipe in new[]{"inspect_active_model","list_documents","activate_document"}) {
                    FileRequests.Parse(coreId,Program.Json.Serialize(new {requestId=coreId,operation="recipe.prepare",arguments=new {recipeId=recipe,parameters=new {}}}));count++;
                }
                foreach(string recipe in new[]{"rotate_z","qualify_read_document","test_surface_section_phase","prepare_operation_plan","execute_operation_plan"}) {
                    rejected=false;try{FileRequests.Parse(coreId,Program.Json.Serialize(new {requestId=coreId,operation="recipe.prepare",arguments=new {recipeId=recipe,parameters=new {}}}));}catch(ArgumentException){rejected=true;}check(rejected);
                }
            }
            check(Program.StrictDouble("1.01") == 1.01);
            rejected = false; try { Program.StrictDouble("NaN"); } catch (FormatException) { rejected = true; } check(rejected);
            rejected = false; try { Program.StrictDouble("1,01"); } catch (FormatException) { rejected = true; } check(rejected);
            string id = Guid.NewGuid().ToString("N");
            check(FileRequests.Parse(id, Program.Json.Serialize(new { requestId=id, operation="model.snapshot" })).Count == 2);
            rejected = false; try { FileRequests.Parse(id, Program.Json.Serialize(new { requestId=id, operation="execute", command="DELETE" })); } catch (ArgumentException) { rejected = true; } check(rejected);
            rejected = false; try { FileRequests.Parse("../invalid", "{}"); } catch (ArgumentException) { rejected = true; } check(rejected);
            check(Program.Vector3(new object[] { 1, 2.5, 3 }).SequenceEqual(new[] {1.0,2.5,3.0}));
            rejected = false; try { Program.Vector3(new object[] {1,2}); } catch (FormatException) { rejected = true; } check(rejected);
            check(TranslationTest.Bounds(new object[] {1,-10,10,-10,10,0,20}).SequenceEqual(new[] {-10.0,10.0,-10.0,10.0,0.0,20.0}));
            rejected = false; try { TranslationTest.Bounds(new object[] {0,0,0,0,0,0,0}); } catch (FormatException) { rejected = true; } check(rejected);
            check(TranslationTest.Same(new[] {1.0,2.0},new[] {1.0000001,2.0}));
            check(!TranslationTest.Same(new[] {1.0,2.0},new[] {1.001,2.0}));
            rejected = false; try { TranslationTest.ValidateSandbox(Program.ReadConfig()); } catch (InvalidOperationException) { rejected = true; }
            check(!Program.ReadConfig().translationTestPanel ? rejected : !rejected);
            CheckpointTest.ValidatePath(Path.Combine(Program.Root,"Checkpoint_Test_Projects","valid.psmodel")); count++;
            rejected=false; try { CheckpointTest.ValidatePath("C:\\Folder With Space\\bad.psmodel"); } catch(ArgumentException){rejected=true;} check(rejected);
            PlannedTranslation.ValidateVector(new[]{5.0,-2.0,3.0}); count++;
            rejected=false; try { PlannedTranslation.ValidateVector(new[]{double.NaN,0.0,0.0}); } catch(ArgumentException){rejected=true;} check(rejected);
            rejected=false; try { PlannedTranslation.ValidateVector(new[]{101.0,0.0,0.0}); } catch(ArgumentException){rejected=true;} check(rejected);
            check(PlannedTranslation.ShiftBounds(new[]{-10.0,10.0,-10.0,10.0,0.0,20.0},new[]{5.0,-2.0,3.0}).SequenceEqual(new[]{-5.0,15.0,-12.0,8.0,3.0,23.0}));
            check(PlannedTranslation.Digest("one") != PlannedTranslation.Digest("two"));
            check(PlannedTranslation.ParseInput("{\"deltaWorldMm\":[5,-2,3]}").SequenceEqual(new[]{5.0,-2.0,3.0}));
            rejected=false;try{PlannedTranslation.ParseInput("{\"deltaWorldMm\":[5,0,0],\"command\":\"DELETE\"}");}catch(ArgumentException){rejected=true;}check(rejected);
            rejected=false;try{PlannedTranslation.ParseInput("{\"deltaWorldMm\":[0,0,0]}");}catch(ArgumentException){rejected=true;}check(rejected);
            DateTime now=DateTime.UtcNow;
            var plan=new PlannedTranslation.Plan{operationId=Guid.NewGuid().ToString("N"),createdUtc=now.ToString("o"),processId=1,modelPath="test",fingerprint="before",geometryFingerprint="geometry",deltaWorldMm=new[]{5.0,-2.0,3.0},expectedBoundsWorldMm=new double[6],expectedCogWorldMm=new double[3]};
            PlannedTranslation.ValidatePreparedPlan(plan,1,"test","before","geometry",now);count++;
            rejected=false;try{PlannedTranslation.ValidatePreparedPlan(plan,1,"test","changed","geometry",now);}catch(InvalidOperationException){rejected=true;}check(rejected);
            rejected=false;try{PlannedTranslation.ValidatePreparedPlan(plan,1,"test","before","geometry",now.AddMinutes(6));}catch(InvalidOperationException){rejected=true;}check(rejected);
            rejected=false;try{PlannedTranslation.ValidatePreparedPlan(plan,2,"test","before","geometry",now);}catch(InvalidOperationException){rejected=true;}check(rejected);
            var offsetPlan=Program.Json.Deserialize<PlannedTranslation.Plan>(Program.Json.Serialize(plan));
            offsetPlan.recipeId="offset_test_top_surface_1mm";offsetPlan.deltaWorldMm=new double[3];
            rejected=false;try{PlannedTranslation.ValidatePreparedPlan(offsetPlan,1,"test","before","geometry",now);}catch(ArgumentException){rejected=true;}check(rejected);
            offsetPlan.offsetDistanceMm=1;offsetPlan.targetSide=5;offsetPlan.expectedTopZMm=21;offsetPlan.expectedSurfaceUnionBoundsWorldMm=new[]{-10.0,10.0,-10.0,10.0,0.0,21.0};
            PlannedTranslation.ValidatePreparedPlan(offsetPlan,1,"test","before","geometry",now);count++;
            offsetPlan.expectedSurfaceUnionBoundsWorldMm[5]=20;
            rejected=false;try{PlannedTranslation.ValidatePreparedPlan(offsetPlan,1,"test","before","geometry",now);}catch(ArgumentException){rejected=true;}check(rejected);
            check(SliceRecipes.Heights(new[]{2.0,18.0,8.0}).SequenceEqual(new[]{2.0,10.0,18.0}));
            check(SliceRecipes.Heights(new[]{0.1,0.3,0.1}).SequenceEqual(new[]{0.1,0.2,0.3}));
            check(SliceRecipes.Heights(new[]{0.0,1.6,0.8}).SequenceEqual(new[]{0.0,0.8,1.6}));
            check(SliceRecipes.Heights(new[]{10.0,10.0,1.0}).SequenceEqual(new[]{10.0}));
            check(GroupedExport.FileName("1:Vneshnie 0.6")=="1-Vneshnie 0.6.dgk");
            double[][] corners=new[]{new[]{-10.0,-10.0,0.8},new[]{10.0,-10.0,0.8},new[]{10.0,10.0,0.8},new[]{-10.0,10.0,0.8}};
            var perimeter=new Dictionary<string,object>{{"boundsWorldMm",new[]{-10.0,10.0,-10.0,10.0,0.8,0.8}},{"lengthMm",80.0},{"closed",true},{"keypointsWorldMm",corners}};
            check((int)SurfaceSectionJob.ValidateSquare(new[]{perimeter},0.8)["curveCount"]==1);
            var serializedPerimeter=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(perimeter));
            check((int)SurfaceSectionJob.ValidateSquare(new[]{serializedPerimeter},0.8)["curveCount"]==1);
            var edges=Enumerable.Range(0,4).Select(n=>new Dictionary<string,object>{{"boundsWorldMm",new[]{-10.0,10.0,-10.0,10.0,0.8,0.8}},{"lengthMm",20.0},{"closed",false},{"keypointsWorldMm",new[]{corners[n],corners[(n+1)%4]}}}).ToArray();
            check((double)SurfaceSectionJob.ValidateSquare(edges,0.8)["totalLengthMm"]==80);
            var workingInventory=Enumerable.Range(0,4).Select(n=>new Dictionary<string,object>{{"type",21},{"levelNumber",1}}).Concat(new[]{new Dictionary<string,object>{{"type",54},{"levelNumber",1}},new Dictionary<string,object>{{"type",59},{"levelNumber",1}},new Dictionary<string,object>{{"type",40},{"levelNumber",1}},new Dictionary<string,object>{{"type",21},{"levelNumber",2}}}).ToArray();
            check(SurfaceSectionJob.WorkingSurfaces(workingInventory,1).Length==4);
            rejected=false;try{SurfaceSectionJob.ValidateSquare(edges.Take(3).ToArray(),0.8);}catch(InvalidOperationException){rejected=true;}check(rejected);
            rejected=false;try{SurfaceSectionJob.ValidateSquare(new[]{edges[0],edges[0],edges[2],edges[3]},0.8);}catch(InvalidOperationException){rejected=true;}check(rejected);
            rejected=false;try{SurfaceSectionJob.ValidateSquare(edges,1.6);}catch(InvalidOperationException){rejected=true;}check(rejected);
            foreach(string unsafeName in new[]{"../escape","CON .test","1:a/b","a\nb"}) {
                rejected=false;try{GroupedExport.FileName(unsafeName);}catch(ArgumentException){rejected=true;}check(rejected);
            }
            foreach(var invalid in new[]{new[]{0.0,18.0,8.0},new[]{2.0,20.0,9.0},new[]{2.0,18.0,7.0},new[]{2.0,18.0,4.0},new[]{0.1,0.3,0.01},new[]{double.NaN,10.0,1.0},new[]{0.10000001,10.0,1.0}}) {
                rejected=false;try{SliceRecipes.Heights(invalid);}catch(ArgumentException){rejected=true;}check(rejected);
            }
            var slicePlan=Program.Json.Deserialize<PlannedTranslation.Plan>(Program.Json.Serialize(plan));slicePlan.recipeId="slice_test_cube_xy";slicePlan.deltaWorldMm=new double[3];slicePlan.sliceParametersMm=new[]{2.0,18.0,8.0};slicePlan.sliceHeightsWorldMm=new[]{2.0,10.0,18.0};
            PlannedTranslation.ValidatePreparedPlan(slicePlan,1,"test","before","geometry",now);count++;
            slicePlan.sliceHeightsWorldMm[1]=11;
            rejected=false;try{PlannedTranslation.ValidatePreparedPlan(slicePlan,1,"test","before","geometry",now);}catch(ArgumentException){rejected=true;}check(rejected);
            string copyTest=Path.Combine(Program.Root,"work","copy-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(copyTest);
            string sourceCopy=Path.Combine(copyTest,"source.bin"), frozenCopy=Path.Combine(copyTest,"checkpoint.bin");
            File.WriteAllBytes(sourceCopy,new byte[]{1,2,3,4});
            using(var openSource=new FileStream(sourceCopy,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete))
                check(PlannedTranslation.CopyFrozen(sourceCopy,frozenCopy)==Program.Hash(sourceCopy));
            File.WriteAllBytes(sourceCopy,new byte[]{5,6,7,8});check(File.ReadAllBytes(frozenCopy).SequenceEqual(new byte[]{1,2,3,4}));
            rejected=false;try{PlannedTranslation.CopyFrozen(sourceCopy,frozenCopy);}catch(IOException){rejected=true;}check(rejected);
            var measure=new CheckpointTest.Measurement{boundsWorldMm=new[]{-10.0,10.0,-10.0,10.0,0.0,20.0},cogWorldMm=new[]{0.0,0.0,10.0},volumeRaw=0.8,areaRaw=2400,volumeUnitsRaw="centilitres",surfaceCount=6,closed=true,connected=true,watertight=true,trimmingValid=true};
            var sameMeasure=Program.Json.Deserialize<CheckpointTest.Measurement>(Program.Json.Serialize(measure));sameMeasure.compositeId=42;sameMeasure.nativeId=99;
            check(DiskVerification.Equivalent(measure,sameMeasure));sameMeasure.cogWorldMm[0]=5;check(!DiskVerification.Equivalent(measure,sameMeasure));
            if(Program.ReadConfig().diskVerificationPanel)check(FileRequests.Parse(id,Program.Json.Serialize(new{requestId=id,operation="disk.verify"})).Count==2);
            if(Program.ReadConfig().mcpMutationsPanel)
            {
                check(TranslationTest.Same(PlannedTranslation.RotatePoint(new[]{1.0,0.0,2.0},90),new[]{0.0,1.0,2.0}));
                check(TranslationTest.Same(PlannedTranslation.RotateBounds(new[]{-10.0,10.0,-10.0,10.0,0.0,20.0},45),new[]{-10*Math.Sqrt(2),10*Math.Sqrt(2),-10*Math.Sqrt(2),10*Math.Sqrt(2),0.0,20.0}));
                rejected=false;try{PlannedTranslation.ValidateAngle(0);}catch(ArgumentException){rejected=true;}check(rejected);
                rejected=false;try{PlannedTranslation.ValidateAngle(181);}catch(ArgumentException){rejected=true;}check(rejected);
                check(FileRequests.Parse(id,Program.Json.Serialize(new{requestId=id,operation="translation.prepare",arguments=new{deltaWorldMm=new[]{-3.0,4.0,2.0}}})).Count==3);
                check(FileRequests.Parse(id,Program.Json.Serialize(new{requestId=id,operation="translation.execute",arguments=new{operationId=id,planSha256=new string('a',64)}})).Count==3);
                rejected=false;try{FileRequests.Parse(id,Program.Json.Serialize(new{requestId=id,operation="translation.execute",arguments=new{operationId="../bad",planSha256=new string('a',64)}}));}catch(ArgumentException){rejected=true;}check(rejected);
                rejected=false;try{FileRequests.Parse(id,Program.Json.Serialize(new{requestId=id,operation="translation.prepare",arguments=new{deltaWorldMm=new[]{1.0,2.0,3.0},command="DELETE"}}));}catch(ArgumentException){rejected=true;}check(rejected);
            }
            var documentLease=new Dictionary<string,object>{{"listingRequestId",new string('a',32)},{"listingSha256",new string('b',64)},{"windowNumber",4},{"modelId",280000579},{"expiresUtc",DateTime.UtcNow.AddMinutes(5).ToString("o")}};
            DocumentSession.ValidateActivation(documentLease);count++;
            foreach(object invalidWindow in new object[]{0,4097,"4 Front",true}) {
                var bad=new Dictionary<string,object>(documentLease);bad["windowNumber"]=invalidWindow;
                rejected=false;try{DocumentSession.ValidateActivation(bad);}catch(ArgumentException){rejected=true;}check(rejected);
            }
            foreach(string field in new[]{"modelId","listingSha256","expiresUtc","command"}) {
                var bad=new Dictionary<string,object>(documentLease);bad[field]=field=="listingSha256"?new string('z',64):"invalid";
                rejected=false;try{DocumentSession.ValidateActivation(bad);}catch(ArgumentException){rejected=true;}catch(FormatException){rejected=true;}check(rejected);
            }
            Console.WriteLine("PASS: " + count + " checks; no PowerShape connection attempted."); return 0;
        }
    }
}

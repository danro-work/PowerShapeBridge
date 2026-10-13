using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

// Standalone passive probe. GetActiveObject only; no new application/document or Bridge worker.
internal static class ReadSolidPrimitiveFrame {
 static object app,document;static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
 static int Integer(object value){return Convert.ToInt32(value,CultureInfo.InvariantCulture);}
 static object[] ArrayOf(object value){var array=value as Array;if(array==null||array.Rank!=1)throw new InvalidDataException("Expected native one-dimensional array.");return array.Cast<object>().ToArray();}
 static string Context() {
  dynamic ps=app;if(Convert.ToBoolean(ps.Busy())||!Convert.ToBoolean(ps.IsDocumentActive()))throw new InvalidOperationException("Busy/no active document.");if(Integer(ps.Evaluate("SELECTION.NUMBER"))!=0)throw new InvalidOperationException("Selection non-empty; no changes allowed.");
  dynamic doc=document;if(Integer(doc.ActiveWorkplane)!=0)throw new InvalidOperationException("World workplane required.");object current=ps.ActiveDocument;IntPtr a=IntPtr.Zero,b=IntPtr.Zero;
  try{a=Marshal.GetIUnknownForObject(document);b=Marshal.GetIUnknownForObject(current);if(a!=b)throw new InvalidOperationException("Active document changed.");}finally{if(a!=IntPtr.Zero)Marshal.Release(a);if(b!=IntPtr.Zero)Marshal.Release(b);if(Marshal.IsComObject(current))Marshal.ReleaseComObject(current);}
  string name=Convert.ToString(ps.Evaluate("MODEL.SELECTED"),CultureInfo.InvariantCulture);if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(new[]{'\'','\r','\n'})>=0)throw new InvalidDataException("Unsupported model name.");int id=Integer(ps.Evaluate("MODEL['"+name+"'].ID"));string path=Convert.ToString(ps.Evaluate("MODEL[ID "+id+"].PATH"),CultureInfo.InvariantCulture);
  object[] items=ArrayOf(doc.GetModelItems());object types=null,ids=null,names=null,descriptions=null,levels=null;doc.GetItemData(items,out types,out ids,out names,out descriptions,out levels);
  return Json.Serialize(new{modelId=id,modelName=name,modelPath=path,items,types,ids,names,descriptions,levels});
 }
 static void Release(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
 static int Main(string[] args) {
  if(args.Length!=1){Console.Error.WriteLine("Usage: fixed passive probe output-json");return 2;}
  try {
   string output=Path.GetFullPath(args[0]);if(File.Exists(output))throw new IOException("Use new evidence path.");app=Marshal.GetActiveObject("PowerShape.Application");dynamic ps=app;
   int pid=Integer(ps.Evaluate("app.pid"));var processes=Process.GetProcessesByName("powershape").Where(p=>p.SessionId==Process.GetCurrentProcess().SessionId).ToArray();if(processes.Length!=1||processes[0].Id!=pid)throw new InvalidOperationException("Ambiguous PowerShape process.");
   string version=string.Join(".",new[]{Integer(ps.Evaluate("VERSION.MAJOR")),Integer(ps.Evaluate("VERSION.MINOR")),Integer(ps.Evaluate("VERSION.REVISION"))});if(version!="23.2.2")throw new InvalidOperationException("Expected installed 2023 version23.2.2.");document=ps.ActiveDocument;dynamic doc=document;string before=Context();var rows=new List<object>();
   object[] items=ArrayOf(doc.GetModelItems());object types=null,ids=null,names=null,descriptions=null,levels=null;doc.GetItemData(items,out types,out ids,out names,out descriptions,out levels);var ts=ArrayOf(types);var ns=ArrayOf(ids);if(ts.Length!=items.Length||ns.Length!=items.Length)throw new InvalidDataException("Incomplete mapping.");
   foreach(int i in Enumerable.Range(0,items.Length).Where(i=>Integer(ts[i])==40)) {
    int nativeId=Integer(ns[i]);if(nativeId<0)throw new InvalidDataException("Invalid solid ID.");var values=new Dictionary<string,object>();string prefix="SOLID[ID "+nativeId+"]";
    foreach(string field in new[]{"TYPE","ORIGIN","XAXIS","YAXIS","ZAXIS","COG","MOI"}) {
     if(Context()!=before)throw new InvalidOperationException("Context changed before fixed read.");try{values[field]=ps.Evaluate(prefix+"."+field);}catch(Exception error){values[field]=new{unavailable=true,error=error.Message};}if(Context()!=before)throw new InvalidOperationException("Context changed after fixed read.");
    }
    rows.Add(new{nativeId,compositeId=Integer(items[i]),values});
   }
   string after=Context();if(before!=after)throw new InvalidOperationException("Context changed.");var result=new{state="passive_fixed_solid_properties_read",processId=pid,processStartedUtc=processes[0].StartTime.ToUniversalTime().ToString("o"),version,timestampUtc=DateTime.UtcNow.ToString("o"),before=Json.DeserializeObject(before),after=Json.DeserializeObject(after),solids=rows,geometryMutationAttempted=false,contextChangeAttempted=false,cadSaveAttempted=false,comApplicationCreated=false,scope="Fixed property read only; availability/units/native rotation convention not qualified"};Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,Json.Serialize(result));Console.WriteLine("PASS: fixed passive solid property read; no CAD changes.");return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 3;}finally{Release(document);Release(app);}
 }
}

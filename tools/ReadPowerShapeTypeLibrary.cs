using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using TYPEDESC=System.Runtime.InteropServices.ComTypes.TYPEDESC;
using TYPEATTR=System.Runtime.InteropServices.ComTypes.TYPEATTR;
using FUNCDESC=System.Runtime.InteropServices.ComTypes.FUNCDESC;
using ELEMDESC=System.Runtime.InteropServices.ComTypes.ELEMDESC;

// Offline metadata only. No COM application/document creation or running-object lookup.
internal static class ReadPowerShapeTypeLibrary {
 [DllImport("oleaut32.dll",CharSet=CharSet.Unicode,PreserveSig=false)]
 static extern void LoadTypeLibEx(string file,int regKind,out ITypeLib library);
 static string Kind(TYPEDESC type){return ((VarEnum)type.vt).ToString();}
 static string Hash(string path){using(var file=File.OpenRead(path))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
 static int Main(string[] args) {
  if(args.Length!=2){Console.Error.WriteLine("Usage: offline-reader type-library-path output-json");return 2;}
  try {
   ITypeLib library;LoadTypeLibEx(Path.GetFullPath(args[0]),2,out library);var types=new List<object>();
   try {
    for(int index=0;index<library.GetTypeInfoCount();index++) {
     ITypeInfo info;library.GetTypeInfo(index,out info);IntPtr attributes=IntPtr.Zero;
     try {
      string typeName,documentation,helpFile;int helpContext;info.GetDocumentation(-1,out typeName,out documentation,out helpContext,out helpFile);
      if(typeName!="IPshapeModelDoc"&&typeName!="IPshapeApp")continue;
      info.GetTypeAttr(out attributes);var type=(TYPEATTR)Marshal.PtrToStructure(attributes,typeof(TYPEATTR));var methods=new List<object>();
      for(int method=0;method<type.cFuncs;method++) {
       IntPtr pointer;info.GetFuncDesc(method,out pointer);
       try {
        var function=(FUNCDESC)Marshal.PtrToStructure(pointer,typeof(FUNCDESC));var names=new string[64];int found;info.GetNames(function.memid,names,names.Length,out found);
        string name=found>0?names[0]:"";
        if(!new[]{"RotateItem","TranslateItem","CopyItems","ScaleItem","ScaleItems","TransformItem","GetItemBoundingBox","GetModelItems","GetItemData","Exec"}.Contains(name))continue;
        var parameters=new List<object>();int size=Marshal.SizeOf(typeof(ELEMDESC));
        for(int p=0;p<function.cParams;p++) {
         var element=(ELEMDESC)Marshal.PtrToStructure(IntPtr.Add(function.lprgelemdescParam,p*size),typeof(ELEMDESC));
         parameters.Add(new{name=p+1<found?names[p+1]:"arg"+p,type=Kind(element.tdesc),flags=element.desc.paramdesc.wParamFlags.ToString()});
        }
        methods.Add(new{name,dispatchId=function.memid,invokeKind=function.invkind.ToString(),returnType=Kind(function.elemdescFunc.tdesc),parameters});
       }finally{info.ReleaseFuncDesc(pointer);}
      }
      types.Add(new{name=typeName,guid=type.guid.ToString(),kind=type.typekind.ToString(),methodCount=type.cFuncs,methods});
     }finally{if(attributes!=IntPtr.Zero)info.ReleaseTypeAttr(attributes);Marshal.ReleaseComObject(info);}
    }
   }finally{Marshal.ReleaseComObject(library);}
   if(types.Count==0)throw new InvalidDataException("Expected PowerShape interfaces absent.");
   var result=new{schema="psbridge.offline-typelib.v1",state="passed",source=Path.GetFullPath(args[0]),sourceSha256=Hash(args[0]),regKind="REGKIND_NONE",powerShapeConnectionAttempted=false,comApplicationCreated=false,types};
   string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllText(output,new JavaScriptSerializer().Serialize(result));Console.WriteLine("PASS: offline PowerShape type library metadata; no application or document created.");return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 3;}
 }
}

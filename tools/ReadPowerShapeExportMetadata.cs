using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using TYPEATTR=System.Runtime.InteropServices.ComTypes.TYPEATTR;
using FUNCDESC=System.Runtime.InteropServices.ComTypes.FUNCDESC;
using ELEMDESC=System.Runtime.InteropServices.ComTypes.ELEMDESC;
using TYPEDESC=System.Runtime.InteropServices.ComTypes.TYPEDESC;

// Offline type-library inspection only. REGKIND_NONE; never creates/attaches a CAD application.
internal static class ReadPowerShapeExportMetadata {
 [DllImport("oleaut32.dll",CharSet=CharSet.Unicode,PreserveSig=false)]
 static extern void LoadTypeLibEx(string file,int regKind,out ITypeLib library);
 static string Type(ITypeInfo owner,TYPEDESC descriptor,int depth=0) {
  if(depth>8)throw new InvalidDataException("Type descriptor nesting limit.");var kind=(VarEnum)descriptor.vt;
  if(kind==VarEnum.VT_PTR||kind==VarEnum.VT_SAFEARRAY)return kind+"("+Type(owner,(TYPEDESC)Marshal.PtrToStructure(descriptor.lpValue,typeof(TYPEDESC)),depth+1)+")";
  if(kind==VarEnum.VT_USERDEFINED) {ITypeInfo reference;owner.GetRefTypeInfo(descriptor.lpValue.ToInt32(),out reference);try{string name,doc,file;int context;reference.GetDocumentation(-1,out name,out doc,out context,out file);return kind+"("+name+")";}finally{Marshal.ReleaseComObject(reference);}}
  return kind.ToString();
 }
 static string Hash(string path){using(var file=File.OpenRead(path))using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
 static int Main(string[] args) {
  if(args.Length!=2){Console.Error.WriteLine("Usage: offline-export-metadata type-library output-json");return 2;}
  try {
   string source=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);if(File.Exists(output)||Directory.Exists(output))throw new IOException("Fresh metadata output required.");ITypeLib library;LoadTypeLibEx(source,2,out library);var types=new List<object>();var names=new List<string>();
   try {
    for(int index=0;index<library.GetTypeInfoCount();index++) {
     ITypeInfo info;library.GetTypeInfo(index,out info);IntPtr attributes=IntPtr.Zero;
     try {
      string typeName,documentation,helpFile;int helpContext;info.GetDocumentation(-1,out typeName,out documentation,out helpContext,out helpFile);names.Add(typeName);
      if(typeName!="IPshapeApp"&&typeName!="IPshapeModelDoc"&&!new[]{"selection","setting","option","parameter","preference","triangle"}.Any(word=>typeName.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0))continue;
      info.GetTypeAttr(out attributes);var type=(TYPEATTR)Marshal.PtrToStructure(attributes,typeof(TYPEATTR));var methods=new List<object>();
      for(int method=0;method<type.cFuncs;method++) {
       IntPtr pointer;info.GetFuncDesc(method,out pointer);
       try {
        var function=(FUNCDESC)Marshal.PtrToStructure(pointer,typeof(FUNCDESC));var memberNames=new string[128];int found;info.GetNames(function.memid,memberNames,memberNames.Length,out found);var parameters=new List<object>();int size=Marshal.SizeOf(typeof(ELEMDESC));
        for(int p=0;p<function.cParams;p++){var element=(ELEMDESC)Marshal.PtrToStructure(IntPtr.Add(function.lprgelemdescParam,p*size),typeof(ELEMDESC));parameters.Add(new{name=p+1<found?memberNames[p+1]:"arg"+p,type=Type(info,element.tdesc),flags=element.desc.paramdesc.wParamFlags.ToString()});}
        methods.Add(new{name=found>0?memberNames[0]:"",dispatchId=function.memid,invokeKind=function.invkind.ToString(),returnType=Type(info,function.elemdescFunc.tdesc),parameters});
       }finally{info.ReleaseFuncDesc(pointer);}
      }
      types.Add(new{name=typeName,guid=type.guid.ToString(),kind=type.typekind.ToString(),methodCount=type.cFuncs,documentation,methods});
     }finally{if(attributes!=IntPtr.Zero)info.ReleaseTypeAttr(attributes);Marshal.ReleaseComObject(info);}
    }
   }finally{Marshal.ReleaseComObject(library);}
   if(types.Count==0)throw new InvalidDataException("Relevant PowerShape interfaces absent.");Directory.CreateDirectory(Path.GetDirectoryName(output));var report=new{schema="psbridge.offline-export-metadata.v1",state="passed",source,sourceSha256=Hash(source),regKind="REGKIND_NONE",allTypeNames=names,types,powerShapeConnectionAttempted=false,comApplicationCreated=false};using(var writer=new StreamWriter(new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None),new System.Text.UTF8Encoding(false)))writer.Write(new JavaScriptSerializer{MaxJsonLength=16000000}.Serialize(report));Console.WriteLine("PASS: "+types.Count+" relevant type descriptions; "+names.Count+" type names; no CAD connection.");return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 3;}
 }
}

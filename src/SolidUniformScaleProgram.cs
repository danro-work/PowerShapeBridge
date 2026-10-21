using System;
using System.Globalization;
using System.Linq;

namespace PowerShapeBridge {
 // Specific frozen recipe only. No caller-supplied commands or recovery steps.
 internal sealed class SolidUniformScaleProgram {
  readonly string[] commands;int next;bool failed;
  internal SolidUniformScaleProgram(string name,double factor,double[] centre){
   if(string.IsNullOrWhiteSpace(name)||name.Length>128||name.Any(c=>!char.IsLetterOrDigit(c)&&c!=' '&&c!='_'&&c!='-'&&c!='.'))throw new ArgumentException("Safe literal SOLID name required.");
   if(double.IsNaN(factor)||double.IsInfinity(factor)||factor<0.01||factor>100||factor==1||centre==null||centre.Length!=3||centre.Any(v=>double.IsNaN(v)||double.IsInfinity(v)||Math.Abs(v)>1000000))throw new ArgumentException("Bounded positive factor and explicit WORLD centre required.");
   commands=new[]{"SELECT CLEARLIST ADD SOLID '"+name+"'","EDIT SCALE","NOKEEP","UNIFORM","LOCK X OFF","LOCK Y OFF","LOCK Z OFF","SCALEORIGIN",string.Join(" ",centre.Select(v=>v.ToString("R",CultureInfo.InvariantCulture))),"VALUE "+factor.ToString("R",CultureInfo.InvariantCulture),"APPLY","DISMISS","SELECT CLEARLIST"};
  }
  internal string[] Commands {get{return commands.ToArray();}}
  internal string Take(int index){if(failed||index!=next||index<0||index>=13)throw new InvalidOperationException("Scale phase already consumed or out of order; no replay.");next++;return commands[index];}
  internal void Abort(){failed=true;}
 }
}

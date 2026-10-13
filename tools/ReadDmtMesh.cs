using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using PowerShapeGeometry;

// Standalone diagnostic CLI. Never registered as an MCP primitive or connected to CAD.
internal static class ReadDmtMesh {
 static string DecodedSha(DmtMeshReader.Mesh mesh) {
  using(var hash=SHA256.Create())using(var crypto=new CryptoStream(Stream.Null,hash,CryptoStreamMode.Write))using(var writer=new BinaryWriter(crypto,System.Text.Encoding.ASCII,true)) {
   writer.Write(mesh.Flags);writer.Write(mesh.Blocks.Count);
   foreach(var block in mesh.Blocks) {writer.Write(block.Flags);writer.Write(block.VertexCount);writer.Write(block.TriangleCount);foreach(double value in block.Vertices)writer.Write(value);foreach(double value in block.Normals)writer.Write(value);foreach(int value in block.Triangles)writer.Write(value);}
   writer.Flush();crypto.FlushFinalBlock();return BitConverter.ToString(hash.Hash).Replace("-","").ToLowerInvariant();
  }
 }
 static int Main(string[] args) {
  try {
   if(args.Length!=1)throw new ArgumentException("One explicit local DMT input required.");string path=Path.GetFullPath(args[0]);
   using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
    if(file.Length==0||file.Length>DmtMeshReader.MaxBytes)throw new InvalidDataException("DMT file size out of supported bounds.");
    string sha;using(var hash=SHA256.Create())sha=BitConverter.ToString(hash.ComputeHash(file)).Replace("-","").ToLowerInvariant();file.Position=0;var mesh=DmtMeshReader.Read(file);
    Console.WriteLine(new JavaScriptSerializer().Serialize(new {state="dmt_mesh_read",sha256=sha,decodedOrderedSha256=DecodedSha(mesh),bytes=file.Length,header=mesh.Header,formatVersion=1000,fileFlags=mesh.Flags,blockCount=mesh.Blocks.Count,vertexCount=mesh.VertexCount,triangleCount=mesh.TriangleCount,lengthUnits=mesh.LengthUnits,coordinateFrame="file_coordinates_unqualified",boundsPaired=mesh.BoundsPaired,blocks=mesh.Blocks.Select(b=>new{flags=b.Flags,vertexCount=b.VertexCount,triangleCount=b.TriangleCount,normalCount=b.Normals.Count/3}),cadConnectionAttempted=false,fullTopologyVerified=false}));return 0;
   }
  }catch(Exception error){Console.Error.WriteLine(error.Message);return 3;}
 }
}

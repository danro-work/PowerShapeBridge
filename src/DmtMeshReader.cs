using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace PowerShapeGeometry {
 // Independently implemented from the documented layout in Autodesk's MIT API source.
 // File geometry only; no COM, unit conversion, mesh cleanup or CAD verification.
 public static class DmtMeshReader {
  public const long MaxBytes=128L*1024*1024;
  public const int MaxBlocks=4096,MaxVertices=1000000,MaxTriangles=2000000;
  public sealed class Block {
   public readonly uint Flags;
   public readonly ReadOnlyCollection<double> Vertices,Normals;
   public readonly ReadOnlyCollection<int> Triangles;
   public int VertexCount {get{return Vertices.Count/3;}}
   public int TriangleCount {get{return Triangles.Count/3;}}
   internal Block(uint flags,double[] vertices,double[] normals,int[] triangles){Flags=flags;Vertices=Array.AsReadOnly(vertices);Normals=Array.AsReadOnly(normals);Triangles=Array.AsReadOnly(triangles);}
  }
  public sealed class Mesh {
   public readonly string Header;
   public readonly ReadOnlyCollection<byte> HeaderBytes;
   public readonly uint Flags;
   public readonly ReadOnlyCollection<Block> Blocks;
   public readonly ReadOnlyCollection<double> BoundsPaired;
   public readonly int VertexCount,TriangleCount;
   public bool MillimetresDeclared {get{return (Flags&2)!=0;}}
   public string LengthUnits {get{return MillimetresDeclared?"mm_declared_by_file_flag":"unknown";}}
   internal Mesh(byte[] header,uint flags,List<Block> blocks,double[] bounds,int vertices,int triangles){HeaderBytes=Array.AsReadOnly(header);Header=Encoding.ASCII.GetString(header);Flags=flags;Blocks=blocks.AsReadOnly();BoundsPaired=Array.AsReadOnly(bounds);VertexCount=vertices;TriangleCount=triangles;}
  }
  static void Require(bool ok,string error){if(!ok)throw new InvalidDataException(error);}
  static int Count(uint value,int maximum,string field){Require(value>0&&value<=maximum,"DMT "+field+" count out of supported bounds.");return (int)value;}
  static double Number(BinaryReader reader,bool single){double value=single?reader.ReadSingle():reader.ReadDouble();Require(!double.IsNaN(value)&&!double.IsInfinity(value),"Nonfinite DMT coordinate/normal.");return value;}
  public static Mesh Read(Stream stream) {
   if(stream==null)throw new ArgumentNullException("stream");Require(stream.CanRead&&stream.CanSeek,"Seekable bounded DMT stream required.");Require(stream.Position==0&&stream.Length>0&&stream.Length<=MaxBytes,"DMT stream size/initial position out of supported bounds.");
   try {
    using(var reader=new BinaryReader(stream,Encoding.ASCII,true)) {
     // Autodesk reads descriptive header bytes up to NUL or 256 bytes. Legacy
     // shipped COPYCAD files include LF; the header is not a geometry signature.
     var header=new List<byte>();for(int i=0;i<256;i++){byte b=reader.ReadByte();if(b==0)break;header.Add(b);}
     ushort version=reader.ReadUInt16();Require(version==1000,"Unsupported DMT version.");uint flags=reader.ReadUInt32();Require((flags&~3u)==0,"Unsupported DMT file flags.");bool single=(flags&1)!=0;
     int blockCount=Count(reader.ReadUInt32(),MaxBlocks,"block"),vertexTotal=Count(reader.ReadUInt32(),MaxVertices,"vertex"),triangleTotal=Count(reader.ReadUInt32(),MaxTriangles,"triangle");
     Require((long)blockCount*14<=stream.Length-stream.Position,"Impossible DMT block inventory.");var blocks=new List<Block>(blockCount);int verticesRead=0,trianglesRead=0;var bounds=new[]{double.PositiveInfinity,double.NegativeInfinity,double.PositiveInfinity,double.NegativeInfinity,double.PositiveInfinity,double.NegativeInfinity};
     for(int blockIndex=0;blockIndex<blockCount;blockIndex++) {
      uint blockFlags=reader.ReadUInt32();Require((blockFlags&~1u)==0,"Unsupported DMT block flags.");bool normals=(blockFlags&1)!=0;int vertices=Count(reader.ReadUInt32(),MaxVertices,"block vertex"),triangles=Count(reader.ReadUInt32(),MaxTriangles,"block triangle");
      Require(vertices<=vertexTotal-verticesRead&&triangles<=triangleTotal-trianglesRead,"DMT block totals exceed file totals.");int scalarBytes=single?4:8,indexBytes=vertices>ushort.MaxValue?4:2;
      long required=(long)vertices*3*scalarBytes*(normals?2:1)+(long)triangles*3*indexBytes+2;Require(required<=stream.Length-stream.Position,"Truncated DMT block payload.");
      var points=new double[vertices*3];var directions=normals?new double[vertices*3]:new double[0];var indices=new int[triangles*3];
      for(int vertex=0;vertex<vertices;vertex++) {
       for(int axis=0;axis<3;axis++){double value=Number(reader,single);points[vertex*3+axis]=value;bounds[axis*2]=Math.Min(bounds[axis*2],value);bounds[axis*2+1]=Math.Max(bounds[axis*2+1],value);}
       if(normals)for(int axis=0;axis<3;axis++)directions[vertex*3+axis]=Number(reader,single);
      }
      for(int index=0;index<indices.Length;index++){uint value=indexBytes==4?reader.ReadUInt32():reader.ReadUInt16();Require(value<(uint)vertices,"DMT triangle index outside block.");indices[index]=(int)value;}
      Require(reader.ReadUInt16()==version,"DMT block trailer version differs.");verticesRead+=vertices;trianglesRead+=triangles;blocks.Add(new Block(blockFlags,points,directions,indices));
     }
     Require(verticesRead==vertexTotal&&trianglesRead==triangleTotal,"DMT totals differ from blocks.");Require(stream.Position==stream.Length,"Trailing DMT bytes are unsupported.");return new Mesh(header.ToArray(),flags,blocks,bounds,verticesRead,trianglesRead);
    }
   }catch(EndOfStreamException error){throw new InvalidDataException("Truncated DMT header/metadata.",error);}
  }
 }
}

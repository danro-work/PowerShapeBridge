using PowerShapeGeometry;
using Xunit;
using System.Text;

namespace PowerShapeMcp.Tests;

public sealed class DmtMeshReaderTests
{
    private static byte[] Synthetic(bool single=false,bool normals=false,int vertices=3,uint flags=2)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream,Encoding.ASCII,true);
        writer.Write(Encoding.ASCII.GetBytes("Synthetic DMT in MM\0"));writer.Write((ushort)1000);
        writer.Write(flags|(single?1u:0u));writer.Write(1u);writer.Write((uint)vertices);writer.Write(1u);
        writer.Write(normals?1u:0u);writer.Write((uint)vertices);writer.Write(1u);
        for(int vertex=0;vertex<vertices;vertex++)
        {
            foreach(double value in new double[]{vertex,vertex==1?2:0,-3})
                if(single)writer.Write((float)value);else writer.Write(value);
            if(normals)foreach(double value in new double[]{0,0,1})
                if(single)writer.Write((float)value);else writer.Write(value);
        }
        foreach(int index in new[]{0,1,vertices-1})if(vertices>ushort.MaxValue)writer.Write((uint)index);else writer.Write((ushort)index);
        writer.Write((ushort)1000);return stream.ToArray();
    }
    private static int Metadata(byte[] bytes)=>Array.IndexOf(bytes,(byte)0)+1;
    private static void WriteUInt(byte[] bytes,int offset,uint value)=>BitConverter.GetBytes(value).CopyTo(bytes,offset);
    [Theory]
    [InlineData(false,false)][InlineData(false,true)][InlineData(true,false)][InlineData(true,true)]
    public void ScalarsNormalsAndIndicesAreDecodedWithoutUnitConversion(bool single,bool normals)
    {
        var stream=new MemoryStream(Synthetic(single,normals));var mesh=DmtMeshReader.Read(stream);
        Assert.True(stream.CanRead);Assert.Single(mesh.Blocks);Assert.Equal(3,mesh.VertexCount);Assert.Equal(1,mesh.TriangleCount);
        Assert.Equal(new double[]{0,2,0,2,-3,-3},mesh.BoundsPaired);Assert.Equal("mm_declared_by_file_flag",mesh.LengthUnits);
        Assert.Equal(normals?9:0,mesh.Blocks[0].Normals.Count);Assert.Equal(new[]{0,1,2},mesh.Blocks[0].Triangles);
    }
    [Fact]
    public void HeaderTextDoesNotQualifyUnits()
    {
        var mesh=DmtMeshReader.Read(new MemoryStream(Synthetic(flags:0)));
        Assert.Contains("in MM",mesh.Header);Assert.Equal("unknown",mesh.LengthUnits);Assert.False(mesh.MillimetresDeclared);
    }
    [Fact]
    public void LegacyLineBreaksInDescriptiveHeaderAreNotMalformedGeometry()
    {
        byte[] original=Synthetic(flags:0);byte[] header=Encoding.ASCII.GetBytes("COPYCAD historical header\n in MM");
        var mesh=DmtMeshReader.Read(new MemoryStream([..header,0,..original[Metadata(original)..]]));
        Assert.Equal("COPYCAD historical header\n in MM",mesh.Header);Assert.Equal(header,mesh.HeaderBytes);Assert.Equal("unknown",mesh.LengthUnits);Assert.Equal(1,mesh.TriangleCount);
    }
    [Fact]
    public void OriginalHeaderBytesSurviveTheAsciiDisplayDecoder()
    {
        byte[] original=Synthetic();byte[] header=[65,128,9,13,10,255];
        var mesh=DmtMeshReader.Read(new MemoryStream([..header,0,..original[Metadata(original)..]]));
        Assert.Equal(header,mesh.HeaderBytes);Assert.Equal(3,mesh.VertexCount);
    }
    [Fact]
    public void DocumentedMaximumLengthHeaderDoesNotRequireANullTerminator()
    {
        byte[] original=Synthetic();byte[] header=Enumerable.Repeat((byte)65,256).ToArray();
        var mesh=DmtMeshReader.Read(new MemoryStream([..header,..original[Metadata(original)..]]));
        Assert.Equal(header,mesh.HeaderBytes);Assert.Equal(new[]{0,1,2},mesh.Blocks[0].Triangles);
    }
    [Theory][InlineData(65535)][InlineData(65536)]
    public void BlockVertexCountDeterminesIndexWidth(int vertices)
    {
        var mesh=DmtMeshReader.Read(new MemoryStream(Synthetic(vertices:vertices)));
        Assert.Equal(vertices-1,mesh.Blocks[0].Triangles[2]);Assert.Equal(vertices,mesh.VertexCount);
    }
    [Theory]
    [InlineData("version")][InlineData("file_flags")][InlineData("block_flags")]
    [InlineData("zero_blocks")][InlineData("huge_blocks")][InlineData("huge_vertices")][InlineData("huge_triangles")]
    [InlineData("file_totals")][InlineData("block_totals")][InlineData("index")][InlineData("negative_index")]
    [InlineData("nonfinite_point")][InlineData("nonfinite_normal")][InlineData("trailer")][InlineData("trailing")]
    public void MalformedOrUnsupportedEvidenceIsRejected(string fault)
    {
        byte[] bytes=Synthetic(normals:true);int meta=Metadata(bytes),block=meta+18,points=block+12,indices=points+3*6*8;
        switch(fault)
        {
            case "version": bytes[meta]=0;break;
            case "file_flags": WriteUInt(bytes,meta+2,4);break;
            case "block_flags": WriteUInt(bytes,block,2);break;
            case "zero_blocks": WriteUInt(bytes,meta+6,0);break;
            case "huge_blocks": WriteUInt(bytes,meta+6,uint.MaxValue);break;
            case "huge_vertices": WriteUInt(bytes,meta+10,uint.MaxValue);break;
            case "huge_triangles": WriteUInt(bytes,meta+14,uint.MaxValue);break;
            case "file_totals": WriteUInt(bytes,meta+10,4);break;
            case "block_totals": WriteUInt(bytes,block+8,2);break;
            case "index": bytes[indices]=3;break;
            case "negative_index": bytes[indices]=255;bytes[indices+1]=255;break;
            case "nonfinite_point": BitConverter.GetBytes(double.NaN).CopyTo(bytes,points);break;
            case "nonfinite_normal": BitConverter.GetBytes(double.PositiveInfinity).CopyTo(bytes,points+24);break;
            case "trailer": bytes[^1]=0;break;
            case "trailing": bytes=[..bytes,0];break;
        }
        Assert.Throws<InvalidDataException>(()=>DmtMeshReader.Read(new MemoryStream(bytes)));
    }
    [Fact]
    public void EveryTruncationOfAnOfficialFileIsRejected()
    {
        byte[] bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","dmt","NormalDmt.dmt"));
        for(int length=0;length<bytes.Length;length++)Assert.Throws<InvalidDataException>(()=>DmtMeshReader.Read(new MemoryStream(bytes[..length])));
    }
    [Theory][InlineData("NormalDmt",1,8,12)][InlineData("TwoBlocks",2,8,4)][InlineData("smallModel",1,80,123)]
    public void OfficialAutodeskFixturesHaveExpectedStructure(string name,int blocks,int vertices,int triangles)
    {
        using var stream=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"fixtures","dmt",name+".dmt"));var mesh=DmtMeshReader.Read(stream);
        Assert.Equal(blocks,mesh.Blocks.Count);Assert.Equal(vertices,mesh.VertexCount);Assert.Equal(triangles,mesh.TriangleCount);Assert.Equal("unknown",mesh.LengthUnits);
        if(name=="NormalDmt")Assert.Equal(new double[]{-124,-54,41,111,0,70},mesh.BoundsPaired);
        if(name=="TwoBlocks")Assert.Equal(new double[]{-62.08,78.64,5.70,76.08,0,0},mesh.BoundsPaired.Select(x=>Math.Round(x,2)));
    }
    [Fact]
    public void NonzeroInitialPositionCannotProducePartialFileEvidence()
    {
        var stream=new MemoryStream(Synthetic());stream.Position=1;Assert.Throws<InvalidDataException>(()=>DmtMeshReader.Read(stream));
    }
}

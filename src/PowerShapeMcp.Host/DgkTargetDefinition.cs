using System.Text.Json.Nodes;
using PowerShapeGeometry;

namespace PowerShapeMcp;

internal static class DgkTargetDefinition
{
    private const string Code="dgk_target_parameters_invalid";
    private static string Text(JsonObject value,string key)=>ArtifactProof.String(value,key,Code);
    internal static JsonObject Read(JsonObject value)
    {
        if(!ArtifactProof.Fields(value,"coordinateFrame","exportVariant","triangleCompanions","featureCompanions","settingsQualification")||
           Text(value,"coordinateFrame")!="world"||Text(value,"settingsQualification")!="planned_unqualified"||
           Text(value,"exportVariant") is not ("geometry" or "geometry_features")||
           value["triangleCompanions"] is not JsonArray triangles||triangles.Count>2||
           value["featureCompanions"] is not JsonArray features||features.Count>2)throw new ReferenceRejected(Code);
        const string stem="Geometry_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        List<DgkBundleVerifier.Artifact> artifacts=[new(stem+".dgk","geometry_dgk",true)];
        foreach(var raw in triangles)
        {
            if(raw is not JsonObject item||!ArtifactProof.Fields(item,"suffix","required")||Text(item,"suffix") is not (".dmt" or ".dgk.dmt")||
               item["required"] is not JsonValue flag||!flag.TryGetValue<bool>(out var required))throw new ReferenceRejected(Code);
            artifacts.Add(new(stem+item["suffix"]!.GetValue<string>(),"triangles_dmt",required));
        }
        foreach(var raw in features)
        {
            if(raw is not JsonObject item||!ArtifactProof.Fields(item,"name","role","required")||Text(item,"role") is not ("features_mac" or "features_xml")||
               item["name"] is not JsonValue name||!name.TryGetValue<string>(out var text)||
               item["required"] is not JsonValue flag||!flag.TryGetValue<bool>(out var required))throw new ReferenceRejected(Code);
            artifacts.Add(new(text,item["role"]!.GetValue<string>(),required));
        }
        if(value["exportVariant"]!.GetValue<string>()=="geometry"?features.Count!=0:
           features.Count==0||!artifacts.Any(item=>item.Role.StartsWith("features_",StringComparison.Ordinal)&&item.Required))throw new ReferenceRejected(Code);
        try{_ = new DgkBundleVerifier.Plan(stem,artifacts);}catch(InvalidDataException){throw new ReferenceRejected(Code);}
        return value.DeepClone().AsObject();
    }
    internal static JsonArray Manifest(string id,JsonObject parameters)
    {
        if(!FileArtifacts.Token(id))throw new ReferenceRejected(Code);parameters=Read(parameters);
        var stem="Geometry_"+id;var result=new JsonArray();
        JsonObject Row(string name,string role,bool required)=>new(){["name"]=name,["role"]=role,["required"]=required,["expectedSha256"]=""};
        result.Add(Row(stem+".dgk","geometry_dgk",true));
        foreach(var raw in parameters["triangleCompanions"]!.AsArray())result.Add(Row(stem+raw!["suffix"]!.GetValue<string>(),"triangles_dmt",raw["required"]!.GetValue<bool>()));
        foreach(var raw in parameters["featureCompanions"]!.AsArray())result.Add(Row(raw!["name"]!.GetValue<string>(),raw["role"]!.GetValue<string>(),raw["required"]!.GetValue<bool>()));
        return result;
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

// SDK transport/binding remains in the SDK; this wrapper enforces our exact inputs,
// connection admission and mode policy before invoking any domain/IPC handler.
public sealed class StrictTool : DelegatingMcpServerTool
{
    private readonly BridgeSession session;
    private readonly JsonObject schema;
    public StrictTool(McpServerTool inner, BridgeSession session, JsonObject schema) : base(inner)
    {
        this.session=session;
        this.schema=schema;
        ProtocolTool.InputSchema=JsonSerializer.SerializeToElement(schema);
    }
    public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request,CancellationToken cancellationToken=default)
    {
        session.AssertToolAllowed(ProtocolTool.Name);
        var arguments=request.Params?.Arguments;
        if(!Valid(arguments))throw new McpProtocolException("Invalid tool arguments.",McpErrorCode.InvalidParams);
        using var admission=session.AcquireCall(request.JsonRpcRequest.Id);
        return await base.InvokeAsync(request,cancellationToken);
    }
    private bool Valid(IDictionary<string,JsonElement>? arguments)
    {
        var properties=schema["properties"]!.AsObject();
        if((arguments?.Count??0)!=properties.Count)return false;
        foreach(var (name,definition) in properties)
        {
            if(arguments is null||!arguments.TryGetValue(name,out var value))return false;
            var rule=definition!.AsObject();
            if(rule["type"]!.GetValue<string>()=="string")
            {
                if(value.ValueKind!=JsonValueKind.String||!FileArtifacts.Token(value.GetString()))return false;
            }
            else if(rule["type"]!.GetValue<string>()=="array")
            {
                if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength() is <1 or >128)return false;
                var seen=new HashSet<string>(StringComparer.Ordinal);
                foreach(var element in value.EnumerateArray())
                    if(element.ValueKind!=JsonValueKind.String||!FileArtifacts.Token(element.GetString())||!seen.Add(element.GetString()!))return false;
            }
            else return false;
        }
        return true;
    }
    public static JsonObject Empty()=>new(){["type"]="object",["properties"]=new JsonObject(),["additionalProperties"]=false};
    public static JsonObject Token(string name,string description)=>new()
    {
        ["type"]="object",["properties"]=new JsonObject{[name]=TokenProperty(description)},
        ["required"]=new JsonArray(name),["additionalProperties"]=false
    };
    public static JsonObject ReadReferences()=>new()
    {
        ["type"]="object",["properties"]=new JsonObject
        {
            ["documentReference"]=TokenProperty("Read document reference from inspect_active_model in this connection; not a candidate or mutation handle."),
            ["entityReferences"]=new JsonObject{["type"]="array",["items"]=new JsonObject{["type"]="string",["pattern"]="^[0-9a-f]{32}$"},["minItems"]=1,["maxItems"]=128,["uniqueItems"]=true,
                ["description"]="One to 128 distinct read entity references belonging to that document and this connection."}
        },["required"]=new JsonArray("documentReference","entityReferences"),["additionalProperties"]=false
    };
    private static JsonObject TokenProperty(string description)=>new(){["type"]="string",["pattern"]="^[0-9a-f]{32}$",["description"]=description};
}

using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
namespace PowerShapeMcp;
internal static class PowerShapeProcessPinOptions
{
    internal static void Validate(JsonElement config)
    {
        if(!config.TryGetProperty("powerShapeProcessPin",out var pin)||pin.ValueKind==JsonValueKind.Null)return;
        if(pin.ValueKind!=JsonValueKind.Object||!pin.EnumerateObject().Select(p=>p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(["processId","processStartedUtc","windowsSessionId","executablePath"])||pin.EnumerateObject().Count()!=4||pin.GetProperty("processId").ValueKind!=JsonValueKind.Number||!pin.GetProperty("processId").TryGetInt32(out var pid)||pid<=0||pin.GetProperty("windowsSessionId").ValueKind!=JsonValueKind.Number||!pin.GetProperty("windowsSessionId").TryGetInt32(out var session)||session<0||pin.GetProperty("processStartedUtc").ValueKind!=JsonValueKind.String||pin.GetProperty("executablePath").ValueKind!=JsonValueKind.String)throw new InvalidDataException("Exact typed PowerShape process pin required.");
        var stamp=pin.GetProperty("processStartedUtc").GetString()!;var path=pin.GetProperty("executablePath").GetString()!;
        if(!Regex.IsMatch(stamp,@"\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)\z")||!DateTimeOffset.TryParse(stamp,CultureInfo.InvariantCulture,DateTimeStyles.None,out _)||path.Length>1024||!Regex.IsMatch(path,@"\A[A-Za-z]:\\")||!string.Equals(Path.GetFullPath(path),path,StringComparison.OrdinalIgnoreCase)||!string.Equals(Path.GetFileName(path),"powershape.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Canonical PowerShape executable and explicit UTC creation time required.");
    }
}

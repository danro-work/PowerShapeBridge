using System.Security.Cryptography;
using System.Text.Json;

namespace PowerShapeMcp;

public static class FileArtifacts
{
    public static string FixedPath(string root, params string[] parts)
    {
        root = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine([root, .. parts]));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Artifact is outside Bridge root.");
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse artifact paths are refused.");
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
        }
        return path;
    }

    public static byte[] ReadBytes(string root, string relative, int limit)
    {
        using var stream = new FileStream(FixedPath(root, relative), FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadBounded(stream,limit);
    }
    private static byte[] ReadBounded(FileStream stream,int limit)
    {
        if (stream.Length > limit) throw new InvalidDataException("Artifact exceeds size limit.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Artifact grew during read.");
        return bytes;
    }

    internal static FileStream OpenAdvisoryReport(string root,string name)
    {
        if(name is not ("panel-status.json" or "worker-stage.json" or "deployment-current.json"))throw new InvalidDataException("Unknown fixed advisory report.");
        return new FileStream(FixedPath(root,"reports",name),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
    }
    public static JsonElement ReadAdvisoryReport(string root,string name)
    {
        // These reports are diagnostics, never proof/lease authority. Permit atomic
        // publisher replacement while this stream retains the original file bytes.
        using var stream=OpenAdvisoryReport(root,name);return Parse(ReadBounded(stream,1000000));
    }

    public static JsonElement Parse(byte[] bytes)
    {
        ReadOnlyMemory<byte> data = bytes;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) data = data[3..];
        using var doc = JsonDocument.Parse(data);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected an object artifact.");
        return doc.RootElement.Clone();
    }

    public static JsonElement ReadJson(string root, string relative, int limit) => Parse(ReadBytes(root, relative, limit));
    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static bool Token(string? value, int length = 32) => value is not null && value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

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
        if (stream.Length > limit) throw new InvalidDataException("Artifact exceeds size limit.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Artifact grew during read.");
        return bytes;
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

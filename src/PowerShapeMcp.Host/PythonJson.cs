using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PowerShapeMcp;

// Python's ensure_ascii=False/default separators for legacy journal details limits.
// Never use this to compute a native receipt hash: hash the original bytes.
public static class PythonJson
{
    private static readonly JsonSerializerOptions Strings = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static string Dump(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(", ", value.EnumerateObject().Select(p => JsonSerializer.Serialize(p.Name, Strings) + ": " + Dump(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(Dump)) + "]",
        JsonValueKind.String => JsonSerializer.Serialize(value.GetString(), Strings),
        JsonValueKind.Number => Number(value.GetRawText()),
        JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Null => "null",
        _ => throw new InvalidDataException("operation_journal_details_invalid")
    };
    private static string Number(string token)
    {
        if (!token.Contains('.') && !token.Contains('e') && !token.Contains('E')) return token;
        var number = double.Parse(token, CultureInfo.InvariantCulture);
        if (!double.IsFinite(number)) throw new InvalidDataException("operation_journal_details_invalid");
        var text = number.ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant();
        var sign = text.StartsWith('-') ? "-" : "";
        text = text.TrimStart('-');
        var parts = text.Split('e');
        var exponent = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        var dot = parts[0].IndexOf('.');
        var digits = parts[0].Replace(".", "");
        var decimalPosition = (dot >= 0 ? dot : digits.Length) + exponent;
        var zeros = digits.TakeWhile(c => c == '0').Count();
        digits = digits[zeros..]; decimalPosition -= zeros;
        if (digits.Length == 0) return sign + "0.0";
        var scale = decimalPosition - 1;
        if (scale < -4 || scale >= 16)
        {
            digits = digits.TrimEnd('0');
            return sign + digits[0] + (digits.Length > 1 ? "." + digits[1..] : "") + "e" + (scale < 0 ? "-" : "+") + Math.Abs(scale).ToString("D2", CultureInfo.InvariantCulture);
        }
        if (decimalPosition <= 0) return sign + "0." + new string('0', -decimalPosition) + digits;
        if (decimalPosition >= digits.Length) return sign + digits + new string('0', decimalPosition - digits.Length) + ".0";
        return sign + digits[..decimalPosition] + "." + digits[decimalPosition..];
    }
}

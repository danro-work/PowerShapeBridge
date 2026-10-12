using System.Text.Json;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class PythonJsonTests
{
    [Theory]
    [InlineData("1.00", "1.0")]
    [InlineData("-0.0", "-0.0")]
    [InlineData("1e-4", "0.0001")]
    [InlineData("1e-5", "1e-05")]
    [InlineData("1e16", "1e+16")]
    [InlineData("1e15", "1000000000000000.0")]
    [InlineData("1.2345e-7", "1.2345e-07")]
    [InlineData("1.2345e20", "1.2345e+20")]
    [InlineData("12345678901234567890", "12345678901234567890")]
    [InlineData("{\"note\":\"Тест\",\"values\":[1,1.0,null,true]}", "{\"note\": \"Тест\", \"values\": [1, 1.0, null, true]}")]
    public void LegacyDetailsFormatting(string json, string expected)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, PythonJson.Dump(doc.RootElement));
    }
}

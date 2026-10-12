using System.Globalization;
using System.Text.RegularExpressions;

namespace PowerShapeMcp;

public static class LeaseTime
{
    public static DateTimeOffset PythonPrecision(DateTimeOffset value)=>new(value.Ticks-value.Ticks%10,value.Offset);
    public static DateTimeOffset Parse(string? value,string code)
    {
        if(value is null||!(value.EndsWith('Z')||Regex.IsMatch(value,@"[+-]\d\d:\d\d$"))||!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var time))
            throw new ReferenceRejected(code);
        return PythonPrecision(time);
    }
    public static string Format(DateTimeOffset value)
    {
        value=PythonPrecision(value);
        var fraction=value.Ticks%TimeSpan.TicksPerSecond;
        var offset=value.Offset;
        return value.ToString("yyyy-MM-dd'T'HH:mm:ss",CultureInfo.InvariantCulture)+(fraction==0?"":"."+(fraction/10).ToString("D6",CultureInfo.InvariantCulture))+
               (offset<TimeSpan.Zero?"-":"+")+offset.Duration().ToString(@"hh\:mm",CultureInfo.InvariantCulture);
    }
    public static (DateTimeOffset Expiry,double Remaining) Remaining(string? timestamp,DateTimeOffset now,string code)
    {
        var expiry=Parse(timestamp,code).AddSeconds(300);
        var seconds=(expiry-PythonPrecision(now)).TotalSeconds;
        if(seconds<=0||seconds>305)throw new ReferenceRejected(code);
        return(expiry,seconds);
    }
}

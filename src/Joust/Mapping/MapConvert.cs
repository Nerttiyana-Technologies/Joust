using System.Collections;
using System.Globalization;

namespace Joust.Mapping;

/// <summary>
/// Value coercion shared by the runtime mapper and source-generated mappers, converting engine
/// values into POCO property types.
/// </summary>
public static class MapConvert
{
    /// <summary>Converts <paramref name="value"/> to <typeparamref name="T"/>.</summary>
    public static T To<T>(object value) => (T)ConvertTo(value, typeof(T))!;

    /// <summary>Converts <paramref name="value"/> to <paramref name="target"/> (handling nullable, enum, Guid, vectors).</summary>
    public static object? ConvertTo(object value, Type target)
    {
        var underlying = Nullable.GetUnderlyingType(target) ?? target;

        if (underlying.IsInstanceOfType(value))
            return value;

        if (underlying == typeof(float[]))
        {
            if (value is float[] fa) return fa;
            if (value is IEnumerable<float> ef) return ef.ToArray();
            if (value is IEnumerable<double> ed) return ed.Select(d => (float)d).ToArray();
            if (value is IEnumerable e) return e.Cast<object>().Select(o => Convert.ToSingle(o, CultureInfo.InvariantCulture)).ToArray();
        }

        try
        {
            if (underlying.IsEnum)
                return Enum.ToObject(underlying, value);
            if (underlying == typeof(Guid))
                return value is string gs ? Guid.Parse(gs) : value;
            return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
        }
        catch
        {
            return value;
        }
    }
}

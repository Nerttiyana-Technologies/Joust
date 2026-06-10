using System.Text;

namespace Joust.Schema;

/// <summary>Helpers for deriving physical column names from CLR member names.</summary>
public static class Naming
{
    /// <summary>
    /// Converts a PascalCase or camelCase identifier to snake_case.
    /// Examples: <c>IsActive</c> → <c>is_active</c>, <c>Price</c> → <c>price</c>,
    /// <c>HTTPStatusCode</c> → <c>http_status_code</c>.
    /// </summary>
    public static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var sb = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                bool prevLower = i > 0 && char.IsLower(name[i - 1]);
                bool nextLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (i > 0 && (prevLower || nextLower))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}

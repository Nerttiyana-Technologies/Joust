namespace Joust;

/// <summary>
/// Overrides the physical column name for a property. When absent, the column name is
/// derived from the property name using snake_case (e.g. <c>IsActive</c> → <c>is_active</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ColumnAttribute : Attribute
{
    /// <summary>Creates the attribute with an explicit column name.</summary>
    public ColumnAttribute(string name) => Name = name;

    /// <summary>The physical column name.</summary>
    public string Name { get; }
}

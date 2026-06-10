namespace Joust;

/// <summary>
/// Marks the property that uniquely identifies a row. Used as the merge/upsert key.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class KeyAttribute : Attribute
{
}

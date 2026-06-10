namespace Joust;

/// <summary>
/// Opts a record type into source-generated, AOT-friendly mapping. When the Joust source generator
/// is referenced, types marked with this attribute get a compiled <c>IRowMapper</c> registered at
/// startup, avoiding reflection entirely. Without the generator, mapping still works via cached
/// compiled delegates.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class JoustRecordAttribute : Attribute
{
}

using Joust;
using Joust.Mapping;

// The source generator emits a mapper for SampleDoc and registers it at module init.
bool registered = MapperRegistry.IsRegistered(typeof(SampleDoc));
Console.WriteLine($"SampleDoc generated mapper registered: {registered}");

var mapper = new PocoMapper<SampleDoc>(Joust.Schema.SchemaCompiler.Compile<SampleDoc>("docs"));
var row = mapper.ToRow(new SampleDoc { Id = 5, Text = "hello", Embedding = [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f] });
var back = mapper.FromRow(row);
Console.WriteLine($"Round-trip id={back.Id}, text={back.Text}, dims={back.Embedding.Length}");

return registered ? 0 : 1;

[JoustRecord]
public sealed class SampleDoc
{
    [Key] public int Id { get; set; }
    [Column("body")] public string Text { get; set; } = "";
    [Vector(8)] public float[] Embedding { get; set; } = [];
}

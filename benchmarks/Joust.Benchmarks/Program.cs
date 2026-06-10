using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Joust;
using Joust.Linq;
using Joust.Mapping;
using Joust.Schema;

BenchmarkRunner.Run<JoustBenchmarks>();

/// <summary>
/// Performance gates from the architecture document:
///   PERF-1 — a LINQ predicate translates in under 5 µs.
///   PERF-2 — record mapping avoids per-call reflection (cached compiled delegates).
/// Run with: <c>dotnet run -c Release --project benchmarks/Joust.Benchmarks</c>
/// </summary>
[MemoryDiagnoser]
public class JoustBenchmarks
{
    private static readonly TableSchema Schema = SchemaCompiler.Compile<Doc>("docs");
    private static readonly Expression<Func<Doc, bool>> Predicate = x => x.Price > 50 && x.Category == "Tools";

    private readonly PocoMapper<Doc> _mapper = new(Schema);
    private readonly Doc _doc = new() { Id = 1, Price = 99, Category = "Tools", Embedding = new float[1536] };
    private Dictionary<string, object?> _row = null!;

    [GlobalSetup]
    public void Setup() => _row = _mapper.ToRow(_doc);

    // PERF-1: expression translation budget.
    [Benchmark]
    public string TranslateFilter() => FilterTranslator.Translate(Predicate, Schema);

    // PERF-2: write/read mapping throughput and allocations.
    [Benchmark]
    public Dictionary<string, object?> MapToRow() => _mapper.ToRow(_doc);

    [Benchmark]
    public Doc MapFromRow() => _mapper.FromRow(_row);

    public sealed class Doc
    {
        [Key] public int Id { get; set; }
        public double Price { get; set; }
        public string Category { get; set; } = "";
        [Vector(1536)] public float[] Embedding { get; set; } = [];
    }
}

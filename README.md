# Joust

An idiomatic, high-performance **.NET client and LINQ provider for [LanceDB](https://lancedb.github.io/lancedb/)**.

Joust lets you work with LanceDB using plain C# classes, attributes, and compile-checked LINQ
queries instead of raw Apache Arrow buffers and string filters. See
[`docs/`](docs) and the architecture document for the full design.

> **Status: early scaffold (`0.1.0-alpha`).** This milestone delivers the pure-managed core —
> attribute schema, the LINQ filter/vector engine, and a complete in-memory backend — behind an
> `INativeEngine` abstraction. The production **P/Invoke** backend that calls the native LanceDB
> Rust engine drops in behind the same interface without changing application code (see ADR-1/ADR-7
> in the architecture document).

## Quick start

```csharp
using Joust;

public sealed class DocumentModel
{
    [Key] public Guid Id { get; set; }
    [Column("body")] public string Text { get; set; } = "";
    [LanceIndex(IndexType.Scalar)] public string Category { get; set; } = "";
    [Vector(Dimensions = 1536, Metric = DistanceMetric.Cosine)] public float[] Embedding { get; set; } = [];
}

// Backend chosen by the connection string (ADR-7). Here: the in-memory backend.
await using var ctx = await LanceDbContext.ConnectAsync("memory://app",
    new LanceConnectOptions { UseInMemoryEngine = true });

var table = await ctx.CreateTableAsync<DocumentModel>("documents");
await table.AddAsync(documents);

// Compile-checked filter + nearest-neighbour vector search.
var hits = await table
    .Where(d => d.Category == "Tools")
    .OrderBy(d => d.Embedding.CosineDistance(queryVector))
    .Take(5)
    .ToListAsync();
```

The same code runs against the embedded engine (`"/data/app.lance"`, `"s3://…"`) or a distributed
cluster (`"db://…"`) once the native backend is wired — only the connection string changes.

## Build & test

Requires the **.NET SDK 10.0.300** (pinned in `global.json`); the libraries multi-target
`net8.0`, `net9.0`, and `net10.0`.

```bash
dotnet build
dotnet test
```

## Project layout

| Project | Purpose |
| :--- | :--- |
| `src/Joust` | Core: attributes, schema compiler, LINQ engine, native abstraction, in-memory backend. |
| `src/Joust.SemanticKernel` | Semantic Kernel adapter (skeleton). |
| `src/Joust.VectorData` | `Microsoft.Extensions.VectorData` adapter (skeleton). |
| `tests/Joust.Tests` | xUnit tests: translator, schema, naming, end-to-end. |

## What works today

- POCO → schema compilation from `[Key]`, `[Vector]`, `[LanceIndex]`, `[Column]`.
- LINQ filter translation (`Where`) with compile-safe property references; unsupported expressions
  fail loudly instead of silently scanning.
- Nearest-neighbour vector search via `OrderBy(x => x.Embedding.CosineDistance(q))`.
- Full CRUD, counting, and streaming against the in-memory backend.

## Roadmap

1. P/Invoke `NativeEngine` over the LanceDB Rust FFI + RID-based native packaging.
2. Source-generated POCO mapper (replace reflection on the hot path).
3. Complete Semantic Kernel and VectorData adapter implementations.
4. Full-text and hybrid search; index management surface.

## License

Apache-2.0. See [LICENSE](LICENSE).

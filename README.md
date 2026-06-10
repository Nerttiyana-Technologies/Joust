# Joust

An idiomatic, high-performance **.NET client and LINQ provider for [LanceDB](https://lancedb.github.io/lancedb/)**.

Joust lets you work with LanceDB using plain C# classes, attributes, and compile-checked LINQ
queries instead of raw Apache Arrow buffers and string filters.

[![CI](https://github.com/Nerttiyana-Technologies/Joust/actions/workflows/ci.yml/badge.svg)](https://github.com/Nerttiyana-Technologies/Joust/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Joust.svg)](https://www.nuget.org/packages/Joust)

What you get: POCO models with `[Vector]` attributes, compile-checked LINQ filters and vector
search, full-text and hybrid (RRF) search, a `Microsoft.Extensions.VectorData` / Semantic Kernel
connector, an AOT-friendly source-generated mapper, and bundled native binaries (no Rust toolchain
required to consume). The backend — in-memory, embedded LanceDB, object storage, or a remote
cluster — is selected by the connection string.

## Install

```bash
dotnet add package Joust
# optional AI-framework connector (also pulls Microsoft.Extensions.VectorData)
dotnet add package Joust.VectorData
```

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

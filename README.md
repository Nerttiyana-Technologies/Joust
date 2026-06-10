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

The same code runs against the embedded LanceDB engine (`"/data/app.lance"`, `"s3://…"`) or a
distributed cluster (`"db://…"`) — only the connection string changes. Switch on the native engine
with `new LanceConnectOptions { EnableNativeBackend = true }`; the per-RID binaries are bundled, so
no Rust toolchain is needed to consume the package.

## Features

- **POCO-first schema** from `[Key]`, `[Vector]`, `[LanceIndex]`, `[Column]` attributes — no manual
  Apache Arrow buffers.
- **Compile-checked LINQ** — `Where` filters with refactor-safe property references; unsupported
  expressions fail loudly instead of silently degrading to a full scan.
- **Vector search** — `OrderBy(x => x.Embedding.CosineDistance(q))` or `table.VectorSearch(vector)`.
- **Full-text & hybrid search** — `NearestToText(...)`, and vector + full-text fused with Reciprocal
  Rank Fusion (`.Rerank(k)`).
- **CRUD, merge-insert (upsert), counting, index management**, and streaming async results.
- **Two backends, one API** — a pure-managed in-memory engine for dev/test and the native LanceDB
  engine (C# → Rust → LanceDB, zero-copy Arrow), selected by the connection string.
- **AI connector** — implements `Microsoft.Extensions.VectorData` (which is also the Semantic Kernel
  vector-store connector).
- **AOT-friendly, source-generated mapping** for `[JoustRecord]` types (reflection-free).
- **Zero-config native deployment** — per-RID binaries shipped inside the NuGet package.

## Build & test

Requires the **.NET SDK 10.0.300** (pinned in `global.json`); the libraries multi-target
`net8.0`, `net9.0`, and `net10.0`.

```bash
dotnet build
dotnet test                 # in-memory backend; no native binary required
```

To build the native LanceDB shim (needs the Rust toolchain) and run the native-backed tests:

```bash
./build-native.sh
JOUST_NATIVE_DATA=1 dotnet test
```

## Project layout

| Project | Purpose |
| :--- | :--- |
| `src/Joust` | Core: attributes, schema compiler, LINQ engine, interop, in-memory + native backends, compiled mapper. |
| `src/Joust.VectorData` | `Microsoft.Extensions.VectorData` / Semantic Kernel connector. |
| `src/Joust.SemanticKernel` | Semantic Kernel integration helper. |
| `src/Joust.SourceGenerators` | Roslyn source generator for AOT-friendly `[JoustRecord]` mappers. |
| `tests/Joust.Tests` | xUnit tests (in-memory + opt-in native). |
| `benchmarks/Joust.Benchmarks` | BenchmarkDotNet PERF gates. |
| `native/shim` | Rust `cdylib` wrapping the LanceDB engine (C ABI in `native/joust_lancedb.h`). |

## Roadmap

Post-1.0 enhancements are tracked in [`BACKLOG.md`](BACKLOG.md): cloud storage-options wiring,
embedding generation in `SearchAsync` (string inputs), and broader LINQ translator coverage.

## License

Apache-2.0. See [LICENSE](LICENSE).

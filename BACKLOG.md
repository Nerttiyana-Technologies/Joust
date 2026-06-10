# Joust — Backlog

Tracked follow-ups beyond the initial version. None are blockers; the library is functional and
tested (in-memory and native LanceDB backends, AI connector, source-gen mapper).

## Native backend

- [ ] **Native scalar ORDER BY / OFFSET pushdown.** Currently applied client-side in `NativeEngine`
      after materializing results (LanceDB has no generic ORDER BY on the query builder). Push down
      where the engine supports it.
- [ ] **Zero-copy Arrow C Data Interface.** The data path currently serializes Arrow IPC bytes across
      the FFI. Move to the Arrow C Data Interface (`ArrowArrayStream`) for true zero-copy.
- [ ] **Storage options wiring.** `joust_connect_with_options` ignores the JSON storage options
      (cloud credentials, region, etc.); thread them into `lancedb::connect`.
- [ ] **Score surfacing.** Vector search currently returns `VectorSearchResult.Score = null`; surface
      the `_distance` column from LanceDB results.

## AI connector (Microsoft.Extensions.VectorData / Semantic Kernel)

- [ ] **Dynamic (dictionary) collections** — implement `GetDynamicCollection`.
- [ ] **`ListCollectionNamesAsync`** — currently returns empty; add a table-listing call on the
      context/engine and implement it.
- [ ] **Embedding generation** — `SearchAsync` currently requires a float vector; support string
      inputs via an `IEmbeddingGenerator` when configured.

## Search features

- [ ] **Full-text search** and **hybrid search** (vector + FTS with reranking).

## Packaging & distribution

- [ ] **Native binaries for all RIDs** — build `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`
      (and consider `osx-x64`, `linux-musl`) and wire a CI build matrix.
- [ ] **Source generator packaging** — ship `Joust.SourceGenerators` as an analyzer inside the
      `Joust` NuGet so `[JoustRecord]` mappers are generated transitively for consumers.
- [ ] **NuGet pack + release pipeline** — versioning, symbols, and a GitHub Actions release job.

## Quality

- [ ] **Cross-platform CI** running `dotnet test` (and native lifecycle/data tests where a binary is
      available) on Linux/Windows/macOS.
- [ ] **More LINQ coverage** in the filter translator (string methods, date comparisons, nested
      predicates) with a documented support matrix.

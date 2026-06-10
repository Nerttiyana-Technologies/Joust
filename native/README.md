# Joust native shim

This folder holds the native side of Joust: the C ABI contract (`joust_lancedb.h`) and the
per-RID binaries the managed layer loads via P/Invoke.

## How loading works

At startup, `JoustNativeResolver` installs a `SetDllImportResolver` (ADR-4). When the runtime needs
the `joust_lancedb` library, the resolver:

1. computes the current RID with `RidResolver.Current` (e.g. `osx-arm64`),
2. maps the base name to the platform file name (`libjoust_lancedb.dylib` / `.so` / `.dll`),
3. probes `runtimes/<rid>/native/` next to the assembly and the app base directory, and
4. loads the match, or throws `NativeLibraryNotFoundException` listing every path it tried.

## RID → file name

| RID | Native file |
| :--- | :--- |
| `win-x64`, `win-arm64` | `joust_lancedb.dll` |
| `linux-x64`, `linux-arm64` | `libjoust_lancedb.so` |
| `osx-arm64` | `libjoust_lancedb.dylib` |

## Expected layout

Place built binaries here so the package and local builds pick them up:

```
native/runtimes/osx-arm64/native/libjoust_lancedb.dylib
native/runtimes/linux-x64/native/libjoust_lancedb.so
native/runtimes/win-x64/native/joust_lancedb.dll
...
```

`src/Joust/Joust.csproj` packs anything under `native/runtimes/**` into the NuGet
`runtimes/<rid>/native/` layout and copies it to the build output. The managed build works with no
binaries present (the in-memory backend needs none).

## Building the shim (you have Rust installed)

The shim crate lives in `native/shim` — a `cdylib` that wraps the `lancedb` crate and exposes the
functions in `joust_lancedb.h` as `extern "C"`. A helper script builds it and drops the artifact in
the right RID folder:

```bash
# from the repo root
./build-native.sh            # auto-detects your RID (osx-arm64 on an M-series Mac)
```

That runs `cargo build --release` in `native/shim` and copies
`libjoust_lancedb.dylib` to `native/runtimes/osx-arm64/native/`.

First time, you may want to refresh the crate versions to match the current API:

```bash
cd native/shim
cargo add lancedb tokio      # picks latest compatible versions
cd ../.. && ./build-native.sh
```

Then rebuild .NET so the binary flows into the build/test output, and run the suite — the
`NativeLifecycleTests` (skipped until now) will execute the real C# → Rust → LanceDB path:

```bash
dotnet build
dotnet test
```

To use it from an app, just enable the native backend (the connection string still selects the
backend — ADR-7):

```csharp
await using var ctx = await LanceDbContext.ConnectAsync(
    "/data/app.lance",
    new LanceConnectOptions { EnableNativeBackend = true });
```

> Status: the connection/table lifecycle (connect, exists, drop, open, count, delete) is wired
> through `NativeEngine` and `SafeHandle`s. The row data path (add/merge/query) goes over the Arrow
> C Data Interface and is the next implementation step — those calls throw a clear error until then.
>
> Because I can't compile Rust in the authoring environment, expect the shim to need a fix or two
> against the `lancedb` crate's exact current API on the first `cargo build`. Paste any errors and
> they're quick to resolve.

using Joust.Interop;
using Joust.Schema;

namespace Joust;

/// <summary>
/// The entry point to Joust: owns a connection to a backend and exposes typed tables. The backend
/// (embedded, object-storage, or remote distributed) is selected from the connection URI per ADR-7;
/// application code is identical across all of them.
/// </summary>
public sealed class LanceDbContext : IAsyncDisposable
{
    private readonly INativeEngine _engine;

    private LanceDbContext(string uri, INativeEngine engine)
    {
        Uri = uri;
        _engine = engine;
    }

    /// <summary>The connection URI this context was opened with.</summary>
    public string Uri { get; }

    /// <summary>The underlying engine. Exposed for advanced scenarios and testing.</summary>
    public INativeEngine Engine => _engine;

    /// <summary>
    /// Connects to a database. Use a local path or <c>s3://</c>/<c>gs://</c>/<c>az://</c> for the
    /// embedded engine, <c>db://</c> for a remote distributed cluster, or <c>memory://</c> (or
    /// <see cref="LanceConnectOptions.UseInMemoryEngine"/>) for the in-memory backend.
    /// </summary>
    public static Task<LanceDbContext> ConnectAsync(string uri, LanceConnectOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();
        var engine = EngineFactory.Create(uri, options ?? new LanceConnectOptions());
        return Task.FromResult(new LanceDbContext(uri, engine));
    }

    /// <summary>
    /// Creates a table whose schema is compiled from <typeparamref name="T"/> and returns a typed handle.
    /// </summary>
    public async Task<LanceTable<T>> CreateTableAsync<T>(string name, CreateTableOptions? options = null, CancellationToken cancellationToken = default)
        where T : class
    {
        var schema = SchemaCompiler.Compile<T>(name);

        if (options?.DropIfExists == true && await _engine.TableExistsAsync(name, cancellationToken).ConfigureAwait(false))
            await _engine.DropTableAsync(name, cancellationToken).ConfigureAwait(false);

        await _engine.CreateTableAsync(schema, cancellationToken).ConfigureAwait(false);
        return new LanceTable<T>(_engine, schema);
    }

    /// <summary>Returns a typed handle to an existing table, using <typeparamref name="T"/>'s schema.</summary>
    public LanceTable<T> Table<T>(string name) where T : class
    {
        var schema = SchemaCompiler.Compile<T>(name);
        return new LanceTable<T>(_engine, schema);
    }

    /// <summary>Returns true if a table with the given name exists.</summary>
    public Task<bool> TableExistsAsync(string name, CancellationToken cancellationToken = default) =>
        _engine.TableExistsAsync(name, cancellationToken);

    /// <summary>Drops a table.</summary>
    public Task DropTableAsync(string name, CancellationToken cancellationToken = default) =>
        _engine.DropTableAsync(name, cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _engine.DisposeAsync();
}

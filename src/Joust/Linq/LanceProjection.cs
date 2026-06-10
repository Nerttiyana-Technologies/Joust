namespace Joust.Linq;

/// <summary>
/// A projected query produced by <see cref="LanceQuery{T}.Select{TResult}"/>. The projection is
/// applied as rows stream from the backend.
/// </summary>
public sealed class LanceProjection<TResult>
{
    private readonly Func<CancellationToken, IAsyncEnumerable<TResult>> _source;

    internal LanceProjection(Func<CancellationToken, IAsyncEnumerable<TResult>> source) => _source = source;

    /// <summary>Streams the projected results.</summary>
    public IAsyncEnumerable<TResult> AsAsyncEnumerable(CancellationToken cancellationToken = default) =>
        _source(cancellationToken);

    /// <summary>Returns the projected results as a list.</summary>
    public async Task<List<TResult>> ToListAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<TResult>();
        await foreach (var item in _source(cancellationToken).ConfigureAwait(false))
            list.Add(item);
        return list;
    }

    /// <summary>Returns the first projected result, or the default value.</summary>
    public async Task<TResult?> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var item in _source(cancellationToken).ConfigureAwait(false))
            return item;
        return default;
    }
}

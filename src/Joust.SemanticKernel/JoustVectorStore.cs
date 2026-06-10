using Joust;
using Joust.VectorData;
using Microsoft.Extensions.VectorData;

namespace Joust.SemanticKernel;

/// <summary>
/// Semantic Kernel integration entry point for Joust / LanceDB.
/// </summary>
/// <remarks>
/// With the GA Vector Data extensions, Semantic Kernel consumes the standard
/// <see cref="VectorStore"/> abstraction directly, so the Joust connector in
/// <c>Joust.VectorData</c> is the Semantic Kernel connector. This type is a thin convenience for
/// obtaining a <see cref="VectorStore"/> to register with the kernel.
/// </remarks>
public static class JoustSemanticKernel
{
    /// <summary>Creates a <see cref="VectorStore"/> over an open Joust context for use with Semantic Kernel.</summary>
    public static VectorStore CreateVectorStore(LanceDbContext context) => new JoustVectorStore(context);
}

namespace Joust;

/// <summary>Base type for all exceptions raised by Joust.</summary>
public class JoustException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public JoustException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    public JoustException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown when a POCO's attribute configuration cannot be compiled into a valid table schema.</summary>
public sealed class SchemaValidationException : JoustException
{
    /// <summary>Creates the exception with a message.</summary>
    public SchemaValidationException(string message) : base(message) { }
}

/// <summary>
/// Thrown when a LINQ expression cannot be translated into a LanceDB filter. The query
/// fails fast and loudly rather than silently degrading to a full table scan.
/// </summary>
public sealed class NotSupportedExpressionException : JoustException
{
    /// <summary>Creates the exception with a message.</summary>
    public NotSupportedExpressionException(string message) : base(message) { }
}

/// <summary>Thrown when a requested backend topology is not yet implemented in this build.</summary>
public sealed class BackendNotAvailableException : JoustException
{
    /// <summary>Creates the exception with a message.</summary>
    public BackendNotAvailableException(string message) : base(message) { }
}

/// <summary>Thrown when the native LanceDB binary for the current RID cannot be located.</summary>
public sealed class NativeLibraryNotFoundException : JoustException
{
    /// <summary>Creates the exception with a message.</summary>
    public NativeLibraryNotFoundException(string message) : base(message) { }
}

/// <summary>Thrown when a native interop call returns an error status.</summary>
public sealed class NativeInteropException : JoustException
{
    /// <summary>Creates the exception with a message.</summary>
    public NativeInteropException(string message) : base(message) { }
}

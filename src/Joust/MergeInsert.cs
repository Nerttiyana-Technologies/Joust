namespace Joust;

/// <summary>Options controlling a merge-insert (upsert) operation.</summary>
public sealed class MergeInsertOptions
{
    /// <summary>When a source row matches an existing row by key, replace the existing row.</summary>
    public bool WhenMatchedUpdateAll { get; set; }

    /// <summary>When a source row has no matching existing row, insert it.</summary>
    public bool WhenNotMatchedInsertAll { get; set; }

    /// <summary>Delete existing rows whose key is not present in the source set.</summary>
    public bool WhenNotMatchedBySourceDelete { get; set; }
}

/// <summary>The outcome of a merge-insert operation.</summary>
public readonly record struct MergeInsertResult(int Inserted, int Updated, int Deleted);

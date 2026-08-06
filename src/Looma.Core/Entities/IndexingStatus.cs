namespace Looma.Core.Entities;

/// <summary>Per-file status reported while streaming indexing progress.</summary>
public enum IndexingStatus
{
    Started,
    Processing,
    Completed,
    Skipped,
    Failed
}

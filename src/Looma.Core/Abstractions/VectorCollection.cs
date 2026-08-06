namespace Looma.Core.Abstractions;

/// <summary>
/// The two vector collections in the system. Deliberately not one flat
/// store — text-embedding space (<see cref="Documents"/>) and CLIP space
/// (<see cref="Images"/>) are not comparable and must never be mixed.
/// </summary>
public enum VectorCollection
{
    Documents,
    Images
}

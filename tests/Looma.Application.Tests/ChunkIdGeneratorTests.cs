using Looma.Application.Chunking;
using Xunit;

namespace Looma.Application.Tests;

public class ChunkIdGeneratorTests
{
    [Fact]
    public void Generate_SameSourceAndIndex_ProducesSameId()
    {
        var first = ChunkIdGenerator.Generate("./data/file.txt", 2);
        var second = ChunkIdGenerator.Generate("./data/file.txt", 2);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Generate_DifferentChunkIndex_ProducesDifferentId()
    {
        var chunk0 = ChunkIdGenerator.Generate("./data/file.txt", 0);
        var chunk1 = ChunkIdGenerator.Generate("./data/file.txt", 1);

        Assert.NotEqual(chunk0, chunk1);
    }

    [Fact]
    public void Generate_DifferentSource_ProducesDifferentId()
    {
        var fileA = ChunkIdGenerator.Generate("./data/a.txt", 0);
        var fileB = ChunkIdGenerator.Generate("./data/b.txt", 0);

        Assert.NotEqual(fileA, fileB);
    }

    [Fact]
    public void Generate_ProducesAValidGuidString()
    {
        var id = ChunkIdGenerator.Generate("./data/file.txt", 0);

        // Qdrant point ids must be an unsigned integer or UUID string —
        // this is the constraint the whole class exists to satisfy.
        Assert.True(Guid.TryParse(id, out _));
    }
}

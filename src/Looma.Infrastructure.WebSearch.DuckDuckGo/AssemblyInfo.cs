using System.Runtime.CompilerServices;

// DuckDuckGoResultParser is pure and dependency-free (no HttpClient, no
// I/O) specifically so it can be unit-tested directly against a canned
// HTML sample, without needing a live network call — see
// DuckDuckGoResultParserTests.
[assembly: InternalsVisibleTo("Looma.Infrastructure.WebSearch.DuckDuckGo.Tests")]

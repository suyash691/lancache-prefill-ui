using LancachePrefill;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LancachePrefill.Tests;

/// <summary>
/// Tests that mutate the process-wide LANCACHE_CACHE_DIR environment variable
/// share this collection so they never run concurrently with each other.
/// </summary>
[CollectionDefinition("EnvSerial", DisableParallelization = true)]
public class EnvSerialCollection { }

[Collection("EnvSerial")]
public class DepotDownloaderTests : IDisposable
{
    private readonly string _cacheDir;
    private readonly string? _prevEnv;

    public DepotDownloaderTests()
    {
        _prevEnv = Environment.GetEnvironmentVariable("LANCACHE_CACHE_DIR");
        _cacheDir = Path.Combine(Path.GetTempPath(), $"lancache-cache-{Guid.NewGuid()}");
        Directory.CreateDirectory(_cacheDir);
        Environment.SetEnvironmentVariable("LANCACHE_CACHE_DIR", _cacheDir);
    }

    private DepotDownloader NewDownloader() =>
        new(Substitute.For<ISteamSession>(), NullLogger<DepotDownloader>.Instance);

    // Golden values: the Lancache/nginx cache key is
    //   steam/depot/{depotId}/chunk/{chunkId}bytes=0-1048575
    // hashed with MD5 (lowercase hex), stored at levels=2:2 (last 2 / next 2 bytes).
    // These constants are computed independently of the implementation so a
    // regression in the key format or path layout breaks this test.
    private const string GoldenHash = "c49495ea58f075da0b0bb66a569de7ed"; // md5("steam/depot/731/chunk/aabbbytes=0-1048575")
    private const string GoldenL1 = "ed";
    private const string GoldenL2 = "e7";

    [Fact]
    public async Task ProbeChunkCached_MatchesGoldenNginxCacheKeyPath()
    {
        // Place a file at the exact path the nginx cache would use.
        var dir = Path.Combine(_cacheDir, GoldenL1, GoldenL2);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, GoldenHash), "cached");

        var result = await NewDownloader().ProbeChunkCachedAsync(new DownloadChunk(731, "aabb", 1024));

        Assert.True(result);
    }

    [Fact]
    public async Task ProbeChunkCached_ChunkIdCaseInsensitive()
    {
        // Same golden path; probe with an upper-cased chunk id should still hit,
        // because the key is lowercased before hashing.
        var dir = Path.Combine(_cacheDir, GoldenL1, GoldenL2);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, GoldenHash), "cached");

        var result = await NewDownloader().ProbeChunkCachedAsync(new DownloadChunk(731, "AABB", 1024));

        Assert.True(result);
    }

    [Fact]
    public async Task ProbeChunkCached_MissingFile_ReturnsFalse()
    {
        var result = await NewDownloader().ProbeChunkCachedAsync(new DownloadChunk(731, "aabb", 1024));
        Assert.False(result);
    }

    [Fact]
    public async Task ProbeChunkCached_NoCacheDirConfigured_ReturnsNull()
    {
        Environment.SetEnvironmentVariable("LANCACHE_CACHE_DIR", null);
        var result = await NewDownloader().ProbeChunkCachedAsync(new DownloadChunk(731, "aabb", 1024));
        Assert.Null(result);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LANCACHE_CACHE_DIR", _prevEnv);
        try { Directory.Delete(_cacheDir, true); } catch { }
    }
}

using Boutique.Services;
using FluentAssertions;
using Xunit;

namespace Boutique.Tests;

public class TextureCacheLifecycleTests : IDisposable
{
    public TextureCacheLifecycleTests() => TextureLoadingService.ResetForTesting();

    public void Dispose() => TextureLoadingService.ResetForTesting();

    [Fact]
    public void RemoveCacheConsumer_WhenLastConsumer_ClearsCache()
    {
        TextureLoadingService.AddCacheConsumer();
        TextureLoadingService.InjectCacheEntry("textures/armor01.dds");
        TextureLoadingService.InjectCacheEntry("textures/armor02.dds");
        TextureLoadingService.InjectCacheEntry("textures/body.dds");
        TextureLoadingService.CacheCount.Should().Be(3);

        TextureLoadingService.RemoveCacheConsumer();

        TextureLoadingService.CacheCount.Should().Be(0);
        TextureLoadingService.ConsumerCount.Should().Be(0);
    }

    [Fact]
    public void RemoveCacheConsumer_WithOtherConsumersRemaining_PreservesCache()
    {
        TextureLoadingService.AddCacheConsumer();
        TextureLoadingService.AddCacheConsumer();
        TextureLoadingService.InjectCacheEntry("textures/armor01.dds");
        TextureLoadingService.InjectCacheEntry("textures/armor02.dds");

        TextureLoadingService.RemoveCacheConsumer();

        TextureLoadingService.CacheCount.Should().Be(2);
        TextureLoadingService.ConsumerCount.Should().Be(1);
    }

    [Fact]
    public void RemoveCacheConsumer_CalledMoreThanAdd_DoesNotGoNegative()
    {
        TextureLoadingService.AddCacheConsumer();
        TextureLoadingService.RemoveCacheConsumer();
        TextureLoadingService.RemoveCacheConsumer();

        TextureLoadingService.ConsumerCount.Should().Be(0);
    }

    [Fact]
    public void AddCacheConsumer_AfterClear_AllowsFreshCacheAccumulation()
    {
        TextureLoadingService.AddCacheConsumer();
        TextureLoadingService.InjectCacheEntry("textures/armor01.dds");
        TextureLoadingService.RemoveCacheConsumer();
        TextureLoadingService.CacheCount.Should().Be(0);

        TextureLoadingService.AddCacheConsumer();
        TextureLoadingService.InjectCacheEntry("textures/armor03.dds");

        TextureLoadingService.CacheCount.Should().Be(1);
        TextureLoadingService.ConsumerCount.Should().Be(1);
    }

    [Fact]
    public void Cache_AccumulatesWithoutConsumerLifecycle_NeverClears()
    {
        TextureLoadingService.AddCacheConsumer();

        for (var i = 0; i < 50; i++)
        {
            TextureLoadingService.InjectCacheEntry($"textures/armor{i:D3}.dds");
        }

        TextureLoadingService.CacheCount.Should().Be(50);
    }

    [Fact]
    public void BodyTextures_SurviveCacheClear()
    {
        TextureLoadingService.AddCacheConsumer();
        TextureLoadingService.InjectCacheEntry("textures/armor/imperial/CuirassLight.dds");
        TextureLoadingService.InjectBodyCacheEntry(@"textures\actors\character\female\femalebody_1.dds");
        TextureLoadingService.InjectBodyCacheEntry(@"textures\actors\character\female\FemaleHands_1.dds");

        TextureLoadingService.CacheCount.Should().Be(1);
        TextureLoadingService.PersistentCacheCount.Should().Be(2);

        TextureLoadingService.RemoveCacheConsumer();

        TextureLoadingService.CacheCount.Should().Be(0);
        TextureLoadingService.PersistentCacheCount.Should().Be(2);
    }

    [Fact]
    public void BodyTextures_NotReloadedAcrossSessions()
    {
        TextureLoadingService.InjectBodyCacheEntry(@"textures\actors\character\female\femalebody_1.dds");

        TextureLoadingService.AddCacheConsumer();
        TextureLoadingService.InjectCacheEntry("textures/armor/imperial/CuirassLight.dds");
        TextureLoadingService.RemoveCacheConsumer();

        TextureLoadingService.AddCacheConsumer();
        TextureLoadingService.PersistentCacheCount.Should().Be(1);
        TextureLoadingService.CacheCount.Should().Be(0);
    }
}

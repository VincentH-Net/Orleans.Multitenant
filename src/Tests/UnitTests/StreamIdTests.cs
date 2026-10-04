using Orleans.Runtime;

namespace OrleansMultitenant.Tests.UnitTests;

/// <remarks>Uses the same tenant ids and keys as <see cref="TenantIdTests"/> and <see cref="NoTenantIdTests"/>: streams support the same tenant ids and keys within a tenant as grains do</remarks>
[Collection(MultiPurposeCluster.Name)]
public class StreamIdTests(ClusterFixture fixture)
{
    const string Namespace = "StreamIdNamespace";

    readonly Orleans.TestingHost.TestCluster cluster = fixture.Cluster;

    public static TheoryData<string /*key*/> TenantUnawareKeys() => new("1", "Key2", "~Key6");

    [Theory]
    [MemberData(nameof(TenantIdTests.TenantKeyQualifiedKeys), MemberType = typeof(TenantIdTests))]
    public void GetStream_ForTenantWithKey_FormatsKeyCorrectly(string tenantId, string keyWithinTenant, string tenantQualifiedKey)
     => Assert.Equal(tenantQualifiedKey, GetStreamId(tenantId, keyWithinTenant).GetKeyAsString());

    [Theory]
    [MemberData(nameof(TenantIdTests.TenantKeyQualifiedKeys), MemberType = typeof(TenantIdTests))]
    [SuppressMessage("Usage", "xUnit1026:Theory methods should use all of their parameters", Justification = "Avoid code duplication")]
    public void GetTenantId_ForStreamIdWithTenant_ReturnsCorrectTenantId(string tenantId, string keyWithinTenant, string _)
     => Assert.Equal(tenantId, GetStreamId(tenantId, keyWithinTenant).GetTenantId());

    [Theory]
    [MemberData(nameof(TenantIdTests.TenantKeyQualifiedKeys), MemberType = typeof(TenantIdTests))]
    [SuppressMessage("Usage", "xUnit1026:Theory methods should use all of their parameters", Justification = "Avoid code duplication")]
    public void GetKeyWithinTenant_ForStreamIdWithTenant_ReturnsCorrectKeyWithinTenant(string tenantId, string keyWithinTenant, string _)
     => Assert.Equal(keyWithinTenant, GetStreamId(tenantId, keyWithinTenant).GetKeyWithinTenant());

    [Theory]
    [MemberData(nameof(TenantIdTests.TenantKeyQualifiedKeys), MemberType = typeof(TenantIdTests))]
    [SuppressMessage("Usage", "xUnit1026:Theory methods should use all of their parameters", Justification = "Avoid code duplication")]
    public void GetStream_ForStreamIdOfTenantStream_ReturnsSameStream(string tenantId, string keyWithinTenant, string _)
    {
        var provider = cluster.Client.GetTenantStreamProvider(ClusterFixture.TenantAwareStreamProviderName, tenantId);
        var stream = provider.GetStream<int>(Namespace, keyWithinTenant);

        var streamFromStreamId = provider.GetStream<int>(stream.StreamId);

        Assert.Equal(stream, streamFromStreamId);
        Assert.Equal(stream.StreamId, streamFromStreamId.StreamId);
    }

    [Theory]
    [MemberData(nameof(NoTenantIdTests.KeyQualifiedKeys), MemberType = typeof(NoTenantIdTests))]
    public void GetStream_ForNoTenantWithKey_FormatsKeyCorrectly(string key, string tenantQualifiedKey)
     => Assert.Equal(tenantQualifiedKey, GetStreamId(null!, key).GetKeyAsString());

    [Theory]
    [MemberData(nameof(NoTenantIdTests.KeyQualifiedKeys), MemberType = typeof(NoTenantIdTests))]
    [SuppressMessage("Usage", "xUnit1026:Theory methods should use all of their parameters", Justification = "Avoid code duplication")]
    public void GetTenantId_ForStreamIdWithNoTenant_ReturnsNull(string key, string _)
     => Assert.Null(GetStreamId(null!, key).GetTenantId());

    [Theory]
    [MemberData(nameof(NoTenantIdTests.KeyQualifiedKeys), MemberType = typeof(NoTenantIdTests))]
    [SuppressMessage("Usage", "xUnit1026:Theory methods should use all of their parameters", Justification = "Avoid code duplication")]
    public void GetKeyWithinTenant_ForStreamIdWithNoTenant_ReturnsCorrectKeyWithinTenant(string key, string _)
     => Assert.Equal(key, GetStreamId(null!, key).GetKeyWithinTenant());

    [Theory]
    [MemberData(nameof(TenantUnawareKeys))]
    public void GetTenantId_ForTenantUnawareStreamId_ReturnsNull(string key)
     => Assert.Null(StreamId.Create(Namespace, key).GetTenantId());

    [Fact]
    public void GetStream_ForKeyWithinTenantThatLooksLikeKeyWithTenantId_DoesNotInterpretKey()
    {
        var provider = cluster.Client.GetTenantStreamProvider(ClusterFixture.TenantAwareStreamProviderName, "TenantA");

        var sameTenantStreamId = provider.GetStream<int>(Namespace, "TenantA|Key").StreamId;
        var otherTenantStreamId = provider.GetStream<int>(Namespace, "TenantB|Key").StreamId;

        Assert.Equal(("TenantA", "TenantA|Key"), (sameTenantStreamId.GetTenantId(), sameTenantStreamId.GetKeyWithinTenant()));
        Assert.Equal(("TenantA", "TenantB|Key"), (otherTenantStreamId.GetTenantId(), otherTenantStreamId.GetKeyWithinTenant()));
    }

    [Fact]
    public void GetStream_ForStreamIdWithKeyThatIncludesTenantId_InterpretsKey()
    {
        var provider = cluster.Client.GetTenantStreamProvider(ClusterFixture.TenantAwareStreamProviderName, "TenantA");

        var streamId = provider.GetStream<int>(StreamId.Create(Namespace, "TenantA|Key")).StreamId;

        Assert.Equal(("TenantA", "Key"), (streamId.GetTenantId(), streamId.GetKeyWithinTenant()));
    }

    [Fact]
    public void GetStream_ForNullKeyWithinTenant_ThrowsArgumentNullException()
     => Assert.Throws<ArgumentNullException>("keyWithinTenant", () => cluster.Client.GetTenantStreamProvider(ClusterFixture.TenantAwareStreamProviderName, "TenantA").GetStream<int>(Namespace, null!));

    StreamId GetStreamId(string tenantId, string keyWithinTenant)
     => cluster.Client.GetTenantStreamProvider(ClusterFixture.TenantAwareStreamProviderName, tenantId).GetStream<int>(Namespace, keyWithinTenant).StreamId;
}

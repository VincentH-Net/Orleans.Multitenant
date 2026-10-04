using System.Reflection;
using System.Runtime.CompilerServices;
using Orleans.Streams;

namespace OrleansMultitenant.Tests.UnitTests;

public class TenantStreamApiTests
{
    /// <summary>
    /// <see cref="TenantStream{T}"/> is a separate type from <see cref="IAsyncStream{T}"/> by design, so its API is not kept complete by the compiler.<br />
    /// This test fails when Orleans has a stream method (e.g. after an Orleans upgrade) that <see cref="TenantStream{T}"/> does not offer
    /// </summary>
    [Fact]
    public void TenantStream_ForEachOrleansStreamMethod_HasMethodWithSameSignature()
    {
        var tenantStreamMethods = typeof(TenantStream<int>).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(method => Signature(method)).ToHashSet();

        var orleansStreamMethods = OrleansStreamMethods().ToList();

        Assert.True(orleansStreamMethods.Count >= 22, $"Expected to find at least the 22 stream methods of Orleans 10 but found {orleansStreamMethods.Count}");
        Assert.Empty(orleansStreamMethods.Except(tenantStreamMethods));
    }

    [Fact]
    public void PublicApi_OfOrleansMultitenant_DoesNotContainTenantEvent()
     => Assert.DoesNotContain(typeof(TenantStream<>).Assembly.GetExportedTypes(), type => type.Name.StartsWith("TenantEvent", StringComparison.Ordinal));

    /// <returns>The signatures of the methods that Orleans offers on an <see cref="IAsyncStream{T}"/>: interface methods and extension methods</returns>
    static IEnumerable<string> OrleansStreamMethods()
    {
        Type[] streamInterfaces = [typeof(IAsyncStream<int>), .. typeof(IAsyncStream<int>).GetInterfaces().Where(type => type.Namespace == typeof(IAsyncStream).Namespace)];

        foreach (var method in streamInterfaces.SelectMany(type => type.GetMethods()))
            yield return Signature(method);

        var streamInterfaceDefinitions = streamInterfaces.Select(Definition).ToHashSet();
        var extensionMethods = typeof(IAsyncStream).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.IsDefined(typeof(ExtensionAttribute), false) && streamInterfaceDefinitions.Contains(Definition(method.GetParameters()[0].ParameterType)));

        foreach (var method in extensionMethods)
            yield return Signature(method.IsGenericMethodDefinition ? method.MakeGenericMethod(typeof(int)) : method, isExtension: true);
    }

    static Type Definition(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition() : type;

    static string Signature(MethodInfo method, bool isExtension = false)
     => $"{method.ReturnType} {method.Name}({string.Join(", ", method.GetParameters().Skip(isExtension ? 1 : 0).Select(parameter => parameter.ParameterType))})";
}

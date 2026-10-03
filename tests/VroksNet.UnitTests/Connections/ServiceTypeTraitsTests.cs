using VroksNet.Application.System.ListConnectionTypes;
using VroksNet.Domain.Connections;

namespace VroksNet.UnitTests.Connections;

/// <summary><see cref="ServiceTypeTraits"/> describes every type once, and the API serves exactly that (ADR 0003).</summary>
public class ServiceTypeTraitsTests
{
    [Fact]
    public void All_DescribesEveryServiceTypeExactlyOnce()
        => Assert.Equal(Enum.GetValues<ConnectionServiceType>().Order(), ServiceTypeTraits.All.Select(traits => traits.Type).Order());

    [Fact]
    public void All_ListenNoteIsGivenExactlyWhenTheTypeCantListen()
        => Assert.All(ServiceTypeTraits.All, traits => Assert.Equal(traits.CanListen, traits.ListenNote is null));

    [Fact]
    public void Find_UnknownValue_IsNull()
        => Assert.Null(ServiceTypeTraits.Find((ConnectionServiceType)99));

    [Fact]
    public async Task ListConnectionTypes_ReturnsEveryTypesTraitsInOrder()
    {
        var types = await new ListConnectionTypesHandler().Handle(new ListConnectionTypes(), TestContext.Current.CancellationToken);

        Assert.Equal(ServiceTypeTraits.All.Select(traits => traits.Type), types.Select(type => type.Type));
        var http = Assert.Single(types, type => type.Type == ConnectionServiceType.Http);
        Assert.Equal(("HTTP", "URL", true, false), (http.DisplayName, http.ValueLabel, http.IsHttp, http.CanListen));
        Assert.NotNull(http.ListenNote);
    }
}

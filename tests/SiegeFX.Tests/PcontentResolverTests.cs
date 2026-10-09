using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;

namespace SiegeFX.Tests;

public class PcontentResolverTests
{
    [Fact]
    public void A_modifier_suffix_is_not_part_of_the_class()
    {
        var spec = PcontentResolver.ParseSpec("#sd_g_c_st_1h_avg:o_avg");
        Assert.Equal("sd_g_c_st_1h_avg", spec.Class);
        Assert.False(spec.HasPower);
    }

    [Fact]
    public void Class_and_power_specs_parse_as_before()
    {
        var spec = PcontentResolver.ParseSpec("#club/2-3");
        Assert.Equal("club", spec.Class);
        Assert.True(spec.HasPower);
        Assert.Equal(2, spec.PowerMin);
        Assert.Equal(3, spec.PowerMax);
    }

    [Theory]
    [InlineData("#sd_test_sword:o_avg")]
    [InlineData("#sd_test_sword")]
    public void A_spec_naming_a_template_resolves_to_that_item(string spec)
    {
        // Elddim's guards author their swords this way; an unresolved spec
        // left them unarmed with the shield hanging at the hip.
        var store = new TemplateStore();
        Assert.Equal(1, store.RegisterFromGasText("[t:template,n:sd_test_sword] { }"));
        var resolver = new PcontentResolver(store);

        Assert.True(resolver.TryResolve(spec, new Random(1), out var name));
        Assert.Equal("sd_test_sword", name);
    }

    [Fact]
    public void An_unknown_name_still_fails()
    {
        var resolver = new PcontentResolver(new TemplateStore());
        Assert.False(resolver.TryResolve("#no_such_item:o_avg", new Random(1), out _));
    }
}

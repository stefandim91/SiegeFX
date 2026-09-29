namespace SiegeFX.Core.Assets;

/// <summary>Choose a placed prop's authored slot-0 texture before falling back
/// to the shared ASP's embedded texture. Instance data overrides the template,
/// and the template lookup follows its specializes chain.</summary>
public static class StaticPropTextureResolver
{
    public static string? AuthoredTexture(
        TemplateStore templates, Template template, GasNode placement)
    {
        var instance = Clean(TemplateStore.GetNodeAttribute(
            placement, "aspect", "textures", "0"));
        if (instance is not null) return instance;
        return Clean(templates.GetAttribute(template, "aspect", "textures", "0"));
    }

    private static string? Clean(string? value)
    {
        var name = value?.Trim().Trim('"').Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}

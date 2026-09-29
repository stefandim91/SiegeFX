using SiegeFX.Core.Tank;

namespace SiegeFX.Core.Assets;

/// <summary>
/// Resolves a terrain subset texture using the terrain tank as the authoritative
/// source and the game's merged resource view as a fallback. Some shipped region
/// meshes reference decoration textures (for example interior rugs) that live in
/// Objects.dsres rather than Terrain.dsres.
/// </summary>
public static class TerrainTextureLoader
{
    public enum Source
    {
        Terrain,
        ResourceFallback,
    }

    /// <summary>Builds the bare-name index used by terrain subset references.</summary>
    public static Dictionary<string, string> BuildTerrainIndex(TankReader terrainReader)
    {
        ArgumentNullException.ThrowIfNull(terrainReader);

        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in terrainReader.ListFiles())
        {
            if (!path.EndsWith(".raw", StringComparison.OrdinalIgnoreCase)) continue;
            var bare = Path.GetFileNameWithoutExtension(path);
            if (!index.ContainsKey(bare)) index[bare] = path;
        }
        return index;
    }

    /// <summary>
    /// Loads and decodes <paramref name="textureName"/>. A valid terrain entry
    /// always wins; the fallback is consulted when it is absent or unreadable.
    /// </summary>
    public static bool TryLoad(
        string textureName,
        TankReader terrainReader,
        IReadOnlyDictionary<string, string> terrainIndex,
        AssetResolver? resourceFallback,
        out RawImage image,
        out Source source)
    {
        ArgumentNullException.ThrowIfNull(terrainReader);
        ArgumentNullException.ThrowIfNull(terrainIndex);

        image = null!;
        source = default;
        if (string.IsNullOrWhiteSpace(textureName)) return false;

        var bareName = Path.GetFileNameWithoutExtension(textureName.Trim());
        if (terrainIndex.TryGetValue(bareName, out var terrainPath))
        {
            try
            {
                image = RawImage.Load(terrainReader.ExtractToMemory(terrainPath));
                source = Source.Terrain;
                return true;
            }
            catch
            {
                // Preserve the original VFS behavior: a broken higher-priority
                // entry may fall through to another loaded resource tank.
            }
        }

        if (resourceFallback is not null)
        {
            try
            {
                if (!resourceFallback.TryLoadByBasename(bareName + ".raw", out var bytes)) return false;
                image = RawImage.Load(bytes);
                source = Source.ResourceFallback;
                return true;
            }
            catch
            {
                // Rendering remains failure-tolerant; the subset uses its
                // existing untextured fallback when every source is invalid.
            }
        }

        return false;
    }
}

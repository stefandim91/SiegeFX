using SiegeFX.Core.Assets;
using SiegeFX.Core.Tank;

namespace SiegeFX.Runtime;

internal static class TerrainTextureSelfTest
{
    public static bool Run(string installDirectory)
    {
        try
        {
            using var terrain = TankFile.Open(Path.Combine(installDirectory, "Resources", "Terrain.dsres"));
            using var objects = TankFile.Open(Path.Combine(installDirectory, "Resources", "Objects.dsres"));
            var reader = new TankReader(terrain);
            var index = TerrainTextureLoader.BuildTerrainIndex(reader);
            var fallback = new AssetResolver();
            fallback.Add(new TankReader(objects), "Objects.dsres");
            if (TerrainTextureLoader.TryLoad("b_d_runner-06", reader, index, null, out _, out _))
                throw new InvalidDataException("Fixture no longer reproduces the terrain-only carpet miss.");
            if (!TerrainTextureLoader.TryLoad("b_d_runner-06", reader, index, fallback, out var rug, out var source)
                || source != TerrainTextureLoader.Source.ResourceFallback || rug.Width != 64 || rug.Height != 128)
                throw new InvalidDataException("Elddim carpet did not resolve from Objects.dsres.");
            var name = index.Keys.First();
            if (!TerrainTextureLoader.TryLoad(name, reader, index, fallback, out var native, out source)
                || source != TerrainTextureLoader.Source.Terrain
                || !native.Pixels.SequenceEqual(RawImage.Load(reader.ExtractToMemory(index[name])).Pixels))
                throw new InvalidDataException("Native terrain texture precedence changed.");
            if (TerrainTextureLoader.TryLoad("siegefx_nonexistent_texture_fixture", reader, index, fallback, out _, out _))
                throw new InvalidDataException("Missing texture unexpectedly resolved.");
            // The entry remains indexed but its source cannot be read. An
            // optional texture failure must not abort terrain loading.
            using var unavailable = TankFile.Open(Path.Combine(installDirectory, "Resources", "Objects.dsres"));
            var brokenFallback = new AssetResolver();
            brokenFallback.Add(new TankReader(unavailable), "unavailable Objects.dsres");
            unavailable.Dispose();
            if (TerrainTextureLoader.TryLoad("b_d_runner-06", reader, index, brokenFallback, out _, out _))
                throw new InvalidDataException("Unavailable fallback unexpectedly resolved.");
            Console.WriteLine("[selftest-terrain-textures] PASS — carpet fallback, native terrain, missing/unreadable texture");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[selftest-terrain-textures] FAIL: " + ex.Message);
            return false;
        }
    }
}

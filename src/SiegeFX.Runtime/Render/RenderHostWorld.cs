using SiegeFX.Core.Assets;
using SiegeFX.Runtime.Render.Hud;

namespace SiegeFX.Runtime.Render;

public sealed partial class RenderHost
{
    private WorldProfile _selectedWorld = WorldProfile.Get("KingdomOfEhb");
    private WorldProfile? _activeWorld;
    private string _saveSetId = Guid.NewGuid().ToString("N");
    private readonly WorldSelectDialog _worldSelect = new();
    private string? _worldLaunchError;
    private bool MultiplayerContent => _activeWorld?.EnableMultiplayerAuthoredTriggers == true;

    private bool HasSaveCoordinateFrame(SiegeFX.Core.Save.SaveFile save) =>
        WorldLaunchPlan.MatchesCoordinateFrame(save, _activeWorld?.Id, SaveFrameRegion());

    private void InitializeWorldIdentity(string? regionPath)
    {
        if (string.IsNullOrEmpty(regionPath)) return;
        var explicitId = Environment.GetEnvironmentVariable("SIEGEFX_WORLD_ID");
        _activeWorld = string.IsNullOrEmpty(explicitId)
            ? WorldProfile.TryFromRegion(regionPath) : WorldProfile.Get(explicitId);
        if (_activeWorld is not null && !_activeWorld.ContainsRegion(regionPath))
            throw new InvalidDataException("Selected world does not contain the launch region.");
        var setId = Environment.GetEnvironmentVariable("SIEGEFX_SAVE_SET_ID");
        if (!string.IsNullOrEmpty(setId))
        {
            if (!Guid.TryParse(setId, out var id)) throw new InvalidDataException("Invalid adventure save-set ID.");
            _saveSetId = id.ToString("N");
        }
    }

    private void HandleWorldSelection(WorldSelectDialog.Result result)
    {
        if (result == WorldSelectDialog.Result.None) return;
        _worldSelect.Close();
        _worldLaunchError = null;
        if (result == WorldSelectDialog.Result.Cancel) return;
        _selectedWorld = WorldProfile.Get(result == WorldSelectDialog.Result.UtraeanPeninsula
            ? "UtraeanPeninsula" : "KingdomOfEhb");
        _creator.Cancelled = false;
        _frontendScene?.SetState(FrontendScene.ScreenState.SinglePlayerToCd);
        _spMenu.IsActive = false;
        _spMenu.ClearHover();
    }

    private static void SetOfflineLaunchIdentity(System.Diagnostics.ProcessStartInfo process,
        WorldProfile profile, string saveSetId)
    {
        process.Environment["SIEGEFX_WORLD_ID"] = profile.Id;
        process.Environment["SIEGEFX_SAVE_SET_ID"] = saveSetId;
        // A normal new/load must not inherit a network session from its parent.
        foreach (var key in process.Environment.Keys.Where(k => k.StartsWith("SIEGEFX_MP_", StringComparison.Ordinal)).ToArray())
            process.Environment.Remove(key);
        process.Environment.Remove("SIEGEFX_LOAD_SAVE");
    }
}

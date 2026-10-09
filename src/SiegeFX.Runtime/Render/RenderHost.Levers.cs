using System.Numerics;
using Silk.NET.OpenGL;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render;

public sealed partial class RenderHost
{
    private void ConfigureLeverPose(StaticPropInstance prop, Template template, ActorInstance placement)
    {
        if (_actorSpawner is null || prop.Asp is not { HasSkin: true } asp
            || asp.BoneCount > SkinnedMesh.MaxBones) return;
        var open = _actorSpawner.LoadTransitionClip(template, placement, "chore_open");
        var close = _actorSpawner.LoadTransitionClip(template, placement, "chore_close");
        if (open is null || close is null) return;
        try
        {
            var pose = new UsableTransitionPose(asp, open, close);
            if (!_actorMeshCache.TryGetValue(asp, out var mesh))
                _actorMeshCache[asp] = mesh = new SkinnedMesh(_gl!, asp);
            prop.LeverPose = pose;
            prop.LeverPoseMesh = mesh;
            var startClose = TemplateStore.GetNodeAttribute(placement.Node, "on_off_lever", "start_anim_close")
                ?? _templateStore!.GetAttribute(template, "on_off_lever", "start_anim_close");
            prop.LeverStartsPosed = !string.Equals(startClose?.Trim().Trim('"'), "false", StringComparison.OrdinalIgnoreCase)
                && startClose?.Trim() != "0";
            prop.LeverPoseApplied = prop.LeverStartsPosed;

            // Skinning already carries the ASP's bind-to-animation transform.
            // Like actors, posed props use placement and node transforms only.
            float scale = ResolveScaleMultiplier(template, placement.Node);
            var local = Matrix4x4.CreateScale(scale)
                * Matrix4x4.CreateFromQuaternion(placement.Placement.Orientation)
                * Matrix4x4.CreateTranslation(placement.Placement.LocalPosition);
            prop.LeverUnposedLocalWorld = prop.NodeLocalWorld;
            prop.LeverPosedLocalWorld = local;
            ApplyLeverPoseTransform(prop);
        }
        catch (Exception ex)
        {
            prop.LeverPose = null;
            prop.LeverPoseMesh = null;
            Console.WriteLine($"[lever-pose] 0x{prop.Scid:X8}: {ex.Message}; keeping static geometry");
        }
    }

    private void SetLeverState(StaticPropInstance prop, bool on, bool snap = false)
    {
        prop.LeverOn = on;
        if (prop.LeverPose is not { } pose) return;
        // A save restore snaps to the stored state; a live pull always animates.
        prop.LeverPoseApplied = !snap || on || prop.LeverStartsPosed;
        pose.SetOpen(on, _terrainTime, snap);
        ApplyLeverPoseTransform(prop);
    }

    private void ApplyLeverPoseTransform(StaticPropInstance prop)
    {
        prop.NodeLocalWorld = prop.LeverPoseApplied ? prop.LeverPosedLocalWorld : prop.LeverUnposedLocalWorld;
        if (!_elevatorNodeOverrides.TryGetValue(prop.NodeGuid, out var node)
            && !(_regionLayout?.TryGetTransform(prop.NodeGuid, out node) ?? false))
            node = Matrix4x4.Identity;
        prop.World = ComposeAttachedPropWorld(prop.NodeLocalWorld, node);
    }

    private Vector3 LeverPosition(StaticPropInstance prop)
    {
        if (prop.LeverPoseApplied && prop.LeverPose is { } pose)
        {
            pose.Update(_terrainTime);
            return Vector3.Transform(pose.Center, prop.World);
        }
        return prop.World.Translation;
    }

    private void DrawLeverPoses(Matrix4x4 viewProjection)
    {
        if (_skinShader is null || _leverProps.Count == 0) return;
        _skinShader.Use();
        _skinShader.SetMatrix4("uViewProj", viewProjection);
        _skinShader.SetInt("uAlbedo", 0);
        _skinShader.SetInt("uFlipV", 0);
        _skinShader.SetInt("uSubsetTintActive", 0);
        ResetAnimatedTextureBinding(_skinShader);
        ApplyLightingUniforms(_skinShader);
        _gl!.Disable(GLEnum.CullFace);
        foreach (var prop in _leverProps)
        {
            if (!prop.LeverPoseApplied || prop.LeverPose is not { } pose
                || prop.LeverPoseMesh is not { } mesh || prop.IsDestroyed || prop.ForceNoRender
                || IsAbovePlayer(prop.RegionPath)
                || (prop.NodeGuid != 0 && _fadedSnodeCounts.ContainsKey(prop.NodeGuid))) continue;
            pose.Update(_terrainTime);
            _skinShader.SetMatrix4("uModel", prop.World);
            _skinShader.SetMatrix4Array("uBones[0]", pose.SkinMatrices);
            _skinShader.SetInt("uHasTexture", prop.Texture is null ? 0 : 1);
            prop.Texture?.Bind(TextureUnit.Texture0);
            mesh.Draw();
        }
        _gl.Enable(GLEnum.CullFace);
    }
}

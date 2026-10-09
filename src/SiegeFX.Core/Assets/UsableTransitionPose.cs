using System.Numerics;

namespace SiegeFX.Core.Assets;

/// <summary>
/// Reusable CPU pose state for a skinned prop with paired open and close clips.
/// The owned arrays keep stable identities so render and interaction code can
/// retain references without creating per-frame garbage.
/// </summary>
public sealed class UsableTransitionPose
{
    private readonly AspMesh _mesh;
    private readonly PrsAnimation _openClip;
    private readonly PrsAnimation _closeClip;
    private PrsAnimation _activeClip;
    private double _transitionStartedAt;

    /// <summary>One current skin matrix per mesh bone.</summary>
    public Matrix4x4[] SkinMatrices { get; }

    /// <summary>Current mesh-local position of every ASP corner.</summary>
    public Vector3[] Positions { get; }

    /// <summary>Minimum mesh-local corner position in the current pose.</summary>
    public Vector3 Min { get; private set; }

    /// <summary>Maximum mesh-local corner position in the current pose.</summary>
    public Vector3 Max { get; private set; }

    /// <summary>Center of the current mesh-local posed bounds.</summary>
    public Vector3 Center { get; private set; }

    /// <summary>The requested logical state, including while its clip is playing.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Whether the active transition has not yet reached its endpoint.</summary>
    public bool IsTransitioning { get; private set; }

    /// <summary>Current time in the active open or close clip.</summary>
    public float ClipTime { get; private set; }

    public UsableTransitionPose(AspMesh mesh, PrsAnimation open, PrsAnimation close)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(close);

        ValidateMesh(mesh);
        ValidateClip(open, nameof(open));
        ValidateClip(close, nameof(close));

        _mesh = mesh;
        _openClip = open;
        _closeClip = close;
        _activeClip = close;
        SkinMatrices = new Matrix4x4[mesh.BoneCount];
        Positions = new Vector3[mesh.Corners.Length];

        // A newly created usable starts fully closed. Sampling the authored close
        // endpoint also warms AnimationRuntime's mesh/clip bone map before Update.
        SamplePose(close, close.AnimLength);
    }

    /// <summary>
    /// Requests the open or closed state. A changed state begins its corresponding
    /// clip at <paramref name="clock"/>; repeated requests do not restart it.
    /// <paramref name="snap"/> immediately applies the requested endpoint and cancels
    /// an active transition, including when restoring the current logical state.
    /// </summary>
    public void SetOpen(bool open, double clock, bool snap = false)
    {
        if (!double.IsFinite(clock))
            throw new ArgumentOutOfRangeException(nameof(clock), "Transition clock must be finite.");

        if (!snap && open == IsOpen)
            return;

        IsOpen = open;
        _activeClip = open ? _openClip : _closeClip;
        _transitionStartedAt = clock;

        if (snap || _activeClip.AnimLength <= 0f)
        {
            IsTransitioning = false;
            SamplePose(_activeClip, _activeClip.AnimLength);
            return;
        }

        IsTransitioning = true;
        SamplePose(_activeClip, 0f);
    }

    /// <summary>Advances the active one-shot transition, clamping at its endpoint.</summary>
    public void Update(double clock)
    {
        if (!double.IsFinite(clock))
            throw new ArgumentOutOfRangeException(nameof(clock), "Transition clock must be finite.");
        if (!IsTransitioning)
            return;

        var length = _activeClip.AnimLength;
        var elapsed = Math.Max(0d, clock - _transitionStartedAt);
        if (elapsed >= length)
        {
            IsTransitioning = false;
            SamplePose(_activeClip, length);
        }
        else
        {
            SamplePose(_activeClip, (float)elapsed);
        }
    }

    /// <summary>
    /// Tests the current posed triangles against a world-space ray. The direction is
    /// normalized before it is transformed, while the mesh-local direction is left at
    /// its transformed length; consequently the returned parameter remains a world
    /// distance even when <paramref name="world"/> contains scale.
    /// </summary>
    public bool TryRayHit(
        Vector3 origin,
        Vector3 worldDirection,
        Matrix4x4 world,
        float maxDistance,
        out float distance)
    {
        distance = 0f;
        if (!IsFinite(origin) || !IsFinite(worldDirection) || !IsFinite(world)
            || !float.IsFinite(maxDistance) || maxDistance < 0f)
            return false;

        var directionLengthSquared = worldDirection.LengthSquared();
        if (!float.IsFinite(directionLengthSquared) || directionLengthSquared <= 1e-12f)
            return false;
        var unitDirection = worldDirection / MathF.Sqrt(directionLengthSquared);

        if (!Matrix4x4.Invert(world, out var inverse) || !IsFinite(inverse))
            return false;

        var localOrigin = Vector3.Transform(origin, inverse);
        var localDirection = Vector3.TransformNormal(unitDirection, inverse);
        if (!IsFinite(localOrigin) || !IsFinite(localDirection)
            || localDirection.LengthSquared() <= 1e-20f)
            return false;

        var indices = _mesh.TriangleIndices;
        var best = maxDistance;
        var hit = false;
        for (var i = 0; i < indices.Length; i += 3)
        {
            var a = Positions[indices[i]];
            var b = Positions[indices[i + 1]];
            var c = Positions[indices[i + 2]];
            if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c))
                continue;

            var edge1 = b - a;
            var edge2 = c - a;
            var p = Vector3.Cross(localDirection, edge2);
            var determinant = Vector3.Dot(edge1, p);
            if (!float.IsFinite(determinant) || MathF.Abs(determinant) <= 1e-8f)
                continue;

            var inverseDeterminant = 1f / determinant;
            var fromA = localOrigin - a;
            var u = Vector3.Dot(fromA, p) * inverseDeterminant;
            if (!float.IsFinite(u) || u < 0f || u > 1f)
                continue;

            var q = Vector3.Cross(fromA, edge1);
            var v = Vector3.Dot(localDirection, q) * inverseDeterminant;
            if (!float.IsFinite(v) || v < 0f || u + v > 1f)
                continue;

            var candidate = Vector3.Dot(edge2, q) * inverseDeterminant;
            if (!float.IsFinite(candidate) || candidate < 0f || candidate > best)
                continue;

            best = candidate;
            hit = true;
        }

        if (hit)
            distance = best;
        return hit;
    }

    private void SamplePose(PrsAnimation clip, float time)
    {
        ClipTime = Math.Clamp(time, 0f, clip.AnimLength);
        AnimationRuntime.ComputeSkinMatrices(
            _mesh, clip, ClipTime, SkinMatrices.AsSpan(), anchorRoot: false);

        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        for (var c = 0; c < Positions.Length; c++)
        {
            var source = _mesh.Positions[_mesh.Corners[c].VertexIndex];
            var weights = _mesh.SkinWeights[c];
            var bones = _mesh.SkinBones[c];
            var posed = Vector3.Zero;
            if (weights.X > 0f)
                posed += weights.X * Vector3.Transform(source, SkinMatrices[bones & 0xFF]);
            if (weights.Y > 0f)
                posed += weights.Y * Vector3.Transform(source, SkinMatrices[(bones >> 8) & 0xFF]);
            if (weights.Z > 0f)
                posed += weights.Z * Vector3.Transform(source, SkinMatrices[(bones >> 16) & 0xFF]);
            if (weights.W > 0f)
                posed += weights.W * Vector3.Transform(source, SkinMatrices[(bones >> 24) & 0xFF]);

            // The skin shader sums weighted mat4s, so clip-space perspective
            // division removes their homogeneous weight sum. Match that
            // rendered position when picking meshes with non-unit weights.
            var weightSum = weights.X + weights.Y + weights.Z + weights.W;
            if (MathF.Abs(weightSum) > 1e-8f)
                posed /= weightSum;

            Positions[c] = posed;
            min = Vector3.Min(min, posed);
            max = Vector3.Max(max, posed);
        }

        Min = min;
        Max = max;
        Center = min + (max - min) * 0.5f;
    }

    private static void ValidateMesh(AspMesh mesh)
    {
        var boneCount = mesh.BoneCount;
        if (boneCount == 0)
            throw new ArgumentException("Usable transition poses require a rigged ASP mesh.", nameof(mesh));
        if (!mesh.HasSkin)
            throw new ArgumentException("Usable transition poses require WCRN skin data.", nameof(mesh));
        if (mesh.Corners.Length == 0)
            throw new ArgumentException("Usable transition poses require at least one mesh corner.", nameof(mesh));
        if (mesh.Positions.Length == 0)
            throw new ArgumentException("Usable transition poses require vertex positions.", nameof(mesh));
        if (mesh.BoneParents.Length != boneCount || mesh.BindPose.Length != boneCount
            || mesh.InverseBindMatrices.Length != boneCount)
            throw new ArgumentException(
                "ASP bone parents, bind pose, and inverse bind matrices must match BoneCount.",
                nameof(mesh));
        if (mesh.SkinWeights.Length != mesh.Corners.Length
            || mesh.SkinBones.Length != mesh.Corners.Length)
            throw new ArgumentException("ASP skin arrays must be parallel to Corners.", nameof(mesh));
        if (mesh.TriangleIndices.Length % 3 != 0)
            throw new ArgumentException("ASP triangle indices must contain complete triplets.", nameof(mesh));

        for (var c = 0; c < mesh.Corners.Length; c++)
        {
            if ((uint)mesh.Corners[c].VertexIndex >= (uint)mesh.Positions.Length)
                throw new ArgumentException($"ASP corner {c} has an invalid vertex index.", nameof(mesh));

            var weights = mesh.SkinWeights[c];
            if (!IsFinite(weights))
                throw new ArgumentException($"ASP corner {c} has non-finite skin weights.", nameof(mesh));
            var bones = mesh.SkinBones[c];
            if ((weights.X > 0f && (bones & 0xFF) >= boneCount)
                || (weights.Y > 0f && ((bones >> 8) & 0xFF) >= boneCount)
                || (weights.Z > 0f && ((bones >> 16) & 0xFF) >= boneCount)
                || (weights.W > 0f && ((bones >> 24) & 0xFF) >= boneCount))
                throw new ArgumentException($"ASP corner {c} references a missing skin bone.", nameof(mesh));
        }

        for (var i = 0; i < mesh.TriangleIndices.Length; i++)
        {
            if ((uint)mesh.TriangleIndices[i] >= (uint)mesh.Corners.Length)
                throw new ArgumentException($"ASP triangle index {i} references a missing corner.", nameof(mesh));
        }
    }

    private static void ValidateClip(PrsAnimation clip, string parameterName)
    {
        if (!float.IsFinite(clip.AnimLength) || clip.AnimLength < 0f)
            throw new ArgumentException("Animation length must be finite and non-negative.", parameterName);
        if (clip.NumBones < 0 || clip.BoneNames.Count != clip.NumBones
            || clip.BoneKeys.Count != clip.NumBones)
            throw new ArgumentException("Animation bone arrays must match NumBones.", parameterName);
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Vector4 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static bool IsFinite(Matrix4x4 value)
        => float.IsFinite(value.M11) && float.IsFinite(value.M12)
        && float.IsFinite(value.M13) && float.IsFinite(value.M14)
        && float.IsFinite(value.M21) && float.IsFinite(value.M22)
        && float.IsFinite(value.M23) && float.IsFinite(value.M24)
        && float.IsFinite(value.M31) && float.IsFinite(value.M32)
        && float.IsFinite(value.M33) && float.IsFinite(value.M34)
        && float.IsFinite(value.M41) && float.IsFinite(value.M42)
        && float.IsFinite(value.M43) && float.IsFinite(value.M44);
}

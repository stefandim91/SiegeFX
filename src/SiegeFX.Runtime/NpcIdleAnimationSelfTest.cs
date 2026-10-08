using System.Globalization;
using System.Numerics;
using SiegeFX.Core.Actors;
using SiegeFX.Core.Assets;
using SiegeFX.Core.Sfx;
using SiegeFX.Core.Tank;

namespace SiegeFX.Runtime;

/// <summary>Checks the idle chore chosen for real Utraea actor content without
/// starting a graphics window or copying game assets into the repository.</summary>
internal static class NpcIdleAnimationSelfTest
{
    public static bool Run(string installRoot)
    {
        try
        {
            using var map = TankFile.Open(Path.Combine(installRoot, "Maps", "MpWorld.dsmap"));
            using var logic = TankFile.Open(Path.Combine(installRoot, "Resources", "Logic.dsres"));
            using var objects = TankFile.Open(Path.Combine(installRoot, "Resources", "Objects.dsres"));
            var mapReader = new TankReader(map);
            var logicReader = new TankReader(logic);
            var objectsReader = new TankReader(objects);
            var (templates, _) = TemplateStore.LoadFromTank(logicReader);
            var resolver = new AssetResolver();
            resolver.Add(mapReader, "MpWorld.dsmap");
            resolver.Add(objectsReader, "Objects.dsres");
            resolver.Add(logicReader, "Logic.dsres");
            var (placements, _) = RegionObjects.LoadActors(mapReader,
                "/world/maps/multiplayer_world/regions/town_center", multiplayerContent: true);
            var krug = placements.First(p => p.Scid == 0x03200D53);
            var blacksmith = placements.First(p => p.Scid == 0x03200C26);
            var noAutoFidget = placements.First(p => p.Scid == 0x03200CC8);
            var dog = ActorInstance.CreateSynthetic("dog_mp", 0xEE000001,
                Vector3.Zero, Quaternion.Identity);
            var templateOnlyBlacksmith = ActorInstance.CreateSynthetic(
                blacksmith.TemplateName, 0xEE000002, Vector3.Zero, Quaternion.Identity);
            var spawner = new ActorSpawner(templates, resolver);
            var actors = spawner.Spawn(new[]
                { krug, blacksmith, noAutoFidget, dog, templateOnlyBlacksmith });
            Actor Find(uint scid) => actors.First(a => a.Instance.Scid == scid);
            var krugActor = Find(krug.Scid);
            int fidget = krugActor.GetClipIndex("chore_fidget");
            if (fidget < 0 || krugActor.CurrentClipIndex != fidget)
                throw new InvalidDataException("Krug did not enter its authored fidget chore.");
            if (Moves(krugActor, fidget) is false)
                throw new InvalidDataException("Selected Krug fidget has no changing pose.");
            krugActor.Host.OverrideAnimIndex(0, 0.2f);
            if (krugActor.CurrentClipIndex != 0)
                throw new InvalidDataException("Explicit default-pose override lost priority.");
            krugActor.Host.TickOverride(0.21f);
            if (krugActor.CurrentClipIndex != fidget)
                throw new InvalidDataException("Idle fidget did not resume after default-pose override.");
            int attack = krugActor.GetClipIndex("chore_attack");
            krugActor.PlayChoreOnce("chore_attack", 0.2f);
            if (attack < 0 || krugActor.CurrentClipIndex != attack)
                throw new InvalidDataException("Attack override lost priority to idle fidget.");
            krugActor.Host.TickOverride(0.21f);
            if (krugActor.CurrentClipIndex != fidget)
                throw new InvalidDataException("Idle fidget did not resume after attack.");
            int die = krugActor.GetClipIndex("chore_die");
            krugActor.PlayChoreOnce("chore_die", float.PositiveInfinity);
            if (die < 0 || krugActor.CurrentClipIndex != die)
                throw new InvalidDataException("Death override lost priority to idle fidget.");
            var smith = Find(blacksmith.Scid);
            VerifyZabarScaleAuthoring(templates, blacksmith, smith,
                Find(templateOnlyBlacksmith.Scid));
            if (smith.CurrentClipIndex == 0 || !Moves(smith, smith.CurrentClipIndex))
                throw new InvalidDataException("Blacksmith's authored idle does not move.");
            var smithClip = smith.Clips[smith.CurrentClipIndex];
            Span<int> sfxCounts = stackalloc int[4];
            AnimationNoteEvents.AccumulateSfxEvents(smithClip, 0.7, 0.9, true, sfxCounts);
            if (sfxCounts[0] != 1 || sfxCounts[1] != 0)
                throw new InvalidDataException("Smith SFX1 cue did not cross at its authored time.");
            sfxCounts.Clear();
            AnimationNoteEvents.AccumulateSfxEvents(smithClip, 1.4, 2.4, true, sfxCounts);
            if (sfxCounts[0] != 1)
                throw new InvalidDataException("Smith SFX1 cue did not repeat on the next loop.");
            VerifyActorVisualPolicy(smith);
            spawner.SpawnTriggers(new[] { blacksmith });
            var triggerCapture = new SfxTriggerCapture();
            spawner.TriggerRuntime.PostInboundMessage(blacksmith.Scid, "we_anim_sfx", 2);
            spawner.TriggerRuntime.Tick(0.05, triggerCapture);
            if (triggerCapture.Scripts.Count != 0)
                throw new InvalidDataException("SFX2 incorrectly fired Zabar's SFX1 row.");
            spawner.TriggerRuntime.PostInboundMessage(blacksmith.Scid, "we_anim_sfx", 1);
            spawner.TriggerRuntime.Tick(0.05, triggerCapture);
            if (triggerCapture.Scripts.Count != 1
                || triggerCapture.Scripts[0] != "blacksmith_hammer")
                throw new InvalidDataException("SFX1 did not dispatch Zabar's authored hammer script.");
            if (triggerCapture.LastTrigger?.Scid != blacksmith.Scid
                || triggerCapture.LastTrigger.Position != smith.WorldTransform.Translation)
                throw new InvalidDataException("Zabar's SFX1 dispatch lost its originating Game Object.");
            for (int i = 0; i < 6; i++)
            {
                spawner.TriggerRuntime.PostInboundMessage(blacksmith.Scid, "we_anim_sfx", 2);
                spawner.TriggerRuntime.Tick(0.05, triggerCapture);
            }
            spawner.TriggerRuntime.PostInboundMessage(blacksmith.Scid, "we_anim_sfx", 1);
            spawner.TriggerRuntime.Tick(0.05, triggerCapture);
            if (triggerCapture.Scripts.Count != 2)
                throw new InvalidDataException("Unmatched SFX2 cues blocked a later SFX1 hammer impact.");
            spawner.TriggerRuntime.PostInboundMessage(blacksmith.Scid, "we_anim_sfx", 1);
            spawner.TriggerRuntime.PostInboundMessage(blacksmith.Scid, "we_anim_sfx", 1);
            spawner.TriggerRuntime.Tick(0.05, triggerCapture);
            spawner.TriggerRuntime.Tick(0.05, triggerCapture);
            if (triggerCapture.Scripts.Count != 4)
                throw new InvalidDataException("Consecutive SFX1 messages did not each fire the hammer effect.");
            var effects = SfxScriptStore.LoadFromTank(logicReader);
            if (!effects.TryGet("blacksmith_hammer", out var hammerEffect)
                || !hammerEffect.Body.Contains("s_e_env_hammer_anvil", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Zabar's hammer effect has no authored anvil sound.");
            VerifyZabarHammerAnchor(effects, smith, smithClip, triggerCapture.LastTrigger!);
            using var sound = TankFile.Open(Path.Combine(installRoot, "Resources", "Sound.dsres"));
            if (!new TankReader(sound).TryGetFile("/sound/effects/s_e_env_hammer_anvil.wav", out _))
                throw new InvalidDataException("Zabar's authored hammer sound is missing.");
            var townfolk = Find(noAutoFidget.Scid);
            if (!townfolk.Host.IsOverrideActive || townfolk.IdleClipIndex != 0
                || townfolk.CurrentClipIndex != townfolk.GetClipIndex("chore_fidget"))
                throw new InvalidDataException("Authored one-shot initial fidget did not start.");
            townfolk.Host.TickOverride(townfolk.Clips[townfolk.CurrentClipIndex].AnimLength + 0.01f);
            if (townfolk.Host.IsOverrideActive || townfolk.CurrentClipIndex != 0)
                throw new InvalidDataException("actor_auto_fidgets=false replayed its initial fidget.");
            var dogActor = Find(dog.Scid);
            if (dogActor.CurrentClipIndex != 0 || !Moves(dogActor, 0))
                throw new InvalidDataException("Dog's authored default loop is not animated.");

            // The same engine path also serves Ehb. Guard against fixing the
            // multiplayer-authored placements while regressing its campaign.
            using var ehbMap = TankFile.Open(Path.Combine(installRoot, "Maps", "World.dsmap"));
            var ehbReader = new TankReader(ehbMap);
            var (ehbPlacements, _) = RegionObjects.LoadActors(ehbReader,
                "/world/maps/map_world/regions/fh_r1");
            var ehbKrug = ehbPlacements.First(p => p.Scid == 0x01C00AE0);
            var ehbResolver = new AssetResolver();
            ehbResolver.Add(ehbReader, "World.dsmap");
            ehbResolver.Add(objectsReader, "Objects.dsres");
            ehbResolver.Add(logicReader, "Logic.dsres");
            var ehbActor = new ActorSpawner(templates, ehbResolver)
                .Spawn(new[] { ehbKrug }).Single();
            int ehbFidget = ehbActor.GetClipIndex("chore_fidget");
            if (ehbFidget < 0 || ehbActor.CurrentClipIndex != ehbFidget
                || !Moves(ehbActor, ehbFidget))
                throw new InvalidDataException("Ehb Krug lost its authored idle animation.");

            Console.WriteLine("[selftest-npc-idle] PASS — Utraea and Ehb idles, Zabar visual clip/scale and SFX stand anchor/sound, dog loop, override priority");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[selftest-npc-idle] FAIL: " + ex);
            return false;
        }
    }

    private static bool Moves(Actor actor, int clipIndex)
    {
        var clip = actor.Clips[clipIndex];
        if (clip.AnimLength < 0.3f) return false;
        var start = AnimationRuntime.ComputeSkinMatrices(actor.Mesh, clip, 0f);
        var later = AnimationRuntime.ComputeSkinMatrices(actor.Mesh, clip, clip.AnimLength * 0.45f);
        return start.Zip(later).Any(pair => pair.First != pair.Second);
    }

    private static void VerifyActorVisualPolicy(Actor smith)
    {
        if (smith.WalkClipIndex < 0 || smith.WalkClipIndex >= smith.Clips.Length)
            throw new InvalidDataException("Zabar has no authored walk clip for visual-policy coverage.");
        if (Render.RenderHost.EffectiveActorClipIndex(smith, isDead: false, isMoving: true)
            != smith.WalkClipIndex)
            throw new InvalidDataException("Moving Zabar did not select his authored walk clip.");

        smith.Host.OverrideAnimIndex(0, 0.2f);
        if (!smith.Host.IsOverrideActive || smith.CurrentClipIndex != 0
            || Render.RenderHost.EffectiveActorClipIndex(smith, isDead: false, isMoving: true)
                != smith.CurrentClipIndex)
            throw new InvalidDataException("Zabar's explicit animation override lost priority while moving.");
        smith.Host.TickOverride(0.21f);

        const float scale = 1.75f;
        var transform = Matrix4x4.CreateFromQuaternion(
                Quaternion.CreateFromYawPitchRoll(0.63f, -0.17f, 0.08f))
            * Matrix4x4.CreateTranslation(13.25f, -2.5f, 7.75f);
        var posePoint = new Vector3(0.31f, 1.12f, -0.46f);
        var expectedModel = Matrix4x4.CreateScale(scale) * transform;
        var actualModel = Render.RenderHost.ActorVisualModel(transform, scale);
        var expectedPoint = Vector3.Transform(posePoint, expectedModel);
        var actualPoint = Vector3.Transform(posePoint, actualModel);
        if (Vector3.Distance(actualPoint, expectedPoint) > 0.00001f)
            throw new InvalidDataException(
                $"Scaled actor visual model transformed a posed point incorrectly: " +
                $"actual={actualPoint}, expected={expectedPoint}.");
    }

    private static void VerifyZabarScaleAuthoring(TemplateStore templates,
        ActorInstance placement, Actor placedActor, Actor templateOnlyActor)
    {
        if (!templates.TryGet(placement.TemplateName, out var template)
            || !templates.TryGet("base_npc_bs", out var blacksmithBase)
            || !EnumerateChain(template).Any(t =>
                t.Name.Equals(blacksmithBase.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                $"Zabar's {placement.TemplateName} template does not specialize base_npc_bs.");

        static float ReadScale(string? authored, string source)
        {
            if (!float.TryParse(authored, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float scale))
                throw new InvalidDataException($"{source} has no valid scale_base value: '{authored}'.");
            return scale;
        }

        float templateScale = ReadScale(
            TemplateStore.GetNodeAttribute(blacksmithBase.Node, "aspect", "scale_base"),
            "base_npc_bs template");
        float placementScale = ReadScale(
            TemplateStore.GetNodeAttribute(placement.Node, "aspect", "scale_base"),
            "Zabar placement");
        if (MathF.Abs(templateScale - 0.85f) > 0.0001f)
            throw new InvalidDataException(
                $"base_npc_bs scale_base changed: actual={templateScale}, expected=0.85.");
        if (MathF.Abs(placementScale - 1f) > 0.0001f)
            throw new InvalidDataException(
                $"Zabar's placement scale_base changed: actual={placementScale}, expected=1.");
        if (MathF.Abs(placedActor.Stats.RenderScale - 1f) > 0.0001f)
            throw new InvalidDataException(
                $"Zabar ignored his placement scale_base: actual={placedActor.Stats.RenderScale}, expected=1.");
        if (MathF.Abs(templateOnlyActor.Stats.RenderScale - 0.85f) > 0.0001f)
            throw new InvalidDataException(
                $"Template-only blacksmith lost base_npc_bs scale_base: " +
                $"actual={templateOnlyActor.Stats.RenderScale}, expected=0.85.");

        var multiplierOverride = GasDocument.Parse(
            "[synthetic] { [aspect] { scale_multiplier = 1.25; } }").Roots.Single();
        float multipliedScale = ActorStats.FromTemplate(
            templates, blacksmithBase, multiplierOverride).RenderScale;
        float expectedMultipliedScale = templateScale * 1.25f;
        if (MathF.Abs(multipliedScale - expectedMultipliedScale) > 0.0001f)
            throw new InvalidDataException(
                $"Instance scale_multiplier did not override the template value: " +
                $"actual={multipliedScale}, expected={expectedMultipliedScale}.");

        static IEnumerable<Template> EnumerateChain(Template leaf)
        {
            for (Template? current = leaf; current is not null; current = current.Specializes)
                yield return current;
        }
    }

    private static void VerifyZabarHammerAnchor(SfxScriptStore effects, Actor smith,
        PrsAnimation clip, TriggerInstance trigger)
    {
        // Measure the actor at the shipped clip's actual SFX1 frame. The live
        // renderer resolves body_posterior to this posed pelvis, so this test
        // exercises real placement and animation geometry rather than a
        // synthetic context assembled around the desired answer.
        const uint SfxPrefix = 0x00584653; // little-endian "SFX"
        var cue = clip.Notes.FirstOrDefault(n =>
            (n.Token & 0x00FFFFFF) == SfxPrefix && (n.Token >> 24) == '1');
        if (cue.Token == 0)
            throw new InvalidDataException("Zabar's authored fidget has no SFX1 cue.");
        float cueTime = Math.Clamp(cue.Time, 0f, 1f) * clip.AnimLength;
        var boneWorlds = AnimationRuntime.ComputeAnimatedBoneWorlds(smith.Mesh, clip, cueTime);
        int pelvisIndex = -1;
        for (int i = 0; i < smith.Mesh.BoneNames.Count; i++)
            if (smith.Mesh.BoneNames[i].Equals("bip01_pelvis", StringComparison.OrdinalIgnoreCase))
            {
                pelvisIndex = i;
                break;
            }
        if (pelvisIndex < 0)
            throw new InvalidDataException("Zabar's shipped mesh has no body_posterior/pelvis bone.");

        var actorRoot = smith.WorldTransform.Translation;
        var actorModel = Render.RenderHost.ActorVisualModel(
            smith.WorldTransform, smith.Stats.RenderScale);
        var pelvis = (boneWorlds[pelvisIndex] * actorModel).Translation;
        var orientation = Quaternion.CreateFromRotationMatrix(smith.WorldTransform);
        var sink = new HammerEffectCapture();
        var runtime = new SfxRuntime(effects, sink)
        {
            SoundSink = (name, position) =>
            {
                sink.Sounds.Add((name, position));
                return true;
            },
        };
        var context = SfxContext.At(actorRoot) with
        {
            TargetObjectAnchor = pelvis,
            SourceOrientation = orientation,
        };
        if (!runtime.Spawn("blacksmith_hammer", context))
            throw new InvalidDataException("Zabar's authored hammer effect did not execute.");
        runtime.Tick(0.05f);
        if (sink.Explosions.Count != 1)
            throw new InvalidDataException($"Zabar hammer produced {sink.Explosions.Count} impacts instead of one.");

        // The anvil top is independently measured from the shipped stand and
        // placement: 0.683 m in front of Zabar and 0.843 m above his feet.
        // A loose 30 cm envelope tolerates animation and mesh-origin variance
        // while still catching root/pelvis anchoring and facing regressions.
        var forward = Vector3.Transform(Vector3.UnitZ, orientation);
        forward.Y = 0f;
        forward = Vector3.Normalize(forward);
        var standTop = actorRoot + forward * 0.683f + Vector3.UnitY * 0.843f;
        var impact = sink.Explosions[0].Anchor;
        var horizontalError = new Vector2(impact.X - standTop.X, impact.Z - standTop.Z).Length();
        var verticalError = MathF.Abs(impact.Y - standTop.Y);
        if (horizontalError > 0.30f || verticalError > 0.30f)
            throw new InvalidDataException(
                $"Zabar hammer impact missed the authored stand top: impact={impact}, stand={standTop}, " +
                $"horizontal error={horizontalError:F3}, vertical error={verticalError:F3}.");

        if (sink.Sounds.Count != 1
            || !sink.Sounds[0].Name.Equals("s_e_env_hammer_anvil", StringComparison.OrdinalIgnoreCase)
            || Vector3.Distance(sink.Sounds[0].Position, actorRoot) > 0.001f)
            throw new InvalidDataException("Zabar's hammer sound did not remain at #TARGET_POSITION (actor root).");
        if (Quaternion.Dot(trigger.Orientation, orientation) is < 0.999f and > -0.999f)
            throw new InvalidDataException("Zabar's SFX trigger lost its authored facing.");
    }

    private sealed class SfxTriggerCapture : TriggerContext
    {
        public List<string> Scripts { get; } = new();
        public TriggerInstance? LastTrigger { get; private set; }

        public override void CallSfxScript(string scriptName,
            IReadOnlyList<string>? args, Vector3 origin) => Scripts.Add(scriptName);

        public override void CallSfxScript(string scriptName,
            IReadOnlyList<string>? args, TriggerInstance trigger)
        {
            LastTrigger = trigger;
            Scripts.Add(scriptName);
        }
    }

    private sealed class HammerEffectCapture : IParticleSink
    {
        public List<ExplosionSpec> Explosions { get; } = new();
        public List<(string Name, Vector3 Position)> Sounds { get; } = new();

        public void SpawnExplosion(in ExplosionSpec spec) => Explosions.Add(spec);
        public void SpawnFire(Vector3 p, Vector4 c, float s, float d, int n = 12) { }
        public void SpawnSmoke(Vector3 p, Vector4 c, float s, float d, int n = 8) { }
        public void SpawnSteam(Vector3 p, Vector4 c, float s, float d, int n = 8) { }
        public void SpawnSpark(Vector3 p, Vector4 c, float s, float d, int n = 16) { }
        public void SpawnLightning(Vector3 a, Vector3 b, Vector4 c, float d) { }
        public void SpawnLightning(Vector3 a, Vector3 b, Vector4 c, float d, float displace) { }
        public void SpawnLightning(Vector3 a, Vector3 b, Vector4 c, float d,
            float minDisplace, float maxDisplace, float subd, float minSubd) { }
        public void SpawnCylinderTube(in CylinderSpec spec) { }
        public void SpawnSrayTimed(in SraySpec spec) { }
        public void SpawnFlurry(in FlurrySpec spec) { }
        public float MaintainPlume(in PlumeSpec spec, Vector3 p, float age, float dt, float carry) => carry;
        public void SetFollowAnchor(int id, Vector3 p) { }
        public void ClearFollowAnchor(int id) { }
        public void BurstPlume(in PlumeSpec spec, Vector3 p, int n) { }
        public void SpawnSpe(in SpeSpec spec) { }
        public void SpawnSparkles(in SparklesSpec spec) { }
        public void SpawnCharge(in ChargeSpec spec) { }
        public void SpawnPolyExplosion(in PolyExplosionSpec spec) { }
        public void SpawnSphereMesh(in SphereMeshSpec spec) { }
        public void SpawnLineTracer(Vector3 a, Vector3 b, Vector4 c0, Vector4 c1,
            float fadeRate, float tin, float tout) { }
        public void SpawnProjectile(Vector3 a, Vector3 b, Vector4 c, float s, float speed, int kind) { }
        public float MaintainFire(Vector3 p, Vector4 c, float s, float dt, float rate, float carry) => carry;
        public float MaintainSmoke(Vector3 p, Vector4 c, float s, float dt, float rate, float carry) => carry;
        public float MaintainSteam(Vector3 p, Vector4 c, float s, float dt, float rate, float carry) => carry;
        public void SpawnCylinder(Vector3 p, Vector4 c, float radius, float thickness,
            float spin, float tin, float tout, float duration, byte tex, byte segments) { }
        public void SpawnSray(Vector3 p, Vector4 c0, Vector4 c1, float minLength,
            float maxLength, float startWidth, float endWidth, float duration, int rays) { }
        public void SpawnFireb(Vector3 p, Vector4 c, Vector3 velocity, Vector3 accel,
            float lifetime, float maxDisplace, float lowerRadius, float upperRadius,
            int count, float flameSize) { }
        public float MaintainGlow(Vector3 p, Vector4 c, float radius,
            float dt, float rate, float carry) => carry;
        public void SpawnSphere(Vector3 p, Vector4 c, float radius, float duration, int count) { }
    }
}

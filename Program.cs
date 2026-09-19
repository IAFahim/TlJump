using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Frent;
using Tl;
using Tl.TestSupport;

var jump = TimelineAsset.Load(new DomainBaker()
    .Track<JumpTrack, JumpClip>(new JumpTrack())
    .Clip(0, 0, 1, new JumpClip(4f))
    .Clip(0, 1, 2, new JumpClip(3f))
    .Clip(0, 2, 3, new JumpClip(1f))
    .Clip(0, 3, 4, new JumpClip(-1f))
    .Clip(0, 4, 5, new JumpClip(-3f))
    .Clip(0, 5, 6, new JumpClip(-4f))
    .Looping()
    .Bake());
var sound = TimelineAsset.Load(new DomainBaker()
    .Track<SoundTrack, SoundClip>(new SoundTrack())
    .Clip(0, 0, 1, new SoundClip(1f))
    .Clip(0, 5, 6, new SoundClip(2f))
    .Looping()
    .Bake());

using var world = new World();
var spec = new SpawnSpec(jump, sound);
Timeline.Bake(sound, world);
for (var i = 0; i < 4; i++) Timeline.Bake(jump, world, spec);

for (var frame = 0; frame < 14; frame++)
{
    foreach (var (jumpTls, soundTls, clocks, jumps, sfxs) in
             world.Query<JumpTl, SoundTl, Clock, JumpY, Sfx>()
                  .EnumerateChunks<JumpTl, SoundTl, Clock, JumpY, Sfx>())
    {
        var jumpIds = MemoryMarshal.Cast<JumpTl, ushort>(jumpTls);
        var soundIds = MemoryMarshal.Cast<SoundTl, ushort>(soundTls);
        var clockCol = MemoryMarshal.Cast<Clock, ushort>(clocks);
        var jumpCol = MemoryMarshal.Cast<JumpY, float>(jumps);
        var sfxCol = MemoryMarshal.Cast<Sfx, float>(sfxs);

        Timeline<JumpTrack, JumpClip>.Apply(jumpIds, clockCol, true, jumpCol);
        Timeline<SoundTrack, SoundClip>.Apply(soundIds, clockCol, true, sfxCol);
        Timeline.Step(jumpIds, clockCol, true);

        for (var k = 0; k < sfxCol.Length; k++)
        {
            if (sfxCol[k] == 1f) Console.WriteLine($"  entity {k}: jump!");
            if (sfxCol[k] == 2f) Console.WriteLine($"  entity {k}: land!");
            sfxCol[k] = 0f;
        }
        Console.Write($"frame {frame}: y =");
        for (var k = 0; k < jumpCol.Length; k++) Console.Write($" {jumpCol[k]:F0}");
        Console.WriteLine();
    }
}

public record struct SpawnSpec(ushort Jump, ushort Sound);

public struct JumpTl { public ushort Id; public JumpTl(ushort v) => Id = v; }
public struct SoundTl { public ushort Id; public SoundTl(ushort v) => Id = v; }
public struct Clock { public ushort Value; public Clock(ushort v) => Value = v; }
public struct JumpY { public float Value; }
public struct Sfx { public float Value; }

public readonly record struct JumpClip(float Height);
public readonly record struct JumpTrack : IBlend<JumpClip>
{
    public void Blend(in JumpClip a, in JumpClip b, float t, out JumpClip o)
        => o = new JumpClip(a.Height + (b.Height - a.Height) * t);
}
public readonly record struct SoundClip(float Code);
public readonly record struct SoundTrack : IBlend<SoundClip>
{
    public void Blend(in SoundClip a, in SoundClip b, float t, out SoundClip o) => o = a;
}

public readonly struct ApplyJump : ITrack<JumpTrack, JumpClip>
{
    public static void Execute(in Frame<JumpTrack, JumpClip> frame, ref float y)
        => y += frame.Clip.Height;
}

public readonly struct ApplySound : ITrack<SoundTrack, SoundClip>
{
    public static void Execute(in Frame<SoundTrack, SoundClip> frame, ref float sfx)
        => sfx += frame.Clip.Code;
}

public readonly struct SpawnJumper : IBake<ApplyJump, World, SpawnSpec>
{
    public static void Bake(ApplyJump consumer, World world, SpawnSpec spec)
        => world.Create<JumpTl, SoundTl, Clock, JumpY, Sfx>(
            new JumpTl(spec.Jump), new SoundTl(spec.Sound), new Clock(0), new JumpY(), new Sfx());
}

public readonly struct SoundAttached : IBake<ApplySound, World>
{
    public static void Bake(ApplySound consumer, World world)
        => Console.WriteLine("sound timeline attached");
}

internal static unsafe class Install
{
    [ModuleInitializer]
    internal static void Wire()
    {
        PairRuntime<JumpTrack, JumpClip>.Consume(&JumpExec, &BindFloat);
        PairRuntime<SoundTrack, SoundClip>.Consume(&SoundExec, &BindFloat);
        BakeRuntime<JumpTrack, JumpClip>.Bake(&SpawnInvoke, TypeKey<World>.Value, TypeKey<SpawnSpec>.Value);
        BakeRuntime<SoundTrack, SoundClip>.Bake(&SoundInvoke, TypeKey<World>.Value);
    }

    static void BindFloat(ulong* keys, int n, byte* table)
    {
        for (var i = 0; i < n; i++)
            if (keys[i] == TypeKey<float>.Value) { table[0] = (byte)(i + 1); return; }
    }

    static void JumpExec(byte* slot, byte* pair, ushort tick, FrameFlags flags, void** columns, int row)
    {
        var s = default(JumpClip);
        var frame = TickFrame.ToFrame<JumpTrack, JumpClip>(slot, pair, tick, flags, ref s);
        ApplyJump.Execute(in frame, ref ((float*)columns[0])[row]);
    }

    static void SoundExec(byte* slot, byte* pair, ushort tick, FrameFlags flags, void** columns, int row)
    {
        var s = default(SoundClip);
        var frame = TickFrame.ToFrame<SoundTrack, SoundClip>(slot, pair, tick, flags, ref s);
        ApplySound.Execute(in frame, ref ((float*)columns[0])[row]);
    }

    static void SpawnInvoke(object[] args) => SpawnJumper.Bake(default, (World)args[0], (SpawnSpec)args[1]);
    static void SoundInvoke(object[] args) => SoundAttached.Bake(default, (World)args[0]);
}

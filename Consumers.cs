using Frent;
using Tl;

namespace TlJump;

public readonly struct MoveY : ITrack<JumpTrack, JumpClip>
{
    public static void Execute(in Frame<JumpTrack, JumpClip> frame, ref float y)
        => y += frame.Direction * frame.Clip.Height * frame.Track.Scale;
}

public readonly struct PlaySound : ITrack<SoundTrack, SoundClip>
{
    public static void Execute(in Frame<SoundTrack, SoundClip> frame, ref float sfx)
        => sfx += frame.Clip.Code;
}

public record struct SpawnSpec(ushort Jump, ushort Sound);

public readonly struct SpawnJumper : IBake<MoveY, World, SpawnSpec>
{
    public static void Bake(MoveY consumer, World world, SpawnSpec spec)
        => world.Create<JumpTl, SoundTl, Clock, JumpY, Sfx>(
            new JumpTl(spec.Jump), new SoundTl(spec.Sound), new Clock(0), new JumpY(), new Sfx());
}

public readonly struct SoundAttached : IBake<PlaySound, World>
{
    public static void Bake(PlaySound consumer, World world)
        => Console.WriteLine("sound timeline attached");
}

public struct JumpTl { public ushort Id; public JumpTl(ushort v) => Id = v; }
public struct SoundTl { public ushort Id; public SoundTl(ushort v) => Id = v; }
public struct Clock { public ushort Value; public Clock(ushort v) => Value = v; }
public struct JumpY { public float Value; }
public struct Sfx { public float Value; }

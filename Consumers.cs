using Frent;
using Tl;

namespace TlJump;

public sealed class Game
{
    public readonly World World = new();
    public TimelineAsset Jump;
    public TimelineAsset Sound;
}

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

public readonly struct AttachJump : IBake<MoveY, Game, Entity>
{
    public static void Bake(MoveY consumer, Game game, Entity entity)
        => entity.Add(new JumpTl(game.Jump.Index));
}

public readonly struct AttachSound : IBake<PlaySound, Game, Entity>
{
    public static void Bake(PlaySound consumer, Game game, Entity entity)
        => entity.Add(new SoundTl(game.Sound.Index));
}

public struct JumpTl { public ushort Id; public JumpTl(ushort v) => Id = v; }
public struct SoundTl { public ushort Id; public SoundTl(ushort v) => Id = v; }
public struct Clock { public ushort Value; public Clock(ushort v) => Value = v; }
public struct JumpY { public float Value; }
public struct Sfx { public float Value; }

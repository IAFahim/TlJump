using Frent;
using Tl;

namespace TlJump;

public readonly struct MoveY : ITrack<JumpTrack, JumpClip>
{
    public static void OnActive(in Frame<JumpTrack, JumpClip> frame, ref float y)
        => y += frame.Direction * frame.Clip.Height * frame.Track.Scale;
}

public readonly struct PlaySound : ITrack<SoundTrack, SoundClip>
{
    public static void OnActive(in Frame<SoundTrack, SoundClip> frame, ref float channel)
    {
        if (frame.IsBackward) return;
        if (frame.Clip.Code == 1) Console.WriteLine("jump!");
        if (frame.Clip.Code == 2) Console.WriteLine("land!");
    }
}

public readonly struct AttachJump : IBake<MoveY>
{
    public static void Bake(in World world, ref Entity entity)
    {
        entity.Add(new JumpY());
    }
}

public readonly struct AttachSound : IBake<PlaySound>
{
    public static void Bake(in World world, ref Entity entity)
        => entity.Add(new Sfx());
}

public struct JumpY { public float Value; }
public struct Sfx { public float Value; }

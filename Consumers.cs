using Frent;
using Tl;

namespace TlJump;

public readonly struct MoveY : ITrack<JumpTrack, JumpClip>
{
    public static void Fold(in Frame<JumpTrack, JumpClip> frame, out float arc)
    {
        arc = frame.Direction * frame.Clip.Height * frame.Track.Scale;
    }

    public static void ExecuteActive(in float arc, ref JumpY y, in JumpPower power)
    {
        y.Value += arc * power.Value;
        Console.WriteLine(y.Value);
    }
}

public readonly struct PlaySound : ITrack<SoundTrack, SoundClip>
{
    public static void ExecuteActive(in Frame<SoundTrack, SoundClip> frame)
    {
        if (frame.IsBackward) return;
        if (frame.Clip.Code == 1) Console.WriteLine("jump!");
        if (frame.Clip.Code == 2) Console.WriteLine("land!");
    }
}

public readonly struct AttachJump : IBake<MoveY>
{
    public static void Bake(ref Span<Entity> entities)
    {
        foreach (ref var entity in entities) entity.Add(new JumpY());
    }
}

public readonly struct AttachSound : IBake<PlaySound>
{
    public static void Bake(ref Span<Entity> entities)
    {
        foreach (ref var entity in entities) entity.Add(new Sfx());
    }
}

public struct JumpY { public float Value; }
public struct Sfx { }
public struct JumpPower { public int Value; }

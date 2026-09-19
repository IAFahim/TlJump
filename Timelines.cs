using Tl;

namespace TlJump;

public record struct TimelineIndex(ushort Value);

public record struct TimelinePosition(ushort Value);

public readonly record struct JumpClip(float Height);

public readonly record struct JumpTrack(float Scale) : IBlend<JumpClip>
{
    public void Blend(in JumpClip first, in JumpClip second, float factor, out JumpClip result)
        => result = new JumpClip(first.Height + (second.Height - first.Height) * factor);
}

public readonly record struct SoundClip(ushort Code);

public readonly record struct SoundTrack : IBlend<SoundClip>
{
    public void Blend(in SoundClip first, in SoundClip second, float factor, out SoundClip result) => result = first;
}

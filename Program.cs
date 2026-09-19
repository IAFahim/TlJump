using System.Runtime.InteropServices;
using Frent;
using Tl;
using TlJump;

var jump = TimelineAsset.Of(TimelineAsset.Load(File.ReadAllBytes("jump.tlb")));

using var world = new World();
for (var i = 0; i < 4; i++)
{
    var entity = world.Create();
    entity.Add(new TimelineComponent(jump.Reference));
    Timeline.Bake(jump.Index, world, entity);
}

for (var frame = 0; frame < 14; frame++)
{
    foreach (var (entity, tl, y, sfx) in
             world.Query<TimelineComponent, JumpY, Sfx>()
                  .EnumerateWithEntities<TimelineComponent, JumpY, Sfx>())
    {
        ref var c = ref tl.Value;
        foreach (var jumpFrame in Timeline.Query<JumpTrack, JumpClip>(in c))
            MoveY.Execute(in jumpFrame, ref y.Value.Value);
        foreach (var soundFrame in Timeline.Query<SoundTrack, SoundClip>(in c))
            PlaySound.Execute(in soundFrame, ref sfx.Value.Value);
        Timeline.Step(jump.Index, MemoryMarshal.CreateSpan(ref c.Position, 1), true);
    }
}

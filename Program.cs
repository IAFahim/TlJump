using Frent;
using Tl;
using TlJump;

ushort jump = TimelineAsset.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "jump.tlb")));
using var world = new World();

var entity = world.Create();
entity.Add(new Position());
entity.Add(new TimelineIndex(jump));
entity.Add(new TimelinePosition(0));

Timeline.Bake(jump, world, entity);


for (var frame = 0; frame < 14; frame++)
{
    PlayFrame(world);
}

static void PlayFrame(World world)
{
    foreach (var (ids, timelinePosition, jumps) in world
                 .Query<TimelineIndex, TimelinePosition, JumpY>()
                 .EnumerateChunks<TimelineIndex, TimelinePosition, JumpY>())
    {
        Timeline<JumpTrack, JumpClip>.Apply(ids, timelinePosition, true, jumps);
        Timeline<SoundTrack, SoundClip>.Apply(ids, timelinePosition, true);
        Timeline<JumpTrack, JumpClip>.Advance(ids, timelinePosition, true);
    }
}

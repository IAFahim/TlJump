using Frent;
using Tl;
using TlJump;

ushort jump = TimelineAsset.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "jump.tlb")));
using var world = new World();

Span<Entity> spawns =
[
    world.Create(new TimelineIndex(jump), new TimelinePosition(0), new JumpPower { Value = 2 }),
    // world.Create(new TimelineIndex(jump), new TimelinePosition(0), new JumpPower { Value = 1 }),
    // world.Create(new TimelineIndex(jump), new TimelinePosition(0), new JumpPower { Value = 3 }),
];
Timeline.Bake(jump, spawns);

for (var frame = 0; frame < 14; frame++) PlayFrame(world);

static void PlayFrame(World world)
{
    foreach (var (ids, positions, y, powers) in world
                 .Query<TimelineIndex, TimelinePosition, JumpY, JumpPower>()
                 .EnumerateChunks<TimelineIndex, TimelinePosition, JumpY, JumpPower>())
    {
        if (ids.Length == 0) continue;
        Timeline<JumpTrack, JumpClip>.Apply(ids, positions, true, y, powers);
        Timeline<SoundTrack, SoundClip>.Apply(ids, positions, true);
        Timeline<JumpTrack, JumpClip>.Advance(ids, positions, true);
    }
}

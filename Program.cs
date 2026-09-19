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
    // the whole lane: chunk spans straight into tl — Lane marshalls the columns itself
    foreach (var (ids, timelinePosition, jumps) in world
                 .Query<TimelineIndex, TimelinePosition, JumpY>()
                 .EnumerateChunks<TimelineIndex, TimelinePosition, JumpY>())
        Lane<JumpTrack, JumpClip>.Apply(ids, timelinePosition, true, jumps);

    foreach (var jumpY in world.Query<JumpY>().Enumerate<JumpY>())
        Console.WriteLine($"frame {frame}: y = {jumpY.Item1.Value.Value:F0}");
}

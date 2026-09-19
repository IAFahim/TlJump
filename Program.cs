using System.Runtime.InteropServices;
using Frent;
using Tl;
using TlJump;

ushort jump = TimelineAsset.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "jump.tlb")));
using var world = new World();

var entity = world.Create();
entity.Add(new Position());
entity.Add(new TimelineIndex(jump));
entity.Add(new TimelinePosition(0));

Timeline.Bake(jump, world, entity); // fires AttachJump + AttachSound → adds JumpY and Sfx

for (var frame = 0; frame < 14; frame++)
{
    foreach (var (ids, timelinePosition, position, jumps) in world
                 .Query<TimelineIndex, TimelinePosition, Position, JumpY>()
                 .EnumerateChunks<TimelineIndex, TimelinePosition, Position, JumpY>())
    {
        // the archetype spans ARE tl's row columns: cast in place,
        // one Apply + one Step for the whole chunk
        Timeline<JumpTrack, JumpClip>.Apply(
            MemoryMarshal.Cast<TimelineIndex, ushort>(ids),
            MemoryMarshal.Cast<TimelinePosition, ushort>(timelinePosition),
            true,
            MemoryMarshal.Cast<JumpY, float>(jumps));
        Timeline.Step(
            MemoryMarshal.Cast<TimelineIndex, ushort>(ids),
            MemoryMarshal.Cast<TimelinePosition, ushort>(timelinePosition),
            true);
        if (jumps.Length != 0) // empty archetypes the entity migrated through still match the query shape
            Console.WriteLine($"frame {frame}: y = {jumps[0].Value:F0}");
    }
}

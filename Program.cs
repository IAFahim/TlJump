using Frent;
using Tl;
using TlJump;

ushort jump = TimelineAsset.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "jump.tlb")));
using var world = new World();

var entity = world.Create();
entity.Add(new TimelineIndex(jump));
entity.Add(new TimelinePosition(0));

Timeline.Bake(jump, world, entity); // fires AttachJump + AttachSound → adds JumpY and Sfx

for (var frame = 0; frame < 14; frame++)
{
    foreach (var (ids, positions, jumps) in world
                 .Query<TimelineIndex, TimelinePosition, JumpY>()
                 .EnumerateChunks<TimelineIndex, TimelinePosition, JumpY>())
    {
        for (var i = 0; i < ids.Length; i++)
        {
            // per-row apply over component fields: single-element spans wrap the
            // archetype storage itself — no gather/scatter, no managed buffers
            Timeline<JumpTrack, JumpClip>.Apply(ids[i].Value,
                new ReadOnlySpan<ushort>(in positions[i].Value), true,
                new Span<float>(ref jumps[i].Value));
            Timeline.Step(ids[i].Value, new Span<ushort>(ref positions[i].Value), true);
            Console.WriteLine($"frame {frame}: y = {jumps[i].Value:F0}");
        }
    }
}

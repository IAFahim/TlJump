using Frent;
using Tl;
using TlJump;

ushort jump = TimelineAsset.Load(File.ReadAllBytes("jump.tlb"));
using var asset = TimelineAsset.Of(jump);
using var world = new World();

var entity = world.Create();
entity.Add(new TimelineComponent(asset.Reference));
Timeline.Bake(jump, world, entity);

for (var frame = 0; frame < 14; frame++)
{
    foreach (var (_, c, y) in
             world.Query<TimelineComponent, JumpY>()
                  .EnumerateWithEntities<TimelineComponent, JumpY>())
    {
        ref var t = ref c.Value;
        Timeline<JumpTrack, JumpClip>.Apply(jump, new ReadOnlySpan<ushort>(in t.Position), true, new Span<float>(ref y.Value.Value));
        Timeline.Step(jump, new Span<ushort>(ref t.Position), true);
        Console.WriteLine($"frame {frame}: y = {y.Value.Value:F0}");
    }
}

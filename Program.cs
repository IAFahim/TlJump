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

if (args.Contains("--verify"))
{
    ReadOnlySpan<float> golden = [3, 6, 9, 6, 3, 0, 3, 6, 9, 6, 3, 0, 3, 6];
    for (var frame = 0; frame < golden.Length; frame++)
    {
        PlayFrame(world);
        foreach (var jumpY in world.Query<JumpY>().Enumerate<JumpY>())
            if (jumpY.Item1.Value.Value != golden[frame])
                throw new InvalidOperationException($"frame {frame}: y = {jumpY.Item1.Value.Value}, expected {golden[frame]}");
    }
    Console.WriteLine("ok");
    return;
}

for (var frame = 0; frame < 14; frame++)
{
    PlayFrame(world);
    foreach (var jumpY in world.Query<JumpY>().Enumerate<JumpY>())
        Console.WriteLine($"frame {frame}: y = {jumpY.Item1.Value.Value:F0}");
}

// the whole lane: chunk spans straight into tl — Lane marshalls the columns itself
static void PlayFrame(World world)
{
    foreach (var (ids, timelinePosition, jumps) in world
                 .Query<TimelineIndex, TimelinePosition, JumpY>()
                 .EnumerateChunks<TimelineIndex, TimelinePosition, JumpY>())
    {
        Lane<JumpTrack, JumpClip>.Apply(ids, timelinePosition, true, jumps);
        Lane<JumpTrack, JumpClip>.Advance(ids, timelinePosition, true);
    }
}

using Frent;
using Tl;
using TlJump;

ushort jump = TimelineAsset.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "jump.tlb")));
using var world = new World();

var entity = world.Create();
entity.Add(new TimelineIndex(jump));
entity.Add(new TimelinePosition(0));

Timeline.Bake(jump, world, entity);

var verify = args.Contains("--verify");
var expected = new[] { 3f, 6f, 9f, 6f, 3f, 0f };
for (var frame = 0; frame < 14; frame++)
{
    var y = PlayFrame(world);
    if (verify)
    {
        if (y != expected[frame % expected.Length])
            throw new InvalidOperationException($"frame {frame}: y = {y}, expected {expected[frame % expected.Length]}");
    }
    else
    {
        Console.WriteLine($"frame {frame}: y = {y}");
    }
}
if (verify) Console.WriteLine("ok");

static float PlayFrame(World world)
{
    var sum = 0f;
    foreach (var (ids, positions, jumps) in world
                 .Query<TimelineIndex, TimelinePosition, JumpY>()
                 .EnumerateChunks<TimelineIndex, TimelinePosition, JumpY>())
    {
        Timeline<SoundTrack, SoundClip>.Apply(ids, positions, true);
        Timeline<JumpTrack, JumpClip>.Apply(ids, positions, true, jumps);
        Timeline<JumpTrack, JumpClip>.Advance(ids, positions, true);
        for (var i = 0; i < jumps.Length; i++)
            sum += jumps[i].Value;
    }
    return sum;
}

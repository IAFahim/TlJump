using System.Runtime.InteropServices;
using Frent;
using Tl;
using TlJump;

ushort jump = TimelineAsset.Load(File.ReadAllBytes("jump.tlb"));
ushort sound = TimelineAsset.Load(File.ReadAllBytes("sound.tlb"));

using var world = new World();
for (var i = 0; i < 4; i++)
{
    var entity = world.Create<Clock, JumpY, Sfx>(new Clock(0), new JumpY(), new Sfx());
    Timeline.Bake(jump, world, entity, new JumpTl(jump));
    Timeline.Bake(sound, world, entity, new SoundTl(sound));
}

for (var frame = 0; frame < 14; frame++)
{
    foreach (var (jumpTls, soundTls, clocks, jumps, sfxs) in
             world.Query<JumpTl, SoundTl, Clock, JumpY, Sfx>()
                  .EnumerateChunks<JumpTl, SoundTl, Clock, JumpY, Sfx>())
    {
        var jumpIds = MemoryMarshal.Cast<JumpTl, ushort>(jumpTls);
        var soundIds = MemoryMarshal.Cast<SoundTl, ushort>(soundTls);
        var clockCol = MemoryMarshal.Cast<Clock, ushort>(clocks);
        var jumpCol = MemoryMarshal.Cast<JumpY, float>(jumps);
        var sfxCol = MemoryMarshal.Cast<Sfx, float>(sfxs);

        Timeline<JumpTrack, JumpClip>.Apply(jumpIds, clockCol, true, jumpCol);
        Timeline<SoundTrack, SoundClip>.Apply(soundIds, clockCol, true, sfxCol);
        Timeline.Step(jumpIds, clockCol, true);

        for (var k = 0; k < sfxCol.Length; k++)
        {
            if (sfxCol[k] == 1f) Console.WriteLine($"  entity {k}: jump!");
            if (sfxCol[k] == 2f) Console.WriteLine($"  entity {k}: land!");
            sfxCol[k] = 0f;
        }
        Console.Write($"frame {frame}: y =");
        for (var k = 0; k < jumpCol.Length; k++) Console.Write($" {jumpCol[k]:F0}");
        Console.WriteLine();
    }
}

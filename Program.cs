using System.Runtime.InteropServices;
using Frent;
using Tl;
using TlJump;

var game = new Game
{
    Jump = TimelineAsset.Of(TimelineAsset.Load(File.ReadAllBytes("jump.tlb"))),
    Sound = TimelineAsset.Of(TimelineAsset.Load(File.ReadAllBytes("sound.tlb"))),
};

for (var i = 0; i < 4; i++)
{
    var entity = game.World.Create<Clock, JumpY, Sfx>(new Clock(0), new JumpY(), new Sfx());
    Timeline.Bake(game.Jump.Index, game, entity);
    Timeline.Bake(game.Sound.Index, game, entity);
}

for (var frame = 0; frame < 14; frame++)
{
    foreach (var (jumpTls, soundTls, clocks, jumps, sfxs) in
             game.World.Query<JumpTl, SoundTl, Clock, JumpY, Sfx>()
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

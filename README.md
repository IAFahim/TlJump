# TlJump

A minimal tl × Frent consumer, split the way the library intends:

- **Designer** authors `jump.json` / `sound.json` — plain data naming the game's C# types.
- **`tlb`** bakes each JSON into canonical `*.tlb` bytes, checked against the built game assembly.
- **Programmer** writes the `ITrack`/`IBake` structs and the frame loop; `Tl.Gen.CSharp` wires consumers and bakes at compile time.

```text
sound timeline attached            ← IBake<PlaySound, World> fired by Timeline.Bake
  entity 0..3: jump!               ← SoundClip code 1 at tick 0
frame 0: y = 3 3 3 3
frame 1: y = 6 6 6 6
frame 2: y = 9 9 9 9               ← arc peak
frame 3: y = 6 6 6 6
frame 4: y = 3 3 3 3
  entity 0..3: land!               ← SoundClip code 2 at tick 5
frame 5: y = 0 0 0 0               ← wraps, loops
```

## The split

**`jump.json`** — designer data. Two clips make the arc: `rise` adds +3 per tick for `[0,3)`, `fall` adds −3 for `[3,6)` — the loop returns to zero:

```json
{
  "name": "jump", "duration": 6, "loop": true,
  "tracks": [{
    "name": "arc", "namespace": "TlJump", "type": "JumpTrack", "data": { "Scale": 1.0 },
    "clips": [
      { "name": "rise", "type": "JumpClip", "start": 0, "end": 3, "data": { "Height": 3.0 } },
      { "name": "fall", "type": "JumpClip", "start": 3, "end": 6, "data": { "Height": -3.0 } }
    ]
  }]
}
```

`sound.json` is the same shape: `SoundClip` code `1` on `[0,1)` (takeoff), `2` on `[5,6)` (touchdown).

**`Timelines.cs`** — programmer declares the pairs the JSON names:

```csharp
public readonly record struct JumpClip(float Height);
public readonly record struct JumpTrack(float Scale) : IBlend<JumpClip>
{
    public void Blend(in JumpClip first, in JumpClip second, float factor, out JumpClip result)
        => result = new(first.Height + (second.Height - first.Height) * factor);
}
```

`IBlend` interpolates adjacent clips inside transition windows — return a real lerp.

**`Consumers.cs`** — programmers' jobs and attach bakes. The source generator registers both; there is no manual `Consume`/`BakeRuntime` call:

```csharp
public readonly struct MoveY : ITrack<JumpTrack, JumpClip>
{
    public static void Execute(in Frame<JumpTrack, JumpClip> frame, ref float y)
        => y += frame.Direction * frame.Clip.Height * frame.Track.Scale;
}

public readonly struct SpawnJumper : IBake<MoveY, World, SpawnSpec>
{
    public static void Bake(MoveY consumer, World world, SpawnSpec spec)
        => world.Create<JumpTl, SoundTl, Clock, JumpY, Sfx>(
            new(spec.Jump), new(spec.Sound), new(0), new(), new());
}
```

`Execute` runs at bind time to build the delta tables — playback is a table gather, so events travel as clip data on the effect column, not callbacks.

## Run

```sh
dotnet build -c Release
tlb jump.json jump.tlb --assembly bin/Release/net10.0/TlJump.dll
tlb sound.json sound.tlb --assembly bin/Release/net10.0/TlJump.dll
dotnet run -c Release --no-build
```

(`tlb` is the `Tl.Bake` dotnet tool — `dotnet tool install -g Tl.Bake --prerelease` once published, or `dotnet run --project <tl>/tools/Tl.Bake --` from a checkout.)

## The frame — most performant shape

Frent's `EnumerateChunks` hands you `Span<Component>` over archetype memory; single-field components cast straight into tl's columns:

```csharp
foreach (var (jumpTls, soundTls, clocks, jumps, sfxs) in
         world.Query<JumpTl, SoundTl, Clock, JumpY, Sfx>()
              .EnumerateChunks<JumpTl, SoundTl, Clock, JumpY, Sfx>())
{
    var jumpIds  = MemoryMarshal.Cast<JumpTl,  ushort>(jumpTls);
    var soundIds = MemoryMarshal.Cast<SoundTl, ushort>(soundTls);
    var clockCol = MemoryMarshal.Cast<Clock,   ushort>(clocks);
    var jumpCol  = MemoryMarshal.Cast<JumpY,   float>(jumps);
    var sfxCol   = MemoryMarshal.Cast<Sfx,     float>(sfxs);

    Timeline<JumpTrack,  JumpClip >.Apply(jumpIds,  clockCol, true, jumpCol);
    Timeline<SoundTrack, SoundClip>.Apply(soundIds, clockCol, true, sfxCol);
    Timeline.Step(jumpIds, clockCol, true);   // the only mutation — once per frame
}
```

- `Apply` is read-only on the clock column — N pair calls per frame, any order, no double-stepping.
- `Step` advances every clock once. Both assets share duration/looping, so the jump ids step the shared clock.
- A lane aggregates every pair in its asset — keep pairs in separate assets when they target separate columns (that's why `jump.json` and `sound.json` are two files).
- A system that owns its clock column with a single pair can fuse: `Timeline<JumpTrack, JumpClip>.Apply(ids, clocks, clocks, true, jumpCol)` — `next` may alias `positions` for in-place.

## Attaching — `IBake<TConsumer, ...TContext>`

`Timeline.Bake(id, ctx0, ctx1, ...)` walks the asset's pairs and runs every bake whose declared context types are satisfied — subset match, chain order, up to four contexts:

```csharp
Timeline.Bake(sound, world);          // runs SoundAttached — IBake<PlaySound, World>
Timeline.Bake(jump, world, spec);     // runs SpawnJumper — IBake<MoveY, World, SpawnSpec>
```

The bake is host-timed attachment: create entities, wire components, log — the warm path never sees it.

## Note on references

`Tl.Core` / `Tl.Gen.CSharp` / `Tl.Gen.Tlb` are project references because the `Apply`/`Step` surface is not yet published; on release they become `<PackageReference Include="Tl.CSharp" />` plus the `Tl.Gen.CSharp` analyzer package — the source stays identical.

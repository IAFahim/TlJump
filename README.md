# TlJump

A minimal tl × Frent consumer, split the way the library intends:

- **Designer** authors `jump.json` — one timeline, plain data naming the game's C# types.
- **`tlb`** bakes it into canonical `jump.tlb` bytes, checked against the built game assembly.
- **Programmer** writes the `ITrack`/`IBake` structs and the per-entity loop; `Tl.Gen.CSharp` wires consumers and bakes at compile time.

```text
jump!                            ← PlaySound dispatched live at position 0 (takeoff clip, code 1)
frame 0: y = 3
frame 1: y = 6
frame 2: y = 9                   ← arc peak
frame 3: y = 6
frame 4: y = 3
land!                            ← touchdown clip, code 2
frame 5: y = 0                   ← wraps, loops
```

## The split

**`jump.json`** — designer data, one timeline, two tracks. `arc` carries the jump (`rise` +3 for `[0,3)`, `fall` −3 for `[3,6)` — the loop returns to zero); `events` carries the sounds (`takeoff` code 1 at `[0,1)`, `touchdown` code 2 at `[5,6)`):

```json
{
  "name": "jump", "duration": 6, "loop": true,
  "tracks": [
    { "name": "arc", "namespace": "TlJump", "type": "JumpTrack", "data": { "Scale": 1.0 },
      "clips": [
        { "name": "rise", "type": "JumpClip", "start": 0, "end": 3, "data": { "Height": 3.0 } },
        { "name": "fall", "type": "JumpClip", "start": 3, "end": 6, "data": { "Height": -3.0 } } ] },
    { "name": "events", "namespace": "TlJump", "type": "SoundTrack", "data": {},
      "clips": [
        { "name": "takeoff",   "type": "SoundClip", "start": 0, "end": 1, "data": { "Code": 1 } },
        { "name": "touchdown", "type": "SoundClip", "start": 5, "end": 6, "data": { "Code": 2 } } ] }
  ]
}
```

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

**`Consumers.cs`** — the jobs, and here the consumer signature picks the lane (the tl README calls this "the two consumer shapes"):

```csharp
public readonly struct MoveY : ITrack<JumpTrack, JumpClip>
{
    // ref shape — measured once per (asset, pair) at first typed use, then frozen:
    public static void OnActive(in Frame<JumpTrack, JumpClip> frame, ref float y)
        => y += frame.Direction * frame.Clip.Height * frame.Track.Scale;
}

public readonly struct PlaySound : ITrack<SoundTrack, SoundClip>
{
    // dispatch shape — runs live, once per row per frame, never folded:
    public static void OnActive(in Frame<SoundTrack, SoundClip> frame)
    {
        if (frame.IsBackward) return;
        if (frame.Clip.Code == 1) Console.WriteLine("jump!");
        if (frame.Clip.Code == 2) Console.WriteLine("land!");
    }
}
```

- **`ref float` shape is measured, not executed.** At the first typed `Apply`/`Advance`/`View` of `(asset, pair)`, tl runs it once per tick in both directions over a scratch column, stores one float per tick and direction, and replays that table for every row forever. Keep it a pure function of `frame` (`Clip`, `Track`, `TimelineTick`, `Flags`); the `ref` starts from zero each probe, so accumulate — never read it. Side effects here fire `duration × 2` times at fold and never during playback.
- **No-`ref` shape is live dispatch.** `Apply(ids, positions, forward)` (no effects column) runs it once per row per frame — the place for audio cues, logs, and reads of live host state. It sees only the frame — no row or entity column — so entity-correlated work stays host-side.

**`IBake<TConsumer>`** — attach reactions, fired by `Timeline.Bake`. The `Bake` signature is the contract — by value, `in`, or `ref`, any types; a parameter typed exactly `TConsumer` binds `default` (consumers are static):

```csharp
public readonly struct AttachJump : IBake<MoveY>
{
    public static void Bake(in World world, ref Entity entity)
        => entity.Add(new JumpY());
}
```

One `Timeline.Bake(id, world, entity)` call walks **every** pair in the asset and fires each bake whose parameter types are covered by the arguments — subset match, chain order, up to four state arguments.

## The entity

`TimelineIndex` + `TimelinePosition` are the link — which interned timeline, and where in it — two `ushort`-sized record structs with real stored fields:

```csharp
ushort jump = TimelineAsset.Load(File.ReadAllBytes("jump.tlb"));

var entity = world.Create();
entity.Add(new TimelineIndex(jump));      // which timeline
entity.Add(new TimelinePosition(0));      // playback position
Timeline.Bake(jump, world, entity);       // fires AttachJump → adds JumpY
```

## The frame

`Apply` does the frame work — it gathers the measured delta at each row's position into the effect column, and dispatches live consumers — and `Advance` moves the position by the asset's own duration/looping. The archetype spans **are** tl's row columns — `MemoryMarshal.Cast` inside the generic overloads, no copy, no marshalling:

```csharp
foreach (var (ids, positions, jumps) in world
             .Query<TimelineIndex, TimelinePosition, JumpY>()
             .EnumerateChunks<TimelineIndex, TimelinePosition, JumpY>())
{
    Timeline<SoundTrack, SoundClip>.Apply(ids, positions, true);        // dispatch — live cues
    Timeline<JumpTrack, JumpClip>.Apply(ids, positions, true, jumps);   // measured gather
    Timeline<JumpTrack, JumpClip>.Advance(ids, positions, true);        // move the clock once
}
```

- `Apply` is read-only on the clock; `Advance` is the only mutation — once per row per frame, after every pair has applied.
- The measured lane aggregates every pair's `ref` consumer into one effect column per asset — tracks that must feed independent channels go in separate assets.
- `TimelineIndex`/`TimelinePosition` must be exactly 2 bytes of stored state: `record struct TimelineIndex(ushort Value);` works (positional parameters become stored properties); a plain `struct TimelineIndex(ushort value);` does **not** (primary-constructor parameters aren't fields — sizeof 1, and the span cast halves the row count; checked builds reject it).

## Run

Build runs a `BakeJumpTimeline` MSBuild target: it builds `Tl.Bake`, then bakes `jump.json` straight into `$(OutDir)jump.tlb` — so `dotnet run` or the IDE Run button works in any configuration:

```sh
dotnet run
```

`dotnet run -- --verify` plays the same loop silently except dispatch cues and asserts the golden `y` sequence frame by frame — non-zero exit on the first wrong frame, `ok` on success.

To bake by hand: `tlb jump.json jump.tlb --assembly bin/Debug/net10.0/TlJump.dll` (`tlb` is the `Tl.Bake` dotnet tool — `dotnet tool install -g Tl.Bake --prerelease` once published, or `dotnet run --project <tl>/tools/Tl.Bake --` from a checkout).

## Note on references

`Tl.Core` / `Tl.Gen.CSharp` / `Tl.Gen.Tlb` are project references because the `Apply`/`Advance` surface is not yet published; on release they become `<PackageReference Include="Tl.CSharp" />` plus the `Tl.Gen.CSharp` analyzer package — the source stays identical.

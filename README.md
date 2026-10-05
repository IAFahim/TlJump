# TlJump

A minimal tl × Frent consumer, split the way the library intends:

- **Designer** authors `jump.json` — one timeline, plain data naming the game's C# types.
- **`tlb`** bakes it into canonical `jump.tlb` bytes, checked against the built game assembly.
- **Programmer** writes the `ITrack`/`IBake` structs and the per-entity loop; `Tl.Gen.CSharp` wires consumers and bakes at compile time.

```text
6                                ← MoveY.ExecuteActive wrote JumpY (arc 3 × JumpPower 2)
jump!                            ← PlaySound dispatched live at position 0 (takeoff clip, code 1)
12                               ← frame 1
18                               ← frame 2, arc peak
12                               ← frame 3
6                                ← frame 4
0                                ← frame 5
land!                            ← touchdown clip, code 2 — then the loop wraps and repeats
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

**`Consumers.cs`** — the jobs. A consumer declares `Fold` and/or `ExecuteActive`, and the signatures pick the contract:

```csharp
public readonly struct MoveY : ITrack<JumpTrack, JumpClip>
{
    // Fold — measured once per (asset, pair) at first typed use, then frozen per tick:
    public static void Fold(in Frame<JumpTrack, JumpClip> frame, out float arc)
        => arc = frame.Direction * frame.Clip.Height * frame.Track.Scale;

    // ExecuteActive — runs live per row; the 'in float arc' feed binds the Fold
    // result by type, 'ref'/'in' params bind caller columns by type:
    public static void ExecuteActive(in float arc, ref JumpY y, in JumpPower power)
        => y.Value += arc * power.Value;
}

public readonly struct PlaySound : ITrack<SoundTrack, SoundClip>
{
    // dispatch-only shape — frame only, no columns; runs live once per row per frame:
    public static void ExecuteActive(in Frame<SoundTrack, SoundClip> frame)
    {
        if (frame.IsBackward) return;
        if (frame.Clip.Code == 1) Console.WriteLine("jump!");
        if (frame.Clip.Code == 2) Console.WriteLine("land!");
    }
}
```

- **`Fold` is measured, not executed.** At the first typed `Apply`/`Advance`/`View` of `(asset, pair)`, tl runs it once per tick in both directions, stores the `out` results per tick and direction, and replays that table for every row forever. Keep it a pure function of `frame` (`Clip`, `Track`, `TimelineTick`, `Flags`) — side effects fire `duration × 2` times at fold and never during playback.
- **`ExecuteActive` is the live half.** Leading `in` parameters whose types match the `Fold` results are memo feeds — the runtime injects the folded value for that row's position; remaining `in`/`ref` parameters are gameplay columns the caller supplies through a `ColumnSet`, bound by `TypeKey` (two same-type columns can't be distinguished — give them distinct types). `ExecuteActive(in frame)` with no columns is dispatch-only — the place for audio cues, logs, and reads of live host state; it sees only the frame, so entity-correlated work stays host-side.

**`IBake<TConsumer>`** — attach reactions, fired by `Timeline.Bake`. The `Bake` signature is the contract — by value, `in`, or `ref`, any types; a parameter typed exactly `TConsumer` binds `default` (consumers are static):

```csharp
public readonly struct AttachJump : IBake<MoveY>
{
    public static void Bake(ref Span<Entity> entities)
    {
        foreach (ref var entity in entities) entity.Add(new JumpY());
    }
}
```

One `Timeline.Bake(id, args...)` call walks **every** pair in the asset and fires each bake whose parameter types are covered by the arguments — subset match, chain order, up to four state arguments.

## The entity

`TimelineIndex` + `TimelinePosition` are the link — which interned timeline, and where in it — two `ushort`-sized record structs with real stored fields:

```csharp
ushort jump = TimelineAsset.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "jump.tlb")));

Span<Entity> spawns =
[
    world.Create(new TimelineIndex(jump), new TimelinePosition(0), new JumpPower { Value = 2 }),
];
Timeline.Bake(jump, spawns);              // fires AttachJump → adds JumpY
```

## The frame

`Apply` does the frame work — it injects the folded values at each row's position into the consumers' memo feeds and runs `ExecuteActive` over the caller's columns — and `Advance` moves the position by the asset's own duration/looping. The archetype spans **are** tl's row columns — `MemoryMarshal.Cast` inside the generic overloads, no copy, no marshalling. Columns ride in a `ColumnSet` the caller assembles per chunk:

```csharp
foreach (var (ids, positions, y, powers) in world
             .Query<TimelineIndex, TimelinePosition, JumpY, JumpPower>()
             .EnumerateChunks<TimelineIndex, TimelinePosition, JumpY, JumpPower>())
{
    if (ids.Length == 0) continue;
    var set = new ColumnSet();
    set.Add(y);                                   // ref column — bound to 'ref JumpY'
    set.Add(powers);                              // in column — bound to 'in JumpPower'
    Timeline<JumpTrack, JumpClip>.Apply(ids, positions, true, in set);
    Timeline<SoundTrack, SoundClip>.Apply(ids, positions, true);  // dispatch — live cues
    Timeline<JumpTrack, JumpClip>.Advance(ids, positions, true);  // move the clock once
}
```

- `Apply` is read-only on the clock; `Advance` is the only mutation — once per row per frame, after every pair has applied.
- `ColumnSet.Add(ReadOnlySpan<T>)` binds by `TypeKey` — the runtime routes each column to the `in`/`ref` parameter of matching type; the set must cover every gameplay column the pair's consumers declare (memo feeds come from the fold tables, not the set).
- A single effect column can skip the set: `Apply(ids, positions, forward, Span<float> fx)` applies a frozen fold lane directly; `ApplyChunk` covers two typed lanes.
- `TimelineIndex`/`TimelinePosition` must be exactly 2 bytes of stored state: `record struct TimelineIndex(ushort Value);` works (positional parameters become stored properties); a plain `struct TimelineIndex(ushort value);` does **not** (primary-constructor parameters aren't fields — sizeof 1, and the span cast halves the row count; checked builds reject it).

## Run

Build runs a `BakeJumpTimeline` MSBuild target: it builds `Tl.Bake`, then bakes `jump.json` straight into `$(OutDir)jump.tlb` — so `dotnet run` or the IDE Run button works in any configuration:

```sh
dotnet run
```

To bake by hand: `tlb jump.json jump.tlb --assembly bin/Debug/net10.0/TlJump.dll` (`tlb` is the `Tl.Bake` dotnet tool — `dotnet tool install -g Tl.Bake --prerelease` once published, or `dotnet run --project <tl>/tools/Tl.Bake --` from a checkout).

## Note on references

`Tl.Core` / `Tl.Gen.CSharp` / `Tl.Gen.Tlb` are project references because the `Apply`/`Advance` surface is not yet published; on release they become `<PackageReference Include="Tl.CSharp" />` plus the `Tl.Gen.CSharp` analyzer package — the source stays identical.

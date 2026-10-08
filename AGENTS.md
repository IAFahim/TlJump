# AGENTS

TlJump is a minimal consumer of `tl` (the timeline library) hosted in Frent, showing the intended designer→programmer pipeline: JSON-authored timelines baked by `tlb`, `ITrack`/`IBake` structs wired by the `Tl.Gen.CSharp` analyzer, playback through `Timeline<TTrack, TClip>.Apply` + `Timeline.Advance`.

## Layout

| file | role |
|---|---|
| `jump.json` | designer data — one timeline (`name`, `duration`, `loop`), `tracks` name the game's C# namespace/type, `clips` name type + half-open `[start, end)` windows + `data` matching the struct fields |
| `Timelines.cs` | programmer pairs — `JumpTrack`/`JumpClip`, `SoundTrack`/`SoundClip` record structs; tracks implement `IBlend<TClip>` |
| `Consumers.cs` | `ITrack<TTrack, TClip>` `Fold`/`ExecuteActive` jobs (folded + live/dispatch shapes) + `IBake<TConsumer>` attach bakes + plain components (`JumpY`, `JumpPower`, `Sfx`) |
| `Program.cs` | load, spawn, frame loop |
| `jump.tlb` | baked bytes — produced by `tlb`, regenerated on rebuild |

## Build, bake, run

```sh
dotnet run
```

`BakeJumpTimeline` (AfterTargets=Build, incremental on `jump.json`+assembly) builds `Tl.Bake` and bakes `jump.json` into `$(OutDir)jump.tlb` — a plain build or IDE Run leaves the asset next to the executable.

The csproj project-references `$(TlRoot)` (default `../tl`; override with `-p:TlRoot=...`) for `Tl.Core`, `Tl.Gen.Tlb`, `Tl.Gen.CSharp` as an analyzer + its `.targets` import, because the `Apply`/`Advance` surface is not yet on NuGet; it becomes `PackageReference` packages on release.

## Authoring a timeline

One JSON file per timeline. `duration` ticks, `loop` wraps position. Each track holds `data` for its track struct and `clips` — half-open windows `[start, end)` carrying clip data in authored order. Type names resolve against the `--assembly`; `tlb` validates types, namespaces, and data field names and emits deterministic TLB1 bytes.

## Programmer side

```csharp
public readonly struct MoveY : ITrack<JumpTrack, JumpClip>
{
    public static void Fold(in Frame<JumpTrack, JumpClip> frame, out float arc)
        => arc = frame.Direction * frame.Clip.Height * frame.Track.Scale;

    public static void ExecuteActive(in float arc, ref JumpY y, in JumpPower power)
        => y.Value += arc * power.Value;
}
```

- A consumer declares `Fold` and/or `ExecuteActive`; signatures pick the contract. `frame` exposes `Clip`, `Track`, `TimelineTick`, `Direction`, `IsBackward`, `Has(FrameFlags.X)`, `WithinClip`, `ClipLength`, `TrackIndex`, `Flags`.
- `Fold(in frame, out T result)` is **measured once** at first typed use — tl runs it per tick in both directions and freezes each `out` result (unmanaged, ≤8 bytes) per `(asset, pair)`. Keep it a pure function of the frame; side effects fire at fold and never during playback.
- `ExecuteActive` is the **live** half: leading `in` params typed like the `Fold` outputs are memo feeds injected by the runtime (bound in declaration order); the remaining `in`/`ref` unmanaged params are gameplay columns the caller supplies via `ColumnSet`, bound by `TypeKey` — two same-type columns of the same mode are a TLGEN81 error, so give them distinct component types. `ExecuteActive(in frame)` with no columns is dispatch-only: `Apply(ids, positions, forward)` runs it per row per frame; `PlaySound` is this shape (guarded by `frame.IsBackward` so rewind replays stay silent).
- `IBlend<TClip>.Blend(first, second, factor, out result)` interpolates adjacent clips across transition windows — implement a real lerp (`a + (b-a)*factor`).
- `IBake<TConsumer>` declares attach bakes; the marker is arity-1 and the `Bake` method's parameter list is the whole contract — by value, `in`, or `ref`, any types, and a parameter whose type is exactly the consumer type binds `default(TConsumer)` (consumers are static). `Timeline.Bake(id, args...)` walks every pair in the asset and fires each bake whose parameter types are covered by the args (chain order, cap 4). The pattern here: the entity owner adds `TimelineIndex` + `TimelinePosition` + `JumpPower` directly; each pair's bake adds the component that pair writes into (`AttachJump`→`JumpY`, `AttachSound`→`Sfx`).

## Runtime shape

```csharp
ushort jump = TimelineAsset.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "jump.tlb")));
using var world = new World();

Span<Entity> spawns =
[
    world.Create(new TimelineIndex(jump), new TimelinePosition(0), new JumpPower { Value = 2 }),
];
Timeline.Bake(jump, spawns);              // fires the pairs' bakes → adds JumpY + Sfx

foreach (var (ids, positions, y, powers) in world
             .Query<TimelineIndex, TimelinePosition, JumpY, JumpPower>()
             .EnumerateChunks<TimelineIndex, TimelinePosition, JumpY, JumpPower>())
{
    if (ids.Length == 0) continue;
    var set = new ColumnSet();
    set.Add(y);                                                     // 'ref JumpY'
    set.Add(powers);                                                // 'in JumpPower'
    Timeline<JumpTrack, JumpClip>.Apply(ids, positions, true, in set);   // live columns + memo feed
    Timeline<SoundTrack, SoundClip>.Apply(ids, positions, true);         // dispatch — live cues
    Timeline<JumpTrack, JumpClip>.Advance(ids, positions, true);         // move the clock once
}
```

- `Timeline<JumpTrack, JumpClip>.Apply(ids, positions, true, in set)` (from `Tl.Core`) is the live half: it casts the component spans into tl's row columns (`MemoryMarshal.Cast`), injects each row's folded `arc` into the `in float` memo feed, and runs `ExecuteActive` over the `ColumnSet` columns — pure over `positions`, never moves the clock. `Timeline<SoundTrack, SoundClip>.Apply(ids, positions, true)` (no columns) is the dispatch half: it runs the sound pair's dispatch-only `ExecuteActive` once per row. `Advance` moves the clock once per frame, after every pair has applied. Generic over structs, so the JIT specializes per instantiation. Per-entity overloads also exist: `Apply(in index, in position, forward, ...)` and `Advance(in index, ref position, forward)`.
- `ColumnSet.Add(ReadOnlySpan<T>)` binds a column by `TypeKey`; the set must cover every gameplay column the pair's consumers declare. Frozen fold lanes can bypass `ExecuteActive` entirely: `Apply(ids, positions, forward, Span<float> fx)` gathers one lane, `ApplyChunk` gathers two typed lanes.
- Under the hood the archetype spans ARE tl's row columns — no per-row loop, no copy, no managed row buffers.
- `TimelineIndex`/`TimelinePosition` must have real storage of exactly 2 bytes: `record struct TimelineIndex(ushort Value);` (positional record parameters become stored properties — sizeof 2). The tempting `struct TimelineIndex(ushort value);` is a trap: plain-struct primary-constructor parameters are NOT fields, the struct has no instance state (sizeof 1), `MemoryMarshal.Cast` then halves the span length — a contract violation tl rejects in checked builds (`Column length must equal position count`); in a Release package it silently plays zeros. Plain structs with public fields also work (and are the only form that binds to `ref`/`in` single-element spans).
- Empty archetypes the entity migrated through still match the query shape — `Apply`/`Advance` handle zero-length spans, but guard any direct indexing.
- Each `Timeline<TTrack, TClip>` plays only its own pair — folding the jump pair never runs the sound pair's consumers, and a pair's fold lanes never carry another pair's contribution. Tracks that must feed independent channels live in separate pairs.
- Frent's `EnumerateChunks<...>()` yields `Span<Component>` over archetype storage; entities sharing an archetype share one contiguous chunk.
- `Entity.Add<T>(in T)` migrates the entity's archetype — valid for bake-time attachment.

## Verifying changes

Rebuild → `dotnet run` plays the demo: `jump!` fires when the position sits on the takeoff tick and `land!` on the touchdown tick (dispatch cues are live), `y` reads `6, 12, 18, 12, 6, 0` (folded `arc` × `JumpPower 2`) looping over 14 frames. There is no `--verify` harness — verification is reading the printed sequence.

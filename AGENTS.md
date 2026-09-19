# AGENTS

TlJump is a minimal consumer of `tl` (the timeline library) hosted in Frent, showing the intended designer→programmer pipeline: JSON-authored timelines baked by `tlb`, `ITrack`/`IBake` structs wired by the `Tl.Gen.CSharp` analyzer, playback through `Timeline<TTrack, TClip>.Apply` + `Timeline.Step`.

## Layout

| file | role |
|---|---|
| `jump.json` | designer data — one timeline (`name`, `duration`, `loop`), `tracks` name the game's C# namespace/type, `clips` name type + half-open `[start, end)` windows + `data` matching the struct fields |
| `Timelines.cs` | programmer pairs — `JumpTrack`/`JumpClip`, `SoundTrack`/`SoundClip` record structs; tracks implement `IBlend<TClip>` |
| `Consumers.cs` | `ITrack<TTrack, TClip>.Execute` jobs + `IBake<TConsumer, ...TContext>` attach bakes + plain components (`JumpY`, `Sfx`) |
| `Program.cs` | load, spawn, frame loop |
| `jump.tlb` | baked bytes — produced by `tlb`, regenerated on rebuild |

## Build, bake, run

```sh
dotnet run
```

`BakeJumpTimeline` (AfterTargets=Build, incremental on `jump.json`+assembly) builds `Tl.Bake` and bakes `jump.json` into `$(OutDir)jump.tlb` — a plain build or IDE Run leaves the asset next to the executable.

The csproj project-references `../tl-237-step-apply` (`Tl.Core`, `Tl.Gen.Tlb`, `Tl.Gen.CSharp` as an analyzer + its `.targets` import) because the `Apply`/`Step` surface is not yet on NuGet; it becomes `PackageReference` packages on release.

## Authoring a timeline

One JSON file per timeline. `duration` ticks, `loop` wraps position. Each track holds `data` for its track struct and `clips` — half-open windows `[start, end)` carrying clip data in authored order. Type names resolve against the `--assembly`; `tlb` validates types, namespaces, and data field names and emits deterministic TLB1 bytes.

## Programmer side

```csharp
public readonly struct MoveY : ITrack<JumpTrack, JumpClip>
{
    public static void Execute(in Frame<JumpTrack, JumpClip> frame, ref float y)
        => y += frame.Direction * frame.Clip.Height * frame.Track.Scale;
}
```

- `Execute` is the pair's job: `frame` exposes `Clip`, `Track`, `TimelineTick`, `Direction`, `IsBackward`, `Has(FrameFlags.X)`, `WithinClip`, `ClipLength`. It runs once per position at bind to build delta tables — playback is a pure gather, so keep `Execute` pure in `ref` output; side-effects fire at bind. Bind measures every asset forward **and** backward (rewind lanes), so side-effecting consumers guard with `if (frame.IsBackward) return;` or they fire twice.
- `IBlend<TClip>.Blend(first, second, factor, out result)` interpolates adjacent clips across transition windows — implement a real lerp (`a + (b-a)*factor`).
- `IBake<TConsumer, T0..T3>` declares attach bakes; `Timeline.Bake(id, ctx...)` walks every pair in the asset and fires each bake whose context types are a subset of the args (chain order, cap 4). The pattern here: the entity owner adds `TimelineIndex` + `TimelinePosition` directly; each pair's bake adds the component that pair writes into (`AttachJump`→`JumpY`, `AttachSound`→`Sfx`).

## Runtime shape

```csharp
ushort jump = TimelineAsset.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "jump.tlb")));
using var world = new World();

var entity = world.Create();
entity.Add(new TimelineIndex(jump));      // which timeline (interned ushort — no pinned ref)
entity.Add(new TimelinePosition(0));      // playback position
Timeline.Bake(jump, world, entity);       // fires per-pair bakes → adds JumpY + Sfx

foreach (var (ids, positions, jumps) in world.Query<TimelineIndex, TimelinePosition, JumpY>()
                 .EnumerateChunks<TimelineIndex, TimelinePosition, JumpY>())
{
    Timeline<JumpTrack, JumpClip>.Apply(
        MemoryMarshal.Cast<TimelineIndex, ushort>(ids),
        MemoryMarshal.Cast<TimelinePosition, ushort>(positions),
        true,
        MemoryMarshal.Cast<JumpY, float>(jumps));
    Timeline.Step(
        MemoryMarshal.Cast<TimelineIndex, ushort>(ids),
        MemoryMarshal.Cast<TimelinePosition, ushort>(positions),
        true);
}
```

- `Apply` is read-only on positions and gathers the measured delta at `Position` into the effect span. `Step` advances `Position` once — apply then move, once per chunk per frame.
- The archetype spans ARE tl's row columns: `MemoryMarshal.Cast` reinterprets each component span in place (`TimelineIndex→ushort`, `TimelinePosition→ushort`, `JumpY→float`) and the whole chunk goes through one `Apply` + one `Step` — no per-row loop, no copy, no managed row buffers. This requires the components to be single-field blittable structs with public fields.
- `TimelineIndex`/`TimelinePosition` must be plain structs with public fields — primary-constructor parameters are not fields, so `struct S(int v);` has no `.Value` at all, and auto-properties cannot bind to `ref`/`in`.
- Empty archetypes the entity migrated through still match the query shape — `Apply`/`Step` handle zero-length spans, but guard any direct indexing.
- `Apply` measures the whole asset: every pair's `Execute` sums into one lane per asset. Tracks that must feed independent channels go in separate assets.
- Frent's `EnumerateChunks<...>()` yields `Span<Component>` over archetype storage; entities sharing an archetype share one contiguous chunk.
- `Entity.Add<T>(in T)` migrates the entity's archetype — valid for bake-time attachment.

## Verifying changes

Rebuild → run. Expected output: `jump!`/`land!` at bind (sound execs during measure), then `y = 3, 6, 9, 6, 3, 0` looping.

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
dotnet build -c Release
dotnet run --project ../tl-237-step-apply/tools/Tl.Bake -- \
    jump.json jump.tlb --assembly bin/Release/net10.0/TlJump.dll
dotnet run -c Release --no-build
```

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

- `Execute` is the pair's job: `frame` exposes `Clip`, `Track`, `TimelineTick`, `Direction`, `Has(FrameFlags.X)`, `WithinClip`, `ClipLength`. It runs once per position at bind to build delta tables — playback is a pure gather, so keep `Execute` pure in `ref` output; side-effects fire at bind.
- `IBlend<TClip>.Blend(first, second, factor, out result)` interpolates adjacent clips across transition windows — implement a real lerp (`a + (b-a)*factor`).
- `IBake<TConsumer, T0..T3>` declares attach bakes; `Timeline.Bake(id, ctx...)` walks every pair in the asset and fires each bake whose context types are a subset of the args (chain order, cap 4). The pattern here: the entity owner adds `TimelineComponent` directly; each pair's bake adds the component that pair writes into (`AttachJump`→`JumpY`, `AttachSound`→`Sfx`).

## Runtime shape

```csharp
ushort jump = TimelineAsset.Load(File.ReadAllBytes("jump.tlb"));
using var asset = TimelineAsset.Of(jump);            // pins the index so Reference stays valid

var entity = world.Create();
entity.Add(new TimelineComponent(asset.Reference));  // { Reference, Position }
Timeline.Bake(jump, world, entity);                  // fires per-pair bakes

foreach (var (_, c, y) in world.Query<TimelineComponent, JumpY>()
                              .EnumerateWithEntities<TimelineComponent, JumpY>())
{
    ref var t = ref c.Value;
    Timeline<JumpTrack, JumpClip>.Apply(jump,
        new ReadOnlySpan<ushort>(in t.Position), true, new Span<float>(ref y.Value.Value));
    Timeline.Step(jump, new Span<ushort>(ref t.Position), true);
}
```

- `Apply` is read-only on positions and gathers the measured delta at `Position` into the effect span. `Step` advances `Position` once — apply then move, once per entity per frame.
- Single-element spans come from `new Span<T>(ref field)` / `new ReadOnlySpan<T>(in field)` — no marshalling.
- `Apply` measures the whole asset: every pair's `Execute` sums into one lane per asset. Tracks that must feed independent channels go in separate assets.
- Frent's `EnumerateChunks<...>()` yields `Span<Component>` over archetype storage for the bulk path; `Ref<T>.Value` gives `ref T` for the per-entity path.
- `Entity.Add<T>(in T)` migrates the entity's archetype — valid for bake-time attachment.

## Verifying changes

Rebuild → rebake `jump.tlb` → run. Expected output: `jump!`/`land!` at bind (sound execs during measure), then `y = 3, 6, 9, 6, 3, 0` looping.

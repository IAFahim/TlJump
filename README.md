# TlJump

Smallest tl × Frent consumer: 4 entities on a 6-tick looping jump arc plus a sound-event timeline, all sharing one clock column.

```text
sound timeline attached            ← IBake<ApplySound, World>
  entity 0..3: jump!               ← SoundClip code 1 at tick 0
frame 0: y = 4 4 4 4
frame 1: y = 7 7 7 7
frame 2: y = 8 8 8 8
frame 3: y = 7 7 7 7
frame 4: y = 4 4 4 4
  entity 0..3: land!               ← SoundClip code 2 at tick 5
frame 5: y = 0 0 0 0               ← wraps, loops
```

## Consuming

```xml
<PackageReference Include="Frent" Version="0.8.0-beta" />
```

`Tl.Core` is a project reference here because the `Apply`/`Step` surface is not yet on NuGet — it becomes `<PackageReference Include="Tl.CSharp" />` once published. `DomainBaker.cs` is linked from the tl repo's test helpers so the sample can author assets without `tlb`; a real project bakes `.tl` files at build time.

## The frame

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
    Timeline.Step(jumpIds, clockCol, true);   // once per frame
}
```

- Frent `EnumerateChunks` yields `Span<Component>` over archetype memory; single-field component structs cast to tl's columns via `MemoryMarshal.Cast` — zero copies, zero allocation in the warm loop.
- `Apply` is read-only on the clock column — any number of pair calls per frame, in any order.
- `Step` is the only mutation: it advances every clock once. Two `Step`s on one column in a frame = double speed.
- `Step` needs one id column to pick the motion profile; both assets share duration/looping so the jump ids step the shared clock.
- If a system owns its clock column and uses a single pair, fuse apply+step: `Timeline<JumpTrack, JumpClip>.Apply(ids, clocks, clocks, true, jumpCol)` — `next` may alias `positions` for in-place.

## Authoring

- `Track<TTrack, TClip>(value)` adds a timeline pair; `Clip(track, start, end, value)` covers `[start, end)` ticks.
- `IBlend<TClip>.Blend(first, second, factor)` interpolates adjacent clips inside a transition window — return `a + (b - a) * factor`; a stub returning `first` makes every later clip invisible.
- `TimelineAsset.Load` returns a `ushort` index — the identity the entity components store.

## Consumers — `ITrack<TTrack, TClip>`

```csharp
public readonly struct ApplyJump : ITrack<JumpTrack, JumpClip>
{
    public static void Execute(in Frame<JumpTrack, JumpClip> frame, ref float y)
        => y += frame.Clip.Height;
}
```

`Execute` runs at bind time, once per position, to build the delta table `Apply` gathers — so it never sees per-entity state; events travel as clip data on the effect column (the `Sfx` column here carries code 1 = jump, 2 = land). The wiring a source generator would emit is hand-written in `Install.Wire` — `PairRuntime.Consume` maps the pair's exec function pointer, `Bind` maps `TypeKey<float>` to effect column 0.

## Attaching — `IBake<TConsumer, ...TContext>`

```csharp
public readonly struct SpawnJumper : IBake<ApplyJump, World, SpawnSpec>
{
    public static void Bake(ApplyJump consumer, World world, SpawnSpec spec)
        => world.Create<JumpTl, SoundTl, Clock, JumpY, Sfx>(
            new(spec.Jump), new(spec.Sound), new(0), new(), new());
}
```

`Timeline.Bake(id, ctx0, ctx1, ...)` walks the asset's pairs and runs every registered bake whose declared context types are satisfied by the args — subset match, chain order. `SpawnJumper` declares `(World, SpawnSpec)` so `Timeline.Bake(jump, world, spec)` spawns an entity per call; `SoundAttached` declares `(World)` so `Timeline.Bake(sound, world)` runs it. Contexts are keyed by `TypeKey<T>` — up to four per bake.

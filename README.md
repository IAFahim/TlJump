# TlJump

A minimal tl × Frent consumer, split the way the library intends:

- **Designer** authors `jump.json` — one timeline, plain data naming the game's C# types.
- **`tlb`** bakes it into canonical `jump.tlb` bytes, checked against the built game assembly.
- **Programmer** writes the `ITrack`/`IBake` structs and the per-entity loop; `Tl.Gen.CSharp` wires consumers and bakes at compile time.

```text
  tick 0  y = 3      jump!       ← MoveY.Execute + PlaySound.Execute per entity
  tick 1  y = 6
  tick 2  y = 9                  ← arc peak
  tick 3  y = 6
  tick 4  y = 3
  tick 5  y = 0      land!       ← wraps, loops
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
        { "name": "takeoff",   "type": "SoundClip", "start": 0, "end": 1, "data": { "Code": 1.0 } },
        { "name": "touchdown", "type": "SoundClip", "start": 5, "end": 6, "data": { "Code": 2.0 } } ] }
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

**`Consumers.cs`** — the jobs. `ITrack<TTrack, TClip>.Execute(in frame, ref effect)` is where per-entity work happens — the frame carries `Clip`, `Track`, `TimelineTick`, `Direction`, and flags, so the consumer decides what to do from authored data:

```csharp
public readonly struct MoveY : ITrack<JumpTrack, JumpClip>
{
    public static void Execute(in Frame<JumpTrack, JumpClip> frame, ref float y)
        => y += frame.Direction * frame.Clip.Height * frame.Track.Scale;
}

public readonly struct PlaySound : ITrack<SoundTrack, SoundClip>
{
    public static void Execute(in Frame<SoundTrack, SoundClip> frame, ref float channel)
    {
        channel = frame.Clip.Code;
        if (frame.Clip.Code == 1) Console.WriteLine("jump!");
        if (frame.Clip.Code == 2) Console.WriteLine("land!");
    }
}
```

**`IBake<TConsumer, ...TContext>`** — attach reactions, fired by `Timeline.Bake`. The timeline component is attached by whoever owns the entity; each pair's bake adds the component that pair writes into:

```csharp
public readonly struct AttachJump : IBake<MoveY, World, Entity>
{
    public static void Bake(MoveY consumer, World world, Entity entity)
        => entity.Add(new JumpY());
}

public readonly struct AttachSound : IBake<PlaySound, World, Entity>
{
    public static void Bake(PlaySound consumer, World world, Entity entity)
        => entity.Add(new Sfx());
}
```

One `Timeline.Bake(id, world, entity)` call walks **every** pair in the asset — `AttachJump` fires for the arc pair, `AttachSound` for the events pair — subset match, chain order, up to four contexts.

## The entity — the coordinator model

`TimelineComponent` is the link: `{ Reference, Position }` — reference to the baked asset plus the coordinator-owned tick. Spawn:

```csharp
var entity = world.Create();
entity.Add(new TimelineComponent(jump.Reference));  // the timeline link, attached directly
Timeline.Bake(jump.Index, world, entity);           // bakes attach JumpY + Sfx
```

## The frame

`Timeline.Query<TTrack, TClip>(in component)` is a read-only view of that pair's frames at the entity's current position — iterate it and hand each frame to the consumer's `Execute`. The coordinator moves `Position` once per entity per frame via `Timeline.Step` — the only mutation:

```csharp
foreach (var (entity, tl, y, sfx) in
         world.Query<TimelineComponent, JumpY, Sfx>()
              .EnumerateWithEntities<TimelineComponent, JumpY, Sfx>())
{
    ref var c = ref tl.Value;
    foreach (var jumpFrame in Timeline.Query<JumpTrack, JumpClip>(in c))
        MoveY.Execute(in jumpFrame, ref y.Value.Value);
    foreach (var soundFrame in Timeline.Query<SoundTrack, SoundClip>(in c))
        PlaySound.Execute(in soundFrame, ref sfx.Value.Value);
    Timeline.Step(jump.Index, MemoryMarshal.CreateSpan(ref c.Position, 1), true);
}
```

- `Query` reads at the current position; `Step` moves it by the asset's own duration/looping — apply then move, once.
- No column arrays, no per-entity ids — `TimelineComponent` holds the reference; the position is a field on it.
- Pairs share one position: the same component feeds both queries — no double-stepping, ever.

## Run

```sh
dotnet build -c Release
tlb jump.json jump.tlb --assembly bin/Release/net10.0/TlJump.dll
dotnet run -c Release --no-build
```

(`tlb` is the `Tl.Bake` dotnet tool — `dotnet tool install -g Tl.Bake --prerelease` once published, or `dotnet run --project <tl>/tools/Tl.Bake --` from a checkout.)

## Bulk path

When the same timeline type runs over thousands of entities, the column API is the throughput path: `Timeline<T,C>.Apply(ids, positions, effects)` gathers deltas over shared caller-owned columns, `Timeline.Step(ids, positions)` moves them once. Frent chunk spans feed it directly via `EnumerateChunks`. The per-entity `Query`/`Execute` model here is the flexible one — arbitrary consumers, events, mixed pairs — the column model is the SIMD one.

## Note on references

`Tl.Core` / `Tl.Gen.CSharp` / `Tl.Gen.Tlb` are project references because the `Apply`/`Step` surface is not yet published; on release they become `<PackageReference Include="Tl.CSharp" />` plus the `Tl.Gen.CSharp` analyzer package — the source stays identical.

# TlJump

A minimal tl × Frent consumer, split the way the library intends:

- **Designer** authors `jump.json` — one timeline, plain data naming the game's C# types.
- **`tlb`** bakes it into canonical `jump.tlb` bytes, checked against the built game assembly.
- **Programmer** writes the `ITrack`/`IBake` structs and the per-entity loop; `Tl.Gen.CSharp` wires consumers and bakes at compile time.

```text
jump!                            ← PlaySound.Execute ran at bind (takeoff clip, code 1)
land!                            ← touchdown clip, code 2
frame 0: y = 3
frame 1: y = 6
frame 2: y = 9                   ← arc peak
frame 3: y = 6
frame 4: y = 3
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

**`Consumers.cs`** — the jobs. `ITrack<TTrack, TClip>.Execute(in frame, ref effect)` is the pair's job — the frame carries `Clip`, `Track`, `TimelineTick`, `Direction`, and flags, so the consumer works from authored data:

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
        if (frame.Clip.Code == 1) Console.WriteLine("jump!");
        if (frame.Clip.Code == 2) Console.WriteLine("land!");
    }
}
```

`Execute` runs once per position at bind — the runtime measures every pair's contribution into per-tick delta tables, so `Apply` playback is a pure gather. `PlaySound` writes nothing to the channel; its contribution to the lane is zero, which keeps the measured arc clean — the events are pure side-effects decided by `frame.Clip`.

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

## The entity

`TimelineComponent` is the link: `{ Reference, Position }` — reference to the baked asset plus the coordinator-owned tick. Spawn:

```csharp
ushort jump = TimelineAsset.Load(File.ReadAllBytes("jump.tlb"));
using var asset = TimelineAsset.Of(jump);          // keeps the reference alive

var entity = world.Create();
entity.Add(new TimelineComponent(asset.Reference)); // the timeline link, attached directly
Timeline.Bake(jump, world, entity);                 // bakes attach JumpY + Sfx
```

## The frame

`Apply` does the frame work — it gathers the measured delta at the entity's `Position` into the component field; `Step` moves the position by the asset's own duration/looping. Both take spans — `new Span<T>(ref x)` views one component field as a column, so no marshalling:

```csharp
foreach (var (_, c, y) in
         world.Query<TimelineComponent, JumpY>()
              .EnumerateWithEntities<TimelineComponent, JumpY>())
{
    ref var t = ref c.Value;
    Timeline<JumpTrack, JumpClip>.Apply(
        jump, new ReadOnlySpan<ushort>(in t.Position), true, new Span<float>(ref y.Value.Value));
    Timeline.Step(jump, new Span<ushort>(ref t.Position), true);
}
```

- `Apply` is read-only on `Position`; `Step` is the only mutation — once per entity per frame.
- The asset lane aggregates every pair's measured contribution — tracks in one asset feed one channel; keep channels in separate assets when they must stay independent.
- The same calls work over `EnumerateChunks` spans for bulk: cast `Span<Clock>` to `Span<ushort>` and the whole chunk applies in one gather — that's the throughput path.

## Run

```sh
dotnet build -c Release
tlb jump.json jump.tlb --assembly bin/Release/net10.0/TlJump.dll
dotnet run -c Release --no-build
```

(`tlb` is the `Tl.Bake` dotnet tool — `dotnet tool install -g Tl.Bake --prerelease` once published, or `dotnet run --project <tl>/tools/Tl.Bake --` from a checkout.)

## Note on references

`Tl.Core` / `Tl.Gen.CSharp` / `Tl.Gen.Tlb` are project references because the `Apply`/`Step` surface is not yet published; on release they become `<PackageReference Include="Tl.CSharp" />` plus the `Tl.Gen.CSharp` analyzer package — the source stays identical.

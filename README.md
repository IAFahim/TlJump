# TlJump

A minimal tl × Frent consumer, split the way the library intends:

- **Designer** authors `jump.json` — one timeline, plain data naming the game's C# types.
- **`tlb`** bakes it into canonical `jump.tlb` bytes, checked against the built game assembly.
- **Programmer** writes the `ITrack`/`IBake` structs and the per-entity loop; `Tl.Gen.CSharp` wires consumers and bakes at compile time.

```text
jump!                            ← frame 0: takeoff clip (code 1), position enters its window
land!                            ← frame 5: touchdown clip (code 2)
jump!                            ← frame 6: loop wrapped to the takeoff tick
land!                            ← frame 11
jump!                            ← frame 12
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

**`Consumers.cs`** — the jobs. `ITrack<TTrack, TClip>.OnActive` is the pair's job — the frame carries `Clip`, `Track`, `TimelineTick`, `Direction`, and flags, so the consumer works from authored data:

```csharp
public readonly struct MoveY : ITrack<JumpTrack, JumpClip>
{
    public static void OnActive(in Frame<JumpTrack, JumpClip> frame, ref float y)
        => y += frame.Direction * frame.Clip.Height * frame.Track.Scale;
}

public readonly struct PlaySound : ITrack<SoundTrack, SoundClip>
{
    public static void OnActive(in Frame<SoundTrack, SoundClip> frame)
    {
        if (frame.IsBackward) return;
        if (frame.Clip.Code == 1) Console.WriteLine("jump!");
        if (frame.Clip.Code == 2) Console.WriteLine("land!");
    }
}
```

Two consumer shapes, decided by the parameter list:

- `OnActive(in frame, ref float y)` — declares a gameplay column. It runs once per position when the pair folds (first use), measuring the pair's contribution into per-tick delta tables; the 4-argument `Apply` then plays back as a pure gather. Keep it pure in `ref` output — measure runs forward **and** backward, so a side-effect here would fire at fold, twice.
- `OnActive(in frame)` — no gameplay slots, pure side-effect. It is dispatch-only: never runs at fold, contributes nothing to tables. The 3-argument `Apply(ids, positions, forward)` fires it once per row at each position's tick, in clip order, `IsBackward` set on rewind — that's what makes `PlaySound` print during playback.

Each `Timeline<TTrack, TClip>` plays only its own pair: folding the jump pair never runs the sound pair's consumers, and the sound pair's `Apply` never touches the jump lane's tables.

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

`Apply` does the frame work — the 4-arg form gathers the measured delta at the entity's `Position` into the component field, and the 3-arg form fires the pair's no-output consumers at the position's tick; `Advance` moves the position by the asset's own duration/looping. Both take spans — `new Span<T>(ref x)` views one component field as a column, so no marshalling:

```csharp
foreach (var (_, c, y) in
         world.Query<TimelineComponent, JumpY>()
              .EnumerateWithEntities<TimelineComponent, JumpY>())
{
    ref var t = ref c.Value;
    Timeline<JumpTrack, JumpClip>.Apply(
        jump, new ReadOnlySpan<ushort>(in t.Position), true, new Span<float>(ref y.Value.Value));
    Timeline<JumpTrack, JumpClip>.Advance(
        jump, new Span<ushort>(ref t.Position), true);
}
```

- `Apply` is read-only on `Position`; `Advance` is the only mutation — once per entity per frame.
- Every pair folds its own lane and never runs another pair's consumers — pairs in one asset feed independent channels; the no-output `Apply` dispatches only the pair's own dispatch consumers.
- The same calls work over `EnumerateChunks` spans for bulk: cast `Span<Clock>` to `Span<ushort>` and the whole chunk applies in one gather — that's the throughput path.

## Run

Build runs a `BakeJumpTimeline` MSBuild target: it builds `Tl.Bake`, then bakes `jump.json` straight into `$(OutDir)jump.tlb` — so `dotnet run` or the IDE Run button works in any configuration:

```sh
dotnet run
```

To bake by hand: `tlb jump.json jump.tlb --assembly bin/Debug/net10.0/TlJump.dll` (`tlb` is the `Tl.Bake` dotnet tool — `dotnet tool install -g Tl.Bake --prerelease` once published, or `dotnet run --project <tl>/tools/Tl.Bake --` from a checkout).

## Note on references

`Tl.Core` / `Tl.Gen.CSharp` / `Tl.Gen.Tlb` are project references because the `Apply`/`Advance` surface is not yet published; on release they become `<PackageReference Include="Tl.CSharp" />` plus the `Tl.Gen.CSharp` analyzer package — the source stays identical.

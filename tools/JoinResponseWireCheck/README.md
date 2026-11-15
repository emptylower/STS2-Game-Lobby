# Join response wire regression

This standalone .NET 9 check invokes the production `SerializeJoinResponseBody`
against a locally supplied game assembly. It runs without launching Godot or the
game and never copies game binaries into the repository or release package.

```sh
dotnet run --project tools/JoinResponseWireCheck -- /absolute/path/to/sts2_lan_connect.dll /absolute/path/to/game/data
```

Run the command in a separate process for **each** old/new game data directory;
.NET dependencies from different game versions must not share one load context.
Use the .NET 9 runtime because the game's Harmony library does not support .NET 10.

The check compares exact bits and bytes with the real game's serializer, then
feeds the production bytes into the real game's deserializer and checks every
public field and exact bit consumption. It covers all five new mode/custom cases,
null/empty/nonempty seeds, absent/present daily times, and ascensions 0/3/20.
Legacy game layouts without mode/custom fields are tested without those fields.

The first pass uses native list width 3. The second installs the production
list-width transpilers on the real game serializer/deserializer and freezes a
compat lease (width 5). Native field order remains the oracle. Every compat body
must be exactly two bits longer than its width-3 baseline.

Player and modifier lists are intentionally empty to isolate body layout from
model/assets initialization. This check supplements the Godot nonempty-roster and
message-bus tests; it does not establish paired-game or transport compatibility.
A failed/unsupported Harmony patch fails the check rather than silently skipping.

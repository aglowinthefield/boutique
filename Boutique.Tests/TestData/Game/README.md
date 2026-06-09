# Skyrim integration test data

The Mutagen integration tests (`Boutique.Tests/Integration/`) read real records from
`Skyrim.esm`. That file is copyrighted game data and **must never be committed** — the
`.gitignore` blocks `*.esm`/`*.esp`/`*.esl`/`*.bsa`/`*.strings` in this folder.

## To run the integration tests locally

Copy `Skyrim.esm` from your Skyrim Special Edition `Data` folder into this directory:

```
Boutique.Tests/TestData/Game/Skyrim.esm
```

Then run `dotnet test`. The integration tests will pick it up automatically.

## Alternative: point the tests at your real Data folder

Instead of copying the file, set one of these environment variables:

- `BOUTIQUE_SKYRIM_DATA` — path to a folder containing `Skyrim.esm`
  (e.g. `C:\Program Files (x86)\Steam\steamapps\common\Skyrim Special Edition\Data`)
- `BOUTIQUE_SKYRIM_ESM` — full path to a `Skyrim.esm` file

## When the file is absent

If no `Skyrim.esm` can be found, the integration tests **skip** (they do not fail), so the
rest of the suite and CI stay green without the game files.

# Contributing

Thanks for wanting to help. Issues and pull requests are both welcome.

Before you spend real time on a change, please read this page. This project has a
few structural quirks that are not obvious from the repository layout, and every
one of them has cost somebody an afternoon.

## The project has two halves

Timberborn Archipelago is two codebases that have to agree with each other:

| Half | Language | What lives there |
|---|---|---|
| **Client mod** (this repo) | Unity / C# | In-game behaviour: unlock blocking, item delivery, goal and milestone tracking, the AP shop and connect panels. All of it under `Assets/Mods/Archipelago/`. |
| **APWorld** | Python | Generation-side logic: the item pool, locations, access rules, building tiers, shop layout and YAML options. |

Both sides carry their own copy of some data, notably building tiers and the
building list. If you change one, check whether the other needs the same change.
A mismatch here does not throw an error, it just silently produces a broken seed.

**Which half does your change belong to?** A useful rule of thumb: anything about
*what the randomizer decides* is APWorld, and anything about *what the game does*
is the mod. Tier and logic questions are almost always APWorld, even when a
matching value also appears in the C# source.

**APWorld-only pull requests are much easier to review and land**, because they
need no Unity toolchain and are covered by an automated test suite. If your change
can be made on that side alone, prefer it.

## Building the client mod

You need **Unity 6000.3.6f1** to open this project. The version is pinned in
`ProjectSettings/ProjectVersion.txt`, and it moves when the upstream Mechanistry
modding tools move, so check that file rather than trusting this paragraph.

The build path is currently documented and tested on Windows only. Contributors
have run the C# side on Linux, but expect to do some of your own toolchain work
there, and please do say so in your pull request so review can account for it.

### The one gotcha that will hang your editor

Upstream added an example-models import that pulls in a `.blend` file:

```csharp
ImportRawFile("TimberbornExampleModels.blend", streamingAssetsDirectory);
```

On a machine without Blender on `PATH`, Unity tries to import that `.blend` as a
3D model on the next asset database scan and **hangs**, for upwards of twelve
minutes, then crashes if you cancel it. If your editor appears to lock up on
first import, this is why.

Either install Blender, or comment that line out locally. If you comment it out,
**do not commit it**. It is a local workaround, not a fix, and it must not reach
a pull request.

## Working on the APWorld

Run the test suite from the Archipelago root:

```
python -m unittest discover -s worlds/timberborn/test -t .
```

**Use Python 3.11 or newer.** Archipelago 0.6.7 imports `typing.Self`, so on
Python 3.10 the entire test suite fails to collect with an unrelated-looking
`ImportError`. If you see that, it is your interpreter version and not your change.

There is also a fuzzer for generation stability:

```
bash run_fuzz.sh 0.02
```

The multiplier scales the seed count. Use a small value while iterating and a
full run before proposing a change to logic or the item pool.

## Consistency check

If your change touches the building list, the unlock blocker or the item pool,
please run:

```
python scripts/check_ap_consistency.py --blueprints <path-to>/StreamingAssets/Modding/Blueprints.zip
```

It catches the two ways the two halves drift apart, both of which are silent in
normal play:

1. A building the mod blocks from vanilla unlocking, but which the APWorld never
   grants as an item. It becomes permanently unbuildable.
2. A building whose internal template id no longer resolves, usually after a game
   update renames it. It silently stays unlocked and free to build, bypassing
   Archipelago entirely.

The script exits non-zero on failure and prints the offending entries.

## Reporting a bug

Please include the game version, the mod version from
`Assets/Mods/Archipelago/manifest.json`, your faction, and your YAML options if
generation is involved. A spoiler log or a seed number helps enormously.

If you have read the source and think you have found the cause, say so and point
at the file. That is genuinely useful. Do also say how confident you are, because
tier and logic values in particular are frequently *correct but surprising*: the
access tier is derived from a building's construction materials, while the shop
prices it on science cost, and those two axes disagree quite often. A cheap
building can legitimately sit behind a late tier.

## Pull requests

- Keep a pull request to one concern.
- Say what you tested, and on which side. "Ran the APWorld tests" or "built and
  loaded in game" are both useful, and so is "not tested, here is why".
- Do not bump version numbers in `manifest.json`. Releases are cut separately.
- Do not commit the `.blend` workaround described above.

All pull requests get reviewed. I am the only one with write access here, so
nothing merges automatically.

## Contact

Dowlle (Appie on Discord), in the Archipelago Discord.

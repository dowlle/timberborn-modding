# Timberborn Archipelago

An [Archipelago](https://archipelago.gg) randomizer for
[Timberborn](https://store.steampowered.com/app/1062090/Timberborn/), the beaver
colony sim by Mechanistry.

Your beavers start with almost nothing. Every building in the game is locked
behind an Archipelago item, so the tech tree you would normally climb on your own
is scattered across the multiworld instead, and other players send you the
pieces.

- **Items** are building unlocks, plus resource drops, extra beavers, boosts, and
  traps that drop a drought or a badtide on you at the worst possible moment.
- **Locations** are bought from an in-game Archipelago shop with science points.
  Some also want a specific building standing, or a population or wellbeing
  threshold, so checks pull you through the colony rather than around it.
- **Goals** include the faction Wonder, population, wellbeing, bot count, water
  storage, and surviving a long drought or badtide.

Both factions are supported, Folktails and Iron Teeth, including the buildings
and production chains unique to each.

## Status

**Pre-alpha, and playable.** The latest release is
[v0.0.5.2](https://github.com/dowlle/timberborn-modding/releases). It runs, it
has been played through to a goal in a live async multiworld, and it still has
rough edges, which is what the
[issue tracker](https://github.com/dowlle/timberborn-modding/issues) is for.

The next release is **v0.1.0**, targeting **Timberborn 1.1**, and it lands after
1.1 leaves the experimental branch.

## Installing

A release ships three files, and between them they cover two different jobs:

| File | Goes where |
|---|---|
| `Archipelago.zip` | Extract into `Documents/Timberborn/Mods/`, so you end up with a `Mods/Archipelago/` folder. This is the in-game mod. |
| `timberborn.apworld` | The Archipelago installation of whoever generates the seed. |
| `Timberborn.yaml` | Your player options template. Edit it, then hand it to the generator. |

Every Timberborn player needs the mod. Only the person generating the multiworld
needs the `.apworld`. Launch the game with the mod enabled and connect from the
Archipelago panel in game.

## Repository layout

The project is two codebases that have to agree with each other:

| Half | Language | Repository |
|---|---|---|
| **Client mod** | Unity / C# | this repository, under `Assets/Mods/Archipelago/` |
| **APWorld** | Python | [`dowlle/TimberbornArchipelago`](https://github.com/dowlle/TimberbornArchipelago), under `worlds/timberborn/` |

Roughly: the APWorld decides what the randomizer does, and the mod decides what
the game does. If you are looking for tiers, logic, the item pool or YAML
options, they are on the APWorld side.

**This repository is a fork of
[`mechanistry/timberborn-modding`](https://github.com/mechanistry/timberborn-modding),**
Mechanistry's official modding tools, which is why it also contains a Unity
project, example mods and asset-import tooling that have nothing to do with
Archipelago. The randomizer is confined to `Assets/Mods/Archipelago/`. For
general Timberborn modding questions their
[wiki](https://github.com/mechanistry/timberborn-modding/wiki) is the place to
look, not this repository.

## Issues and contributing

**Bug reports and feature requests belong in
[this repository's issue tracker](https://github.com/dowlle/timberborn-modding/issues),
including APWorld ones.** Because this is a fork, GitHub will sometimes offer you
Mechanistry's tracker instead. They maintain the modding tools, not this
randomizer.

Pull requests are welcome and all of them get reviewed. Before you start, please
read [CONTRIBUTING.md](CONTRIBUTING.md). It covers the two-halves split, the
build gotcha that will hang your Unity editor for twelve minutes, the supported
Python versions, and the consistency check to run when you touch the building
list.

## License

This repository keeps the MIT license it inherits from the upstream Mechanistry
modding tools, see [LICENSE](LICENSE). The Archipelago code under
`Assets/Mods/Archipelago/` is offered under those same terms. Bundled
third-party components keep their own licenses, notably
`Archipelago.MultiClient.Net`.

## Contact

Dowlle (Appie on Discord), in the Archipelago Discord.

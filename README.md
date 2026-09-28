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

## AI Usage Disclosure

Timberborn Archipelago is developed with AI assistance. You deserve to know how something is made before you decide how you feel about it, so here is the honest version.

- **AI writes code under my direction:** much of the mod and APWorld code, the shop and logic rules, and debugging and review passes. The design decisions, the priorities, and the accountability are mine. I use AI as a tool that helps me finish what I start, not as a replacement for anyone's craft.
- **No AI-generated art.** The mod uses Timberborn's own interface and the official Archipelago logo. No generated textures, logos, or models. This project exists because of human creative work (Mechanistry's Timberborn and their modding tools, and the Archipelago community), and it doesn't launder anyone's art through a model.
- **Nothing ships unverified.** Every APWorld change runs the full test suite and a fuzzer across thousands of generated seeds for both factions, in CI and locally. Client changes are compiled against the game's libraries and playtested in game before they merge. I don't merge code I haven't understood.
- **The footprint, for those weighing it:** measured production data ([Google, 2025](https://cloud.google.com/blog/products/infrastructure/measuring-the-environmental-impact-of-ai-inference); [independent measurement](https://www.sciencedirect.com/science/article/pii/S2542435126001145)) puts a typical AI request at roughly 0.3 Wh and a fraction of a milliliter of water. A heavy day of AI-assisted development on this project costs electricity on the order of one hot shower. The data center buildout at large is a real concern; I think it belongs in energy policy, not at the feet of hobby projects, but you may weigh that differently.
- **Why:** AI lets me actually finish my projects (I have ADHD). Using it is a considered choice, not a careless one.

If AI-assisted development is a dealbreaker for you, that's a fair call to make with the facts in front of you.

## License

This repository keeps the MIT license it inherits from the upstream Mechanistry
modding tools, see [LICENSE](LICENSE). The Archipelago code under
`Assets/Mods/Archipelago/` is offered under those same terms. Bundled
third-party components keep their own licenses, notably
`Archipelago.MultiClient.Net`.
The Archipelago logo on the shop button and on locked-building costs
(`Assets/Mods/Archipelago/AssetBundles/Resources/Sprites/BottomBar/ApShopTool.png`)
is the official logo from the [Archipelago](https://github.com/ArchipelagoMW/Archipelago)
project (`data/icon.png`), used under its MIT license.

## Contact

Dowlle (Appie on Discord), in the Archipelago Discord.

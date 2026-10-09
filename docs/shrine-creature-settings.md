# Shrine creature settings

Set these fields inside an event's `wildLevelDefinition` in
`BepInEx/config/VFortress/WildShrines.yaml`:

```yaml
wildLevelDefinition:
  levelIndex: 20
  minimumStars: 3
  slsModifiers:
    Fire: Major
    Fast: Minor
  biome: Mistlands
  waveFormat: ElitesOnly
  maxCreaturesPerPhaseOverride: 12
  onlySelectMonsters:
  - SeekerSoldier
  - Gjall
  commonSpawnModifiers: {}
  rareSpawnModifiers: {}
  eliteSpawnModifiers:
    linearIncreaseRandomWaveAdjustment: true
```

For Challenge and Arena events, add `minimumStars` and `slsModifiers` directly
to the level entry in `Levels.yaml`, beside `levelIndex`.

## Minimum stars

- `minimumStars` uses visible stars: `3` means three stars, or game level 4.
- The default is `0`. Existing configurations retain their behavior.
- Values outside 0 through 10 are clamped and produce a warning.
- The floor applies to every creature in every phase, including bosses.
- The floor is applied after normal wave generation and count reduction. It does
  not spend extra spawn points or reduce the number of creatures.
- Stars already above the floor are preserved. A floor higher than the global
  `MaxCreatureStars` setting takes priority over that setting.
- Without SLS, Fortress uses the game's networked `SetLevel` method. Creature
  visuals above the vanilla star range depend on the installed level mod.

## SLS modifiers

`slsModifiers` maps exact modifier names to `Major`, `Minor`, or `Boss`.
Names are case-sensitive and must exist in the installed SLS configuration.
For example, `Fire: Major` and `Fast: Minor` are separate entries in the map.

The modifiers are guaranteed additions, not a replacement for SLS's random
modifiers. A modifier already present is not added twice. Explicit additions
use the SLS API rather than the random-selection chances and count limits.
Use boss modifiers on creatures that the modifier supports.

SLS is optional. If it is absent, the star floor still works and requested
modifiers produce a warning. Unknown modifier names or types are skipped with
a warning. The integration requires SLS's spawn-management and modifier APIs;
it is validated with SLS 1.23.1. With an older incompatible SLS version, Fortress
warns, applies the game level, and cannot guarantee that SLS will retain it.

For events using either setting, Fortress marks the creature as managed by its
spawner. SLS still supplies stats and modifiers, but does not reroll or clamp its
level, multiply it, or remove it through spawn-rate rules. Other world spawns
are unchanged.

The settings travel with the generated phase data. SLS persists the applied
creature data. Install the same Fortress build on the server and all clients.
Version 0.38 uses a new minor version to prevent older clients from receiving
phase data that they cannot read.

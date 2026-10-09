# Validation results — 2026-10-08

Tested on Valnet client 01 in a local `TestingWorld`, using a development
character and the Praetoris Season 8 **8.0.38** mod set. This was the deployed
production release at test time; 8.0.39 was only staged. SLS was **1.23.1**.
The candidate DLL was built on macOS and ran on the Linux client.

| Check | Result |
| --- | --- |
| Original Fortress 0.37.2 parses new YAML | Reproduced the limitation: `minimumStars` is an unknown property |
| Debug and Release SDK builds on macOS | Passed, zero warnings and errors |
| Candidate YAML parsing | Passed |
| Generation, wild conversion, bounds, siege phases, defaults, YAML round trip | Passed for 120 generated creature groups |
| Real tribute starts a wild shrine event | Passed through `WildShrine.UseItem` |
| Initial phase, after delayed SLS setup | All 11 tracked creatures passed |
| Save, logout, reload | All 8 tracked creatures present at inspection passed |
| Later phase after clearing enemies | All 10 tracked creatures passed |

Each live assertion required **at least three stars** in both `Character` and
networked ZDO data, plus **Fire: Major** and **Fast: Minor** from the SLS API.
The global Fortress star cap remained **2** and ordinary SLS modifier chances
remained below 100%. Inspected Necks had three stars and 20 maximum health.

The final candidate log had no SLS integration warnings or failed creature
assertions. It did contain shader errors also seen in the baseline, PieceManager
snapshot exceptions during logout, and empty death-screenshot webhook errors.
These were not treated as successful checks or silently omitted. The development
character died during screenshot capture after logout reset god mode; the live
assertions had already passed. The test instructions now explicitly restore god
and ghost mode after reload.

Two earlier test-helper failures were corrected before the passing run: wild
shrines must be spawned through the location system, and the helper must access
`ZNetView` through `GetComponent` instead of a private game field.

## Limits

- This is single-client local-world proof with the production mod set. Dedicated
  server operation, ownership transfer, and multiple clients were not tested.
- The no-SLS fallback and incompatible older SLS versions were not runtime-tested.
- Boss-specific modifiers and every creature type were not tested.
- The public upstream source reports 0.36.2, while production shipped 0.37.2.
  This fork starts from public upstream commit `f49aa2d`; it does not claim to
  reconstruct unpublished release changes.

The reproducible helper and configuration are in this directory. Do not package
`FortressValidation.dll` with the released mod.

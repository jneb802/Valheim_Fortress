# Live validation

`FortressValidation.dll` is a temporary test helper, not a release dependency.
It runs assertions inside Valheim against the actual YAML parser, wave generator,
shrine tribute flow, spawned Characters, network data, and SLS API.

Build it on the Mac with:

```sh
dotnet build validation/FortressValidation.csproj
```

Use a leased test client, a development character, and the maintained production
mirror profile. Back up the replaced DLL and configs. Deploy only the helper DLL
from `validation/bin/Debug`, not its copied dependencies. Never connect this
helper to production.

1. With the original Fortress DLL, run `vf_test_schema`. It must reject
   `minimumStars` as an unknown property.
2. Stop Valheim. Deploy the candidate Fortress DLL and this directory's
   `WildShrines.yaml`. Keep `MaxCreatureStars = 2` to test that an explicit floor
   of three takes priority. Keep SLS's ordinary modifier chances below 100%.
3. Start Valheim and select a development character. Load the existing local
   `TestingWorld`.
4. Run `vf_test_schema` and `vf_test_generate`. The latter checks the star bounds,
   wild-to-Challenge conversion, eight siege phases, default settings, and YAML
   round trips.
5. Run `devcommands`, `god`, `ghost`, and `vf_test_prepare`. Wait for shrine setup.
6. Run `cli_give_item TrophyBoar 1`, then `vf_test_offer`. This uses the shrine's
   real `UseItem` path, consumes the tribute, and starts normal wave generation.
7. After creatures spawn and SLS's delayed setup finishes, run `vf_test_inspect`
   and `vf_test_assert`. Every tracked creature must have at least three stars
   in its Character and ZDO, plus Fire (Major) and Fast (Minor).
8. Save, log out, and reload the same world. Re-enable `god` and `ghost`, since
   these settings reset on logout. Repeat inspection and assertions to
   check persistence. Check later phases by defeating the tracked enemies.
9. Run `vf_test_cleanup` before leaving. Restore the original profile files and
   verify hashes. Remove the helper and its generated config. Restore the prior
   profile selection and stop the leased client.

For the optional-dependency check, repeat the tribute flow without SLS. The
assertion checks the star floor and skips modifier assertions when SLS is absent.
The expected log warning says that SLS modifiers cannot be applied. Record any
other mods disabled because they depend on SLS.

Capture command output and inspect the relevant game log. A command returning
success does not replace the creature assertions. Keep unrelated baseline
warnings separate from errors introduced by the candidate.

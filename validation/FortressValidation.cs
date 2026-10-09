using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;
using ValheimFortress.Challenge;

// Temporary local-world proof helper. Never include this DLL in the mod release.
[BepInPlugin("fortress.validation", "Fortress Validation", "1.0.0")]
[BepInDependency("MidnightsFX.ValheimFortress")]
public sealed class FortressValidation : BaseUnityPlugin
{
    private static readonly Assembly Fortress = typeof(WildLevelDefinition).Assembly;
    private static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private void Awake()
    {
        Register("vf_test_schema", Schema);
        Register("vf_test_generate", Generate);
        Register("vf_test_prepare", Prepare);
        Register("vf_test_offer", Offer);
        Register("vf_test_inspect", Inspect);
        Register("vf_test_assert", AssertCreatures);
        Register("vf_test_cleanup", Cleanup);
    }

    private void Register(string name, Func<string> command)
    {
        new Terminal.ConsoleCommand(name, "Fortress local validation helper", args =>
        {
            try { string result = command(); args.Context.AddString(result); Logger.LogInfo(result); }
            catch (Exception e) { args.Context.AddString("FAIL " + e); Logger.LogError(e); }
        });
    }

    private static string Schema()
    {
        string yaml = "levelIndex: 1\nminimumStars: 3\nslsModifiers:\n  Fire: Major\n  Fast: Minor\n";
        WildLevelDefinition definition = CONST.yamldeserializer.Deserialize<WildLevelDefinition>(yaml);
        return "SCHEMA ACCEPTED " + CONST.yamlserializer.Serialize(definition).Replace("\n", " | ");
    }

    private static string Generate()
    {
        int checkedHordes = 0;
        Type generator = Fortress.GetType("ValheimFortress.Challenge.Levels");
        foreach (int requested in new[] { -1, 0, 3, 10, 99 })
        {
            WildLevelDefinition wild = CONST.yamldeserializer.Deserialize<WildLevelDefinition>(
                "levelIndex: 2\nbiome: Meadows\nwaveFormat: CommonOnly\nonlySelectMonsters: [Neck]\n" +
                "minimumStars: " + requested + "\nslsModifiers: {Fire: Major, Fast: Minor}\n" +
                "commonSpawnModifiers: {}\nrareSpawnModifiers: {}\neliteSpawnModifiers: {}\n");
            ChallengeLevelDefinition level = wild.ToChallengeLevelDefinition();
            PhasedWaveTemplate wave = (PhasedWaveTemplate)generator.GetMethod("generateRandomWaveWithOptions", All)
                .Invoke(null, new object[] { level, false, false, true, (short)12 });
            wave = CONST.yamldeserializer.Deserialize<PhasedWaveTemplate>(CONST.yamlserializer.Serialize(wave));
            if (wave.hordePhases.Count != 8) { throw new Exception("Siege should have eight phases"); }
            foreach (HoardConfig horde in wave.hordePhases.SelectMany(p => p))
            {
                int minimum = Math.Max(0, Math.Min(10, requested));
                if (horde.stars < minimum) { throw new Exception("Star floor lost during generation or serialization"); }
                string serialized = CONST.yamlserializer.Serialize(horde);
                if (!serialized.Contains("Fire: Major") || !serialized.Contains("Fast: Minor"))
                { throw new Exception("Modifiers lost during conversion or serialization"); }
                checkedHordes++;
            }
        }
        if (checkedHordes == 0) { throw new Exception("No hordes generated"); }
        // Omitted settings must preserve the existing defaults.
        WildLevelDefinition legacy = CONST.yamldeserializer.Deserialize<WildLevelDefinition>("levelIndex: 1");
        PropertyInfo minimumProperty = typeof(WildLevelDefinition).GetProperty("minimumStars");
        if ((int)minimumProperty.GetValue(legacy) != 0) { throw new Exception("Legacy default changed"); }
        return "PASS generation, wild conversion, bounds, siege phases, legacy defaults and YAML round trip; hordes=" + checkedHordes;
    }

    private static string Prepare()
    {
        if (Player.m_localPlayer == null) { throw new Exception("Enter a local test world first"); }
        Vector3 position = Player.m_localPlayer.transform.position + Player.m_localPlayer.transform.forward * 10f;
        position.y = ZoneSystem.instance.GetGroundHeight(position);
        HashSet<ZNetView> before = new HashSet<ZNetView>(UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None));
        if (!ZoneSystem.instance.TestSpawnLocation("VF_wild_shrine_green1", position, false))
        { throw new Exception("The game could not spawn the wild shrine location"); }
        foreach (ZNetView view in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
        {
            if (!before.Contains(view) && view.IsValid()) { view.GetZDO().Set("fortress_validation_piece", true); }
        }
        MonoBehaviour shrine = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Where(c => c.GetType().FullName == "ValheimFortress.Challenge.WildShrine")
            .OrderBy(c => Vector3.Distance(c.transform.position, position)).First();
        shrine.GetComponentInParent<ZNetView>().GetZDO().Set("fortress_validation", true);
        return "Prepared wild shrine through the game's location command path; offer tribute after setup finishes.";
    }

    private static string Offer()
    {
        Component shrine = TestShrines().First();
        ItemDrop.ItemData tribute = Player.m_localPlayer.GetInventory().GetAllItems()
            .First(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "TrophyBoar");
        object result = shrine.GetType().GetMethod("UseItem", All).Invoke(shrine, new object[] { Player.m_localPlayer, tribute });
        return "WildShrine.UseItem result=" + result + "; real tribute consumed and event requested.";
    }

    private static string Inspect()
    {
        Type receiver = Type.GetType("StarLevelSystem.modules.APIReciever, StarLevelSystem");
        List<string> lines = new List<string>();
        foreach (Character creature in TestCreatures())
        {
            Dictionary<string, int> modifiers = receiver == null ? null :
                (Dictionary<string, int>)receiver.GetMethod("GetAllModifiersForCreature").Invoke(null, new object[] { creature });
            lines.Add(creature.name + " stars=" + (creature.GetLevel() - 1) + " maxHealth=" + creature.GetMaxHealth() +
                " modifiers=" + (modifiers == null ? "none" : string.Join(",", modifiers.Select(m => m.Key + ":" + m.Value))));
        }
        return "CREATURES " + lines.Count + "\n" + string.Join("\n", lines);
    }

    private static string AssertCreatures()
    {
        List<Character> creatures = TestCreatures().ToList();
        if (creatures.Count == 0) { throw new Exception("No tracked test creatures"); }
        Type receiver = Type.GetType("StarLevelSystem.modules.APIReciever, StarLevelSystem");
        foreach (Character creature in creatures)
        {
            if (creature.GetLevel() < 4 || creature.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_level, 1) < 4)
            { throw new Exception("Three-star floor missing locally or in network data"); }
            if (receiver != null)
            {
                Dictionary<string, int> modifiers = (Dictionary<string, int>)receiver.GetMethod("GetAllModifiersForCreature")
                    .Invoke(null, new object[] { creature });
                if (modifiers == null || !modifiers.ContainsKey("Fire") || modifiers["Fire"] != 0 ||
                    !modifiers.ContainsKey("Fast") || modifiers["Fast"] != 1)
                { throw new Exception("Required Fire/Fast modifiers missing"); }
            }
        }
        return "PASS live shrine creatures=" + creatures.Count + "; stars>=3 in Character and ZDO; " +
            (receiver == null ? "SLS absent, star-only fallback" : "Fire:Major and Fast:Minor present on every creature");
    }

    private static IEnumerable<Character> TestCreatures()
    {
        foreach (MonoBehaviour shrine in TestShrines())
        {
            SpawnedCreatureRecordsZNetProperty records = (SpawnedCreatureRecordsZNetProperty)
                shrine.GetType().GetProperty("spawned_creature_records").GetValue(shrine);
            foreach (SpawnedCreatureRecord record in records.Get())
            {
                GameObject instance = ZNetScene.instance.FindInstance(record.Id);
                Character creature = instance == null ? null : instance.GetComponent<Character>();
                if (creature != null) { yield return creature; }
            }
        }
    }

    private static string Cleanup()
    {
        foreach (MonoBehaviour shrine in TestShrines())
        {
            shrine.GetType().GetMethod("CancelShrineRun", All).Invoke(shrine, null);
            ZNetScene.instance.Destroy(shrine.GetComponentInParent<ZNetView>().gameObject);
        }
        foreach (ZNetView view in UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
        {
            if (view.IsValid() && view.GetZDO().GetBool("fortress_validation_piece", false))
            { ZNetScene.instance.Destroy(view.gameObject); }
        }
        return "Cancelled test shrine runs and removed created shrines.";
    }

    private static IEnumerable<MonoBehaviour> TestShrines()
    {
        return UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Where(c => c.GetType().FullName == "ValheimFortress.Challenge.WildShrine" &&
                c.GetComponentInParent<ZNetView>()?.GetZDO()?.GetBool("fortress_validation", false) == true);
    }
}

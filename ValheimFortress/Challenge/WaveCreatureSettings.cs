using System;
using System.Collections.Generic;
using ValheimFortress.Common;

namespace ValheimFortress.Challenge
{
    public enum SlsModifierType
    {
        Major = 0,
        Minor = 1,
        Boss = 2
    }

    internal static class WaveCreatureSettings
    {
        internal static void ApplyToWave(PhasedWaveTemplate wave, ChallengeLevelDefinition level)
        {
            int minimum = Math.Max(0, level.minimumStars);
            if (minimum != level.minimumStars)
            {
                Jotunn.Logger.LogWarning($"minimumStars {level.minimumStars} is negative; using 0.");
            }
            int? maximum = level.maximumStars.HasValue ? Math.Max(0, level.maximumStars.Value) : (int?)null;
            if (maximum != level.maximumStars)
            {
                Jotunn.Logger.LogWarning($"maximumStars {level.maximumStars} is negative; using 0.");
            }
            if (maximum.HasValue && minimum > maximum.Value)
            {
                Jotunn.Logger.LogWarning($"minimumStars {minimum} exceeds maximumStars {maximum}; using {maximum} for both.");
                minimum = maximum.Value;
            }

            // Store the settings on each horde: the existing phase serialization then carries
            // them to the spawning peer and preserves them when an unfinished run reloads.
            foreach (List<HoardConfig> phase in wave.hordePhases)
            {
                foreach (HoardConfig horde in phase)
                {
                    horde.minimumStars = minimum;
                    horde.maximumStars = maximum;
                    horde.slsModifiers = level.slsModifiers == null
                        ? null : new Dictionary<string, SlsModifierType>(level.slsModifiers);
                }
            }
        }

        internal static void ApplyToCreature(Character creature, HoardConfig horde)
        {
            if (creature == null) { return; }
            int minimum = Math.Max(0, horde.minimumStars);
            long stars = Math.Max((long)creature.GetLevel() - 1, Math.Max(horde.stars, minimum));
            if (horde.maximumStars.HasValue)
            {
                stars = Math.Min(stars, Math.Max(0, horde.maximumStars.Value));
            }
            // Game levels are signed integers and include the base (zero-star) level.
            int level = (int)Math.Min(int.MaxValue, stars + 1);
            // SetLevel updates the networked level and vanilla health, unlike assigning m_level.
            // SLS receives the same level and persists its own cache through its public API.
            SlsIntegration.Apply(creature, level, horde.slsModifiers);
            if (VFConfig.EnableDebugMode.Value)
            {
                Jotunn.Logger.LogInfo($"Shrine creature {horde.creature}: minimumStars={minimum}, maximumStars={horde.maximumStars}, level={creature.GetLevel()}.");
            }
        }
    }
}

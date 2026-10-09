using System;
using System.Collections.Generic;
using System.Reflection;
using ValheimFortress.Challenge;

namespace ValheimFortress.Common
{
    // Optional integration using the same receiver and methods as SLS's published API.
    // No SLS assembly is referenced or bundled with Fortress.
    internal static class SlsIntegration
    {
        private static readonly Type Receiver = Type.GetType("StarLevelSystem.modules.APIReciever, StarLevelSystem");
        private static readonly MethodInfo SetManaged = Find("SetCreatureSpawnManaged");
        private static readonly MethodInfo SetLevel = Find("UpdateCreatureLevel");
        private static readonly MethodInfo GetModifiers = Find("GetAllModifiersForCreature");
        private static readonly MethodInfo GetPossibleModifiers = Find("GetPossibleModifiersForType");
        private static readonly MethodInfo AddModifier = Find("AddModifierToCreature");
        private static readonly MethodInfo ApplyUpdates = Find("ApplyUpdatesToCreature");
        private static readonly HashSet<string> Warnings = new HashSet<string>();

        private static MethodInfo Find(string name)
        {
            return Receiver?.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
        }

        private static void WarnOnce(string message)
        {
            if (Warnings.Add(message)) { Jotunn.Logger.LogWarning(message); }
        }

        private static void RequireSuccess(MethodInfo method, params object[] arguments)
        {
            if (!(bool)method.Invoke(null, arguments))
            {
                throw new InvalidOperationException($"SLS {method.Name} returned false.");
            }
        }

        internal static void Apply(Character creature, int level, Dictionary<string, SlsModifierType> modifiers)
        {
            bool hasModifiers = modifiers != null && modifiers.Count > 0;
            if (Receiver == null)
            {
                creature.SetLevel(level);
                if (hasModifiers) { WarnOnce("Shrine slsModifiers require StarLevelSystem. Applying stars only."); }
                return;
            }

            if (SetManaged == null || SetLevel == null || GetModifiers == null ||
                GetPossibleModifiers == null || AddModifier == null || ApplyUpdates == null)
            {
                creature.SetLevel(level);
                WarnOnce("Shrine creature settings require an SLS version with SetCreatureSpawnManaged and modifier APIs. Upgrade SLS; its spawn rules may override the stars.");
                return;
            }

            try
            {
                // Do this in the spawn frame, before SLS's delayed setup can change the level,
                // multiply the spawn, or remove a creature that Fortress is tracking.
                RequireSuccess(SetManaged, creature, true);
                creature.SetLevel(level);
                RequireSuccess(SetLevel, creature, level);

                if (hasModifiers)
                {
                    Dictionary<string, int> existing = (Dictionary<string, int>)GetModifiers.Invoke(null, new object[] { creature });
                    foreach (KeyValuePair<string, SlsModifierType> modifier in modifiers)
                    {
                        int type = (int)modifier.Value;
                        if (!Enum.IsDefined(typeof(SlsModifierType), modifier.Value))
                        {
                            WarnOnce($"Invalid SLS modifier type {type} for '{modifier.Key}'; skipped.");
                            continue;
                        }
                        List<string> available = (List<string>)GetPossibleModifiers.Invoke(null, new object[] { type });
                        if (available == null || !available.Contains(modifier.Key))
                        {
                            WarnOnce($"Unknown SLS {modifier.Value} modifier '{modifier.Key}'; skipped. Names are case-sensitive.");
                            continue;
                        }
                        // SLS returns false for a modifier it already rolled. Do not add it twice.
                        if (existing != null && existing.ContainsKey(modifier.Key)) { continue; }
                        RequireSuccess(AddModifier, creature, modifier.Key, type, true);
                    }
                }
                RequireSuccess(ApplyUpdates, creature);
            }
            catch (Exception exception)
            {
                creature.SetLevel(level);
                Exception cause = exception is TargetInvocationException && exception.InnerException != null
                    ? exception.InnerException : exception;
                WarnOnce($"Could not apply shrine SLS settings: {cause.Message}");
            }
        }
    }
}

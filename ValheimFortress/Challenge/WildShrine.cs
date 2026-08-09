using Jotunn.Extensions;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimFortress.Challenge
{
    internal class WildShrine : GenericShrine
    {
        private static int log_slower = 0;
        private WildShrineConfiguration wildShrineConfiguration;
        public override void Awake()
        {}

        private void setupLateNetworking()
        {
            zNetView = GetComponentInParent<ZNetView>();
            if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo($"Looking for a parented znet view {zNetView}."); }

            spawned_creatures = new IntZNetProperty("spawned_creatures", zNetView, 0);
            hard_mode = new BoolZNetProperty("shrine_hard_mode", zNetView, false);
            boss_mode = new BoolZNetProperty("shrine_boss_mode", zNetView, false);
            siege_mode = new BoolZNetProperty("shrine_siege_mode", zNetView, false);
            challenge_active = new BoolZNetProperty("shrine_challenge_active", zNetView, false);
            start_challenge = new BoolZNetProperty("shrine_start_challenge", zNetView, false);
            selected_level = new IntZNetProperty("shrine_selected_level", zNetView, 0);
            selected_reward = new StringZNetProperty("shrine_selected_reward", zNetView, "coins");
            portal_disabled = new BoolZNetProperty("end_of_challenge", zNetView, false);
            should_add_creature_beacons = new BoolZNetProperty("should_add_creature_beacons", zNetView, false);
            currentPhase = new IntZNetProperty("shrine_current_phase", zNetView, 0);
            wave_definition_ready = new BoolZNetProperty("wave_definition_ready", zNetView, false);
            spawn_locations_ready = new BoolZNetProperty("spawn_locations_ready", zNetView, false);
            force_next_phase = new BoolZNetProperty("force_next_phase", zNetView, false);
            phase_spawned_total = new IntZNetProperty("phase_spawned_total", zNetView, 0);
            phase_spawn_in_flight = new BoolZNetProperty("phase_spawn_in_flight", zNetView, false);
            challenge_progress_time = new IntZNetProperty("challenge_progress_time", zNetView, 0);
            remote_spawn_locations = new ArrayVectorZNetProperty("remote_spawn_locations", zNetView, new Vector3[0]);
            if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Created shrine znet values."); }

            Dictionary<String, short> default_creature_dictionary = new Dictionary<String, short>() { };
            alive_creature_list = new DictionaryZNetProperty("alive_creature_list", zNetView, default_creature_dictionary);
            spawned_creature_records = new SpawnedCreatureRecordsZNetProperty("spawned_creature_records", zNetView, new List<SpawnedCreatureRecord>());

            // Must match the name the other shrines use. This previously registered "levelsyaml_rpc", which
            // is VFConfig's challenge-level config channel, so wave definitions were fed to the level-config
            // parser and never reached other clients. Initial synchronization is deliberately not registered
            // here either -- see the note in GenericShrine.Awake.
            WaveDefinitionRPC = NetworkManager.Instance.AddRPC("VF_levelsyaml_rpc", VFConfig.OnServerRecieveConfigs, OnClientReceivePhaseConfigs);

            if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Added shrine custom RPC."); }
        }

        public override string GetHoverName()
        {
            return Localization.instance.Localize(wildShrineConfiguration.wildShrineNameLocalization);
        }

        public override string GetHoverText()
        {
            string text = $"[<color=yellow><b>$KEY_Use</b></color>] {wildShrineConfiguration.wildShrineNameLocalization}";
            return Localization.instance.Localize(text);
        }

        public override bool Interact(Humanoid user, bool hold, bool alt)
        {
            user.Message(MessageHud.MessageType.Center, Localization.instance.Localize(wildShrineConfiguration.wildShrineRequestLocalization));
            return false;
        }

        public override bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo($"znet data {start_challenge.Get()} {challenge_active.Get()}."); }
            if (start_challenge.Get() || challenge_active.Get())
            {
                // Don't want the message that you can't do that, because its already done
                return true;
            }
            if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo($"Evaluating offered tribute {item.m_shared.m_name} {item.m_dropPrefab.name}."); }
            short wild_level_index = 0;
            foreach(WildShrineLevelConfiguration wlevelcfg in wildShrineConfiguration.wildShrineLevelsConfig)
            {
                if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo($"Checking tribute: {wlevelcfg.tributeName} == {item.m_dropPrefab.name} ({(wlevelcfg.tributeName == item.m_dropPrefab.name)})"); }
                if (wlevelcfg.tributeName == item.m_dropPrefab.name)
                {
                    List<ItemDrop.ItemData> user_inventory = user.m_inventory.GetAllItemsInGridOrder();
                    if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo($"User Inventory contains {user_inventory.Count} items."); }
                    int user_tribute_count = 0;
                    Dictionary<ItemDrop.ItemData, int> user_tribute_indexes = new Dictionary<ItemDrop.ItemData, int>();
                    foreach (ItemDrop.ItemData user_item in user_inventory)
                    {
                        if (user_item.m_dropPrefab.name == wlevelcfg.tributeName)
                        {
                            user_tribute_indexes.Add(user_item, user_item.m_stack);
                            user_tribute_count += user_item.m_stack;
                        }
                        
                    }
                    if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo($"User Inventory contains {user_tribute_count} of {wlevelcfg.tributeAmount} required tribute type {wlevelcfg.tributeName}."); }
                    if (user_tribute_count >= wlevelcfg.tributeAmount) {
                        user.GetInventory().RemoveItem(item, wlevelcfg.tributeAmount);
                        hard_mode.ForceSet(wlevelcfg.hardMode);
                        siege_mode.ForceSet(wlevelcfg.siegeMode);
                        selected_level.ForceSet(wild_level_index);
                        start_challenge.ForceSet(true);
                        wave_definition_ready.ForceSet(false);
                        spawn_locations_ready.ForceSet(false);
                        user.Message(MessageHud.MessageType.Center, Localization.instance.Localize(wlevelcfg.wildshrine_wave_start_localization));
                        Jotunn.Logger.LogInfo($"Deducted {wlevelcfg.tributeAmount} {wlevelcfg.tributeName} and enabling challenge setup.");
                        return true;
                    } else
                    {
                        user.Message(MessageHud.MessageType.Center, Localization.instance.Localize(wildShrineConfiguration.shrine_larger_tribute_required_localization));
                        return true;
                    }
                }
                wild_level_index++;
            }

            user.Message(MessageHud.MessageType.Center, Localization.instance.Localize(wildShrineConfiguration.shrine_unaccepted_tribute_localization));
            return false;
        }

        public override void StartChallengeMode()
        {
            currentPhase.Set(0); //ensure this is zero
            // Must be before portals are placed
            challenge_active.Set(true);
            // Should be before the phase starts
            BeginPhaseSpawn();
            RemoteLocationPortals.DrawMapOverlayAndPortals(remote_spawn_locations.Get(), gameObject.GetComponent<WildShrine>(), VFConfig.EnableShrineMapOverlay.Value);
            spawn_controller.TrySpawningPhase(5f, false, wave_phases_definitions.hordePhases[currentPhase.Get()], gameObject, remote_spawn_locations.Get());
            SetCurrentCreatureList(wave_phases_definitions.hordePhases[currentPhase.Get()]);
            start_challenge.Set(false);
            currentPhase.Set(currentPhase.Get() + 1);
            Jotunn.Logger.LogInfo($"Challenge started. Level: {selected_level.Get()} Reward: {selected_reward.Get()}");
            Jotunn.Logger.LogInfo("Start challenge functions completed. Challenge started!");
            if (shrine_portal == null)
            {
                shrine_portal = gameObject.transform.Find("portal").gameObject;
            }

        }

        public override void Update()
        {
            log_slower++;
            if (log_slower > 60) { log_slower = 0; }
            // This is required for the location because the used znetView isn't available until the location is placed.
            if (zNetView == null)
            {
                setupLateNetworking();
                return; // skip the first update tick
            }
            // We do nothing when this is not a znet object (this happens during object placement)
            if (zNetView.IsValid() != true) { return; }
            //if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Znet Valid, starting update."); }
            if (wildShrineConfiguration == null)
            {
                string shrine_name = gameObject.name;
                shrine_name = shrine_name.Remove(shrine_name.Length - 7,7); // remove the last 7 characters which is (Clone)
                if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo($"Loading level definition for shrine {shrine_name}."); }
                wildShrineConfiguration = WildShrineData.GetWildShrineConfigurationForSpecificShrine(shrine_name);
            }

            // reconnect componets if they go missing. Awake is intentionally empty for these shrines (the
            // location's ZNetView does not exist yet), so this is also where the base class references get
            // populated. These are per-instance fields: a wild shrine must only ever resolve its own
            // spawnpoint, or rewards end up at a different shrine entirely.
            if (spawn_controller == null || shrine_spawnpoint == null || shrine_portal == null)
            {
                if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Missing required references for shrine, reconnecting."); }
                if (spawn_controller == null) { spawn_controller = this.gameObject.GetComponent<Spawner>(); }
                if (shrine_spawnpoint == null) { shrine_spawnpoint = this.transform.FindDeepChild("spawnpoint").gameObject; }
                if (shrine_portal == null) { shrine_portal = gameObject.transform.Find("portal").gameObject; }
            }
            //if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Componets available."); }

            // So clients and servers see the internal structure portal update
            if (challenge_active.Get() == true)
            {
                EnablePortal();
            }
            else if (portal_disabled.Get() == true)
            {
                Disableportal();
            }
            //if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Checked portal status."); }

            // Everything past here should only be run once, by whatever main thread is controlling the ticks in this region.
            NoteOwnershipState(zNetView.IsOwner());
            if (!zNetView.IsOwner())
            {
                return;
            }
            //if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Entering ZnetOwner States."); }

            // Kick off the challenge- even if it was trigger by a non-znet owner
            if (start_challenge.Get() == true)
            {
                // if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Kicking off wave start."); }

                if (wave_definition_ready.Get() == false && spawn_locations_ready.Get() == false)
                {
                    if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Build spawn locations & wave definition."); }
                    WildShrineLevelConfiguration wLevelDefinition = wildShrineConfiguration.wildShrineLevelsConfig.ElementAt(selected_level.Get());
                    wave_phases_definitions = Levels.generateRandomWaveWithOptions(wLevelDefinition.wildLevelDefinition.ToChallengeLevelDefinition(), hard_mode.Get(), false, siege_mode.Get(), wLevelDefinition.wildLevelDefinition.maxCreaturesPerPhaseOverride);
                    wave_definition_ready.Set(true);
                    selected_reward.Set(string.Join(", ", wLevelDefinition.rewards.Keys));
                    StartCoroutine(RemoteLocationPortals.DetermineRemoteSpawnLocations(gameObject, gameObject.GetComponent<WildShrine>()));
                }

                // We can only actually start the challenge when all of the data objects are ready
                if (wave_definition_ready.Get() == true && spawn_locations_ready.Get() == true)
                {
                    Vector3[] spawn_points = remote_spawn_locations.Get();
                    if (wave_phases_definitions == null || spawn_points == null || spawn_points.Length == 0)
                    {
                        // we got here but arn't ready yet, DO IT AGAIN. Returning matters: falling through
                        // would start the challenge (setting challenge_active) against a null wave and throw,
                        // leaving the shrine permanently active and unable to pay out.
                        wave_definition_ready.Set(false);
                        spawn_locations_ready.Set(false);
                        if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Challenge tried to start but did not have the data objects required to do so, attempting regeneration."); }
                        return;
                    }
                    if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Starting challenge and sending phase configs to others."); }
                    SendUpdatedPhaseConfigs();
                    StartChallengeMode();
                }
                // we skip to the next update iteration
                return;
            }

            // Jotunn.Logger.LogInfo("Did not need to start a challenge.");
            //if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Challenge start not needed."); }

            if (challenge_active.Get() == true)
            {
                //if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Challenge active."); }
                // Authoritatively reconcile the alive creature count from tracked ZDOIDs (throttled). This is
                // what advances phases when creatures die, independent of creature/shrine ZDO ownership.
                ReconcileIfDue();
                CheckProgressWatchdog();

                // Rebuild the local enemies list once after taking ownership. It only drives the creature
                // beacons and the teleport action now -- the phase gate reads ZDO state -- so this no longer
                // needs to run every frame while creatures are alive but not yet loaded locally.
                if (local_enemies_synced == false)
                {
                    local_enemies_synced = true;
                    StartCoroutine(ReconnectUnlinkedCreatures(shrine_spawnpoint.transform.position, gameObject.GetComponent<WildShrine>()));
                }

                // The wave definition is local-only state, so a client that took ownership part-way through
                // a run may not have it. Rebuild it once from the ZDO-backed level and mode flags.
                if (wave_phases_definitions == null || wave_phases_definitions.hordePhases == null)
                {
                    Jotunn.Logger.LogInfo("Shrine is missing its wave definition, regenerating it.");
                    WildShrineLevelConfiguration wLevelDefinition = wildShrineConfiguration.wildShrineLevelsConfig.ElementAt(selected_level.Get());
                    wave_phases_definitions = Levels.generateRandomWaveWithOptions(wLevelDefinition.wildLevelDefinition.ToChallengeLevelDefinition(), hard_mode.Get(), false, siege_mode.Get(), wLevelDefinition.wildLevelDefinition.maxCreaturesPerPhaseOverride);
                    RemoteLocationPortals.DrawMapOverlayAndPortals(remote_spawn_locations.Get(), gameObject.GetComponent<WildShrine>(), VFConfig.EnableShrineMapOverlay.Value);
                    return;
                }

                if (wave_phases_definitions.hordePhases != null && wave_phases_definitions.hordePhases.Count > 0)
                {
                    // We need to A. have spawned creatures & there needs to be none of those spawned creatures remaining
                    if (VFConfig.EnableDebugMode.Value && log_slower == 60) { Jotunn.Logger.LogInfo($"Checking phase_spawned_total: {phase_spawned_total.Get()} spawned_creatures: {spawned_creatures.Get()} phase_spawn_in_flight: {phase_spawn_in_flight.Get()} (local enemies: {enemies.Count})"); }
                    ///if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Checking start for next phase"); }

                    if (ShouldAdvancePhase())
                    {
                        if (RemainingPhases())
                        {
                            if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Starting next phase."); }
                            // Start the next phase
                            should_add_creature_beacons.Set(false);
                            force_next_phase.Set(false);
                            var current_phase = currentPhase.Get();
                            BeginPhaseSpawn();
                            spawn_controller.TrySpawningPhase(10f, true, wave_phases_definitions.hordePhases[current_phase], gameObject, remote_spawn_locations.Get());
                            SetCurrentCreatureList(wave_phases_definitions.hordePhases[current_phase]);
                            int max_wave_phase = wave_phases_definitions.hordePhases.Count;
                            int expected_next_phase = currentPhase.Get() + 1;
                            if (max_wave_phase <= expected_next_phase)
                            {
                                currentPhase.Set(max_wave_phase);
                            }
                            else
                            {
                                currentPhase.Set(expected_next_phase);
                            }
                        }
                        else
                        {
                            // No phases remaining, we are done!
                            Jotunn.Logger.LogInfo("Challenge complete! Spawning reward.");
                            List<Player> nearby_players = new List<Player> { };
                            Player.GetPlayersInRange(this.transform.position, VFConfig.ShrineAnnouncementRange.Value, nearby_players);
                            foreach (Player localplayer in nearby_players)
                            {
                                localplayer.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$shrine_challenge_complete"));
                            }
                            WildShrineLevelConfiguration wLevelDefinition = wildShrineConfiguration.wildShrineLevelsConfig.ElementAt(selected_level.Get());
                            SpawnMultiRewardsDirectly(wLevelDefinition.rewards, wLevelDefinition.wildLevelDefinition.levelIndex, shrine_spawnpoint.transform.position, hard_mode.Get(), boss_mode.Get(), siege_mode.Get());
                            challenge_active.Set(false);
                            RemoteLocationPortals.ClearMapOverlay();
                            // Clear records/enemies and destroy any creatures still alive (e.g. a forced finish).
                            DestroyAllSpawnedCreatures();
                            boss_mode.Set(false);
                            hard_mode.Set(false);
                            siege_mode.Set(false);
                            portal_disabled.Set(true);
                            force_next_phase.Set(false);
                            Disableportal();
                            wave_phases_definitions = new PhasedWaveTemplate() { hordePhases = new List<List<HoardConfig>> { } }; // Got to clear the template
                            SendUpdatedPhaseConfigs();
                            currentPhase.Set(0);
                            wave_definition_ready.Set(false);
                            spawn_locations_ready.Set(false);
                            phase_spawned_total.Set(0);
                            phase_spawn_in_flight.Set(false);
                            challenge_progress_time.Set(0);
                        }
                    }
                }
            }
        }
    }
}

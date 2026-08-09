using Jotunn.Extensions;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ValheimFortress.Data;

namespace ValheimFortress.Challenge
{
    internal class ChallengeShrine : GenericShrine
    {
        private ChallengeShrineUI ui_controller;
        private short fail_to_start = 0;

        public override string GetHoverName()
        {
            return Localization.instance.Localize("$piece_shrine_of_challenge");
        }

        public override string GetHoverText()
        {
            string text = "[<color=yellow><b>$KEY_Use</b></color>] $piece_shrine_of_challenge";
            return Localization.instance.Localize(text);
            // return "\n[<color=yellow><b>E</b></color>] Alter of Challenge";
        }

        public override bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold)
            {
                return false;
            }

            //TODO: Add in support for ward checks

            if (challenge_active.Get())
            {
                // Cancel / remaining UI here.
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, $"Creatures remaining {spawned_creatures.Get()}");
                ui_controller.DisplayCancelUI();
            }
            else
            {
                ui_controller.DisplayUI();
                Jotunn.Logger.LogInfo($"Challenge UI from {this.GetInstanceID()}");
            }

            return true;
        }

        public override void StartChallengeMode()
        {
            if (challenge_active.Get() == false)
            {
                RemoteLocationPortals.DrawMapOverlayAndPortals(remote_spawn_locations.Get(), gameObject.GetComponent<ChallengeShrine>(), VFConfig.EnableShrineMapOverlay.Value);
                currentPhase.Set(0); //ensure this is zero
                challenge_active.Set(true);
                BeginPhaseSpawn();
                spawn_controller.TrySpawningPhase(5f, false, wave_phases_definitions.hordePhases[currentPhase.Get()], gameObject, remote_spawn_locations.Get());
                SetCurrentCreatureList(wave_phases_definitions.hordePhases[currentPhase.Get()]);
                Jotunn.Logger.LogInfo($"Challenge started. Level: {selected_level.Get()} Reward: {selected_reward.Get()}");
                start_challenge.Set(false);
                currentPhase.Set(currentPhase.Get() + 1);
                Jotunn.Logger.LogInfo("Start challenge functions completed. Challenge started!");
            } else
            {
                if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Challenge mode is already active."); }
                fail_to_start++;
            }
            
            // this needs to be not super small due to how many times this function is triggered
            if (fail_to_start > 3)
            {
                if (VFConfig.EnableDebugMode.Value) { Jotunn.Logger.LogInfo("Challenge mode failed to start, resetting."); }
                // reset all of the characteristics so that we get it all rebuilt to ensure the next try will actually work
                challenge_active.Set(false);
                wave_definition_ready.Set(false);
                spawn_locations_ready.Set(false);
                fail_to_start = 0;
                currentPhase.Set(0);
                start_challenge.Set(true);
            }
        }

        public override void Update()
        {
            // We do nothing when this is not a znet object (this happens during object placement)
            //Jotunn.Logger.LogInfo("Checking zNetView Validness.");
            if (zNetView.IsValid() != true) { return; }

            // reconnect componets if they go missing
            if (ui_controller == null || spawn_controller == null)
            {
                spawn_controller = this.gameObject.GetComponent<Spawner>();
                ui_controller = this.gameObject.GetComponent<ChallengeShrineUI>();
            }

            if (ui_controller.IsShrineOrCancelUIVisible() && (Input.GetKeyDown(KeyCode.Escape)))
            {
                // Jotunn.Logger.LogInfo("Shrine UI detected close commands.");
                ui_controller.HideUI();
                ui_controller.HideCancelUI();
            }
            // Jotunn.Logger.LogInfo("Shrine UI not closing.");

            // So clients and servers see the internal structure portal update
            // Jotunn.Logger.LogInfo("Checking Portal status.");
            if (challenge_active.Get() == true)
            {
                EnablePortal();
            }
            else if (portal_disabled.Get() == true)
            {
                Disableportal();
            }

            // Jotunn.Logger.LogInfo("Checking if this is the owner.");
            // Everything past here should only be run once, by whatever main thread is controlling the ticks in this region.
            NoteOwnershipState(zNetView.IsOwner());
            if (!zNetView.IsOwner())
            {
                return;
            }

            // Jotunn.Logger.LogInfo("Entering ZnetOwner States.");

            // Jotunn.Logger.LogInfo("Checking to start the challenge.");
            // Kick off the challenge- even if it was trigger by a non-znet owner
            if (start_challenge.Get() == true)
            {
                if (wave_definition_ready.Get() == false && spawn_locations_ready.Get() == false)
                {
                    List<ChallengeLevelDefinition> clevels = ChallengeLevels.GetChallengeLevelDefinitions();
                    ChallengeLevelDefinition levelDefinition = clevels.ElementAt(selected_level.Get());
                    wave_phases_definitions = Levels.generateRandomWaveWithOptions(levelDefinition, hard_mode.Get(), boss_mode.Get(), siege_mode.Get(), VFConfig.ChallengeShrineMaxCreaturesPerWave.Value);
                    wave_definition_ready.Set(true);
                    StartCoroutine(RemoteLocationPortals.DetermineRemoteSpawnLocations(gameObject, gameObject.GetComponent<ChallengeShrine>()));
                }

                // We can only actually start the challenge when all of the data objects are ready
                if (wave_definition_ready.Get() == true && spawn_locations_ready.Get() == true)
                {
                    SendUpdatedPhaseConfigs();
                    StartChallengeMode();
                }
                // we skip to the next update iteration
                return;
            }

            // Jotunn.Logger.LogInfo("Did not need to start a challenge.");
            // Jotunn.Logger.LogInfo("Checking if the challenge is already active.");
            if (challenge_active.Get() == true)
            {
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
                    StartCoroutine(ReconnectUnlinkedCreatures(shrine_spawnpoint.transform.position, gameObject.GetComponent<ChallengeShrine>()));
                }

                // The wave definition is local-only state, so a client that took ownership part-way through
                // a run may not have it. Rebuild it once from the ZDO-backed level and mode flags.
                if (wave_phases_definitions == null || wave_phases_definitions.hordePhases == null)
                {
                    Jotunn.Logger.LogInfo("Shrine is missing its wave definition, regenerating it.");
                    List<ChallengeLevelDefinition> clevels = ChallengeLevels.GetChallengeLevelDefinitions();
                    ChallengeLevelDefinition levelDefinition = clevels.ElementAt(selected_level.Get());
                    wave_phases_definitions = Levels.generateRandomWaveWithOptions(levelDefinition, hard_mode.Get(), boss_mode.Get(), siege_mode.Get(), VFConfig.ChallengeShrineMaxCreaturesPerWave.Value);
                    RemoteLocationPortals.DrawMapOverlayAndPortals(remote_spawn_locations.Get(), gameObject.GetComponent<ChallengeShrine>(), VFConfig.EnableShrineMapOverlay.Value);
                    wave_definition_ready.Set(true);
                    return;
                    // Ideally everything else is currently still correct since the last time this object was loaded
                }

                // Jotunn.Logger.LogInfo("Checking for remaining phases and active enemies.");
                if (wave_phases_definitions.hordePhases != null && wave_phases_definitions.hordePhases.Count > 0)
                {
                    // We need to A. have spawned creatures & there needs to be none of those spawned creatures remaining
                    if (ShouldAdvancePhase())
                    {
                        if (RemainingPhases())
                        {
                            Jotunn.Logger.LogInfo("Starting next phase");
                            // Start the next phase
                            should_add_creature_beacons.Set(false);
                            force_next_phase.Set(false);
                            var current_phase = currentPhase.Get();
                            BeginPhaseSpawn();
                            spawn_controller.TrySpawningPhase(10f, true, wave_phases_definitions.hordePhases[current_phase], gameObject, remote_spawn_locations.Get());
                            SetCurrentCreatureList(wave_phases_definitions.hordePhases[current_phase]);
                            currentPhase.Set(current_phase + 1);
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
                            SpawnReward(shrine_spawnpoint.transform.position);
                            challenge_active.Set(false);
                            RemoteLocationPortals.ClearMapOverlay();
                            boss_mode.Set(false);
                            hard_mode.Set(false);
                            siege_mode.Set(false);
                            // Clear records/enemies and destroy any creatures still alive (e.g. a forced finish).
                            DestroyAllSpawnedCreatures();
                            force_next_phase.Set(false);
                            portal_disabled.Set(true);
                            Disableportal();
                            wave_phases_definitions = new PhasedWaveTemplate() { hordePhases = new List<List<HoardConfig>> { } }; // Got to clear the template
                            SendUpdatedPhaseConfigs();
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

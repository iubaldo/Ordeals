using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;


namespace ordeals.src
{
    /*
     * TODO:
     * - implement configs
     *      enable ordeals
     *      enable natural spawning
     *      enable time limit
     *      enable certain ordeal variants
     *          note: would need to change ordealvariantutil methods
     * - delay ordeal feature/command
     * - correct time settings for ordeal scheduling
     * - time limit settings to forcibly end ordeal
     * - disable begin/end ordeal from firing if splash animation/sound is still running
     *      or add the animation to a stack and play when current one is finished
     * - fix crashing issue when scaling
     *      need to either figure out how to scale png or switch to svg
     * 
     * long term:
     * - all additional ordeal variants
     * - update chat messages to use lang files in case people want to do translations
     *      would also mean I would have to make more splash screens lmao
     */


    class OrdealsMain : ModSystem
    {
        ICoreAPI api;
        ICoreServerAPI sapi;
        ICoreClientAPI capi;

        IServerNetworkChannel sOrdealEventChannel;
        IClientNetworkChannel cOrdealEventChannel;

        IServerNetworkChannel sShowSplashChannel;
        IClientNetworkChannel cShowSplashChannel;

        //Dictionary<OrdealTier, OrdealStrength> ordealStrengths;   
        //Dictionary<OrdealVariant, WaveSpawnSettings> ordealSpawnSettings;
        //Dictionary<OrdealVariant, EntityProperties> ordealEntityTypes;
        //Dictionary<IServerPlayer, List<Entity>> ordealActiveWaves = new Dictionary<IServerPlayer, List<Entity>>();

        OrdealSplash splash;

        OrdealEventConfig config;
        OrdealEventRuntimeData runtimeData = new OrdealEventRuntimeData();

        // TODO: implement disabling features via config
        bool ordealsEnabled;
        bool naturalSpawningEnabled;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;
        }


        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);

            // InitConfigs();
            api.Logger.StoryEvent("The unknown...");
            api.Logger.StoryEvent("The uncontainable...");
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            sapi = api;

            sOrdealEventChannel =
                api.Network.RegisterChannel("ordealeventruntimedata")
               .RegisterMessageType(typeof(OrdealEventRuntimeData));

            sShowSplashChannel =
               api.Network.RegisterChannel("showsplash")
               .RegisterMessageType(typeof(OrdealVariantTime));

            api.Event.SaveGameLoaded += () =>
            {
                bool shouldPrepNextOrdeal = sapi.WorldManager.SaveGame.IsNew;

                byte[] bytedata = sapi.WorldManager.SaveGame.GetData("ordealEventRuntimeData");
                if (bytedata != null)
                {
                    try
                    {
                        runtimeData = SerializerUtil.Deserialize<OrdealEventRuntimeData>(bytedata);
                    } catch (Exception)
                    {
                        api.World.Logger.Notification("Failed loading ordeal event runtime data, will initialize new data set");
                        runtimeData = new OrdealEventRuntimeData();
                        runtimeData.nextStartTotalDays = sapi.World.Calendar.DaysPerMonth;
                        shouldPrepNextOrdeal = true;
                    }
                }
                else
                {
                    runtimeData = new OrdealEventRuntimeData();
                    runtimeData.nextStartTotalDays = sapi.World.Calendar.DaysPerMonth;
                    shouldPrepNextOrdeal = true;
                }

                if (shouldPrepNextOrdeal)
                    PrepareNextOrdeal();
            };
            

            api.Event.GameWorldSave += Event_GameWorldSave;
            api.Event.PlayerJoin += Event_PlayerJoin;
            api.Event.PlayerNowPlaying += Event_PlayerNowPlaying;
            api.Event.OnEntityDeath += Event_OnEntityDeath;
            api.Event.RegisterGameTickListener(OnOrdealEventTick, 2000);
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, LoadEntities);
            RegisterCommands();
        }

 
        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            capi = api;

            cOrdealEventChannel =
                api.Network.RegisterChannel("ordealeventruntimedata")
                .RegisterMessageType(typeof(OrdealEventRuntimeData))
                .SetMessageHandler<OrdealEventRuntimeData>(OnServerData);
            
            cShowSplashChannel =
                api.Network.RegisterChannel("showsplash")
                .RegisterMessageType(typeof(OrdealVariantTime))
                .SetMessageHandler<OrdealVariantTime>(OnShowSplash);

            splash = new OrdealSplash(capi);
        }


        private void OnServerData(OrdealEventRuntimeData data) { this.runtimeData = data; }


        private void OnOrdealEventTick(float dt)
        {
            if (!ordealsEnabled)
            {
                if (runtimeData.currentEvent != null)
                    EndOrdeal();
                return;
            }

            if (config == null)
                LoadConfigs();

            if (runtimeData.eventStack.Count == 0)
            {
                sapi.Logger.Notification("Ordeal Event stack is empty, preparing a new ordeal.");
                PrepareNextOrdeal();
            }
                

            double nextOrdealDaysLeft = runtimeData.PeekEvent().startTimeTotalDays - api.World.Calendar.TotalDays;

            if (nextOrdealDaysLeft > 0.03 && nextOrdealDaysLeft < 0.35 && runtimeData.noticeStatus == OrdealNotice.DoNotNotify) 
            {
                runtimeData.noticeStatus = OrdealNotice.Approaching;

                OrdealVariant variant = runtimeData.PeekEvent().PeekWave().variant;
                string message = "The " + variant.FirstPart() + " of " + variant.SecondPart() + " approaches.";
                sapi.BroadcastMessageToAllGroups(message, EnumChatType.Notification);
            }

            if (nextOrdealDaysLeft <= 0.02 && (runtimeData.noticeStatus == OrdealNotice.DoNotNotify || runtimeData.noticeStatus == OrdealNotice.Approaching))
            {
                runtimeData.noticeStatus = OrdealNotice.Imminent;

                OrdealVariant variant = runtimeData.PeekEvent().PeekWave().variant;
                string message = "The " + variant.FirstPart() + " of " + variant.SecondPart() + " is imminent.";
                sapi.BroadcastMessageToAllGroups(message, EnumChatType.Notification);
            }

            if (nextOrdealDaysLeft <= 0)
            {
                if (runtimeData.currentEvent == null && runtimeData.currentEvent.startTimeTotalDays + runtimeData.currentEvent.timeLimitTotalDays < api.World.Calendar.TotalDays)
                {
                    EndOrdeal();
                    return;
                }

                if (runtimeData.currentEvent == null)
                    BeginOrdealEvent();

                double activeDaysLeft = runtimeData.currentEvent.startTimeTotalDays + runtimeData.currentEvent.timeLimitTotalDays - api.World.Calendar.TotalDays;
                if (activeDaysLeft < 0.02 && runtimeData.noticeStatus == OrdealNotice.Imminent)
                {
                    runtimeData.noticeStatus = OrdealNotice.DoNotNotify;

                    string message = "The " + runtimeData.currentEvent.currentWave.variant.FirstPart() + " of " + runtimeData.currentEvent.currentWave.variant.SecondPart() + " is waning.";
                    sapi.BroadcastMessageToAllGroups(message, EnumChatType.Notification);
                }

                //// TODO: forcibly end ordeal after time limit expires
                //if (activeDaysLeft < 0)
                //{
                //    data.isOrdealActive = false;
                //    data.ordealDayNotify = 99;
                //    prepareNextOrdeal();

                //    serverChannel.BroadcastPacket(data);

                //    var list = (api.World as IServerWorldAccessor).LoadedEntities.Values;
                //    foreach (var e in list)
                //    {
                //         sapi.World.DespawnEntity(e, new EntityDespawnData() { Reason = EnumDespawnReason.Expire });
                //    }
                //}
            }
        }


        // pops the event stack and begins the event
        private void BeginOrdealEvent()
        {
            if (runtimeData.currentEvent != null)
            {
                sapi.BroadcastMessageToAllGroups("Error: tried to start Ordeal, but another Ordeal is currently active.", EnumChatType.Notification);
                return;
            }

            if (runtimeData.eventStack.Count == 0)
            {
                sapi.BroadcastMessageToAllGroups("Error: tried to start " + runtimeData.PeekEvent().tier.GetName() + "-tier Ordeal, but none are currently queued.", EnumChatType.Notification);
                return;
            }

            if (runtimeData.currentEvent.PeekWave() == null)
            {
                sapi.BroadcastMessageToAllGroups("Error: tried to start " + runtimeData.PeekEvent().tier.GetName() + "-tier Ordeal, but another Ordeal is currently active.", EnumChatType.Notification);
                return;
            }

            // update runtime variables
            runtimeData.currentEvent = runtimeData.PopEvent();
            sOrdealEventChannel.BroadcastPacket(runtimeData);

            // show start splash/sound
            sShowSplashChannel.BroadcastPacket(new OrdealVariantTime
            {
                variant = runtimeData.currentEvent.currentWave.variant,
                startOrEnd = false
            });

            string message = "The " + runtimeData.currentEvent.currentWave.variant.FirstPart() + " of " + runtimeData.currentEvent.currentWave.variant.SecondPart() + " begins...";
            sapi.BroadcastMessageToAllGroups(message, EnumChatType.Notification);

            BeginWave(runtimeData.currentEvent);
        }


        private void BeginWave(OrdealEvent ordealEvent)
        {
            if (ordealEvent.waveStack.Count == 0)
            {
                sapi.BroadcastMessageToAllGroups("Error: tried to start wave for " + ordealEvent.tier.GetName() + "-tier Ordeal, but the Ordeal has no waves queued.", EnumChatType.Notification);
                return;
            }

            ordealEvent.currentWave = ordealEvent.PopWave();
            sOrdealEventChannel.BroadcastPacket(runtimeData);

            OrdealVariant variant = ordealEvent.currentWave.variant;
            OrdealTier tier = ordealEvent.tier;

            if (!OrdealData.entityTypes.ContainsKey(variant))
            {
                sapi.BroadcastMessageToAllGroups("Error: tried to spawn mobs for " + tier.GetName() + "-tier Ordeal Wave of Variant '" + variant.GetName() + "', but this variant is not yet implemented.", EnumChatType.Notification);
                ordealEvent.currentWave = null;
                sOrdealEventChannel.BroadcastPacket(runtimeData);
                return;
            }

            // get ordeal settings for correct variant
            OrdealStrength strength = OrdealData.tierStrengths[ordealEvent.tier];
            WaveSpawnSettings spawnSettings = ordealEvent.currentWave.spawnSettings; 
            CollisionTester collisionTester = new CollisionTester();

            // spawn mobs
            foreach (IServerPlayer player in sapi.Server.Players)
            {
                if (player.ConnectionState != EnumClientState.Playing) // only spawn wave for active players
                    continue;

                Vec3d playerPos = player.Entity.ServerPos.XYZ;

                // TODO: adjust size by ordeal strength/variants
                // TODO: ensure group members spawn near each other

                var spawnedEntities = new List<Entity>();
                for (int i = 0; i < spawnSettings.numGroups; i++)
                {
                    int groupSize = sapi.World.Rand.Next(spawnSettings.minGroupSize, spawnSettings.maxGroupSize);
                    var entityType = OrdealData.entityTypes[variant];

                    int tries = 15;
                    int numSpawned = 0;
                    
                    while (tries-- > 0 && numSpawned < groupSize)
                    {
                        int spawnX = api.World.Rand.Next(-spawnSettings.spawnRange, spawnSettings.spawnRange);
                        int spawnY = api.World.Rand.Next(-spawnSettings.spawnRange, spawnSettings.spawnRange);
                        int spawnZ = api.World.Rand.Next(-spawnSettings.spawnRange, spawnSettings.spawnRange);

                        Vec3d spawnPos = new Vec3d((int)playerPos.X + spawnX + 0.5, (int)playerPos.Y + spawnY + 0.001, (int)playerPos.Z + spawnZ + 0.5);
                        BlockPos worldPos = new BlockPos((int)spawnPos.X, (int)spawnPos.Y, (int)spawnPos.Z);

                        while (api.World.BlockAccessor.GetBlock(worldPos.X, worldPos.Y - 1, worldPos.Z).Id == 0 && spawnPos.Y > 0)
                        {
                            worldPos.Y--;
                            spawnPos.Y--;
                        }

                        if (!api.World.BlockAccessor.IsValidPos((int)spawnPos.X, (int)spawnPos.Y, (int)spawnPos.Z))
                            continue;

                        Cuboidf collisionBox = entityType.SpawnCollisionBox.OmniNotDownGrowBy(0.1f);
                        if (collisionTester.IsColliding(api.World.BlockAccessor, collisionBox, spawnPos, false))
                            continue;

                        // api.Logger.Warning("Attempting to spawn entity " + entityType.Code.GetName() + " at location " + spawnPos.ToString());
                        spawnedEntities.Add(DoSpawn(entityType, spawnPos, 0));
                        numSpawned++;
                    }
                    
                }

                ordealEvent.activeGroups.Add(player, spawnedEntities);
            }
        }


        private Entity DoSpawn(EntityProperties entityType, Vec3d spawnPos, long herdid)
        {
            Entity entity = api.ClassRegistry.CreateEntity(entityType);

            EntityAgent agent = entity as EntityAgent;
            if (agent != null) 
                agent.HerdId = herdid;

            entity.ServerPos.SetPos(spawnPos);
            entity.ServerPos.SetYaw((float)api.World.Rand.NextDouble() * GameMath.TWOPI);
            entity.Pos.SetFrom(entity.ServerPos);
            entity.PositionBeforeFalling.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);

            api.World.SpawnEntity(entity);

            return entity;
        }


        // TODO: create a queue for splashes invoked while another is currently playing
        private void OnShowSplash(OrdealVariantTime variantTime)
        {
            string variantName = variantTime.variant.GetName();
            string time = variantTime.startOrEnd == false ? "start" : "end";

            if (variantName == null)
            {
                api.World.Logger.Error("Tried to show splash screen, but invalid Ordeal Variant was provided");
                return;
            }

            // show splash screen
            splash.SetSplashImage(new AssetLocation("ordeals", "textures/splashes/" + variantName.ToLower() + time + ".png"));
            splash.isActive = true;

            // play event sound
            api.World.PlaySoundAt(new AssetLocation("ordeals", "sounds/event/" + variantTime.variant.SecondPart().ToLower() + time + ".ogg"), capi.World.Player, null, false, 5, 1);
        }


        private void EndWave()
        {

        }


        private void EndOrdeal()
        {
            OrdealVariant variant = runtimeData.currentEvent.currentWave.variant;

            // show end splash/sound
            sShowSplashChannel.BroadcastPacket(new OrdealVariantTime
            {
                variant = variant,
                startOrEnd = true
            }, sapi.Server.Players);

            string message = "The " + runtimeData.currentEvent.currentWave.variant.FirstPart() + " of " + runtimeData.currentEvent.currentWave.variant.SecondPart() + " has concluded...";
            sapi.BroadcastMessageToAllGroups(message, EnumChatType.Notification);

            // update runtime data
            runtimeData.currentEvent = null;
            sOrdealEventChannel.BroadcastPacket(runtimeData);

            PrepareNextOrdeal();
        }


        // only call when an ordeal event has ended or event stack is empty
        private void PrepareNextOrdeal()
        {
            // TODO: calculate next ordeal variant based on ordealTier
            //       broadcast message when last ordeal of the day has ended
            //       broadcast message if there are still ordeals left in the day
            if (config == null)
            {
                sapi.Logger.Notification("configs are null");
                return;
            }
                

            // advance ordeal tier if enough time has passed
            if (sapi.World.Calendar.TotalDays / config.ordealTierIncreaseFrequency >= (double)runtimeData.nextOrdealTier && (int)runtimeData.nextOrdealTier < Enum.GetNames(typeof(OrdealTier)).Count() - 1)
                runtimeData.nextOrdealTier++;
   
            // schedule next ordeal day on the next multiple of ordealFrequency
            runtimeData.nextStartTotalDays = Math.Ceiling(sapi.World.Calendar.TotalDays / config.ordealFrequency) * config.ordealFrequency;
            runtimeData.PushEvent(new OrdealEvent(runtimeData.nextOrdealTier, runtimeData.nextStartTotalDays));

            double daysLeft = runtimeData.nextStartTotalDays - api.World.Calendar.TotalDays;
            string message = "In " + daysLeft + " days: " + runtimeData.nextOrdealTier + "-tier Ordeal in " + daysLeft + " days.";

            sapi.BroadcastMessageToAllGroups(message, EnumChatType.Notification);

            sOrdealEventChannel.BroadcastPacket(runtimeData);
        }


        private void LoadEntities()
        {
            // TODO: add additional entities as they're made
            OrdealData.entityTypes = new Dictionary<OrdealVariant, EntityProperties>()
            {
                { OrdealVariant.DawnGreen, sapi.World.GetEntityType(new AssetLocation("ordeals:entitydawngreen")) }
            };
        }


        // TODO: make this read from a config file
        private void LoadConfigs()
        {
            OrdealData.configs = new Dictionary<string, OrdealEventConfig>()
            {
                {
                    "default", new OrdealEventConfig()
                    {
                        ordealFrequency = sapi.World.Calendar.DaysPerMonth,                 // default ordeals to occur at the end of every month
                        ordealTierIncreaseFrequency = sapi.World.Calendar.DaysPerMonth * 3, // increase ordeal tier every 3 months
                        indigoOrdealFrequency = sapi.World.Calendar.DaysPerMonth * 6,       // indigo ordeals occur every 6 months
                        ordealTimeLimit = 1f                                                // ordeals forcibly end after 1 day
                    }
                }
            };

            // TODO: update this to be set via config file to choose from presets
            config = OrdealData.configs["default"];
        }


        private void RegisterCommands()
        {
            api.World.Logger.Notification("Ordeals: Begin loading commands...");

            CommandArgumentParsers parsers = sapi.ChatCommands.Parsers;

            sapi.ChatCommands
                .GetOrCreate("ordeals")
                .IgnoreAdditionalArgs()
                .RequiresPrivilege("worldedit")
                .WithDescription("Ordeals mod commands.")

                .BeginSubCommand("next")
                    .WithDescription("Tells you the amount of days until the next Ordeal.")
                    .HandleWith(OnCmdShowNextOrdeal)
                .EndSubCommand()

                .BeginSubCommand("list")
                    .WithDescription("Tells you the upcoming list of Ordeals.")
                    .HandleWith(OnCmdShowSchedule)
                .EndSubCommand()

                .BeginSubCommand("begin")
                    .WithDescription("Starts a new Ordeal of variant OrdealVariant.")
                    .WithArgs(parsers.WordRange("OrdealVariant",
                                                "DawnAmber", "DawnCrimson", "DawnGreen", "DawnViolet", "DawnWhite",
                                                                "NoonCrimson", "NoonGreen", "NoonViolet", "NoonWhite",
                                                "DuskAmber", "DuskCrimson", "DuskGreen", "DuskWhite",
                                                "MidnightAmber", "MidnightGreen", "MidnightViolet", "MidnightWhite",
                                                "NightIndigo"))
                    .HandleWith(OnCmdBeginWave)
                .EndSubCommand()

                .BeginSubCommand("schedule")
                    .WithDescription("Schedules a new Ordeal of tier OrdealTier and time DaysFromNow &gt; 0.")
                    .WithArgs(parsers.WordRange("OrdealTier",
                                                "Malkuth",
                                                "Yesod",
                                                "Hod",
                                                "Netzach",
                                                "Tiphereth",
                                                "Gebura",
                                                "Chesed",
                                                "Binah",
                                                "Hokma",
                                                "Keter")
                    , parsers.Double("DaysFromNow"))
                    .HandleWith(OnCmdScheduleOrdeal)
                .EndSubCommand()

                .BeginSubCommand("clear")
                    .WithDescription("Clears list of scheduled Ordeals")
                .EndSubCommand()

                .BeginSubCommand("end")
                    .WithDescription("Ends current Ordeal if one is active.")
                    .HandleWith(OnCmdEndOrdeal)
                .EndSubCommand()
                ;

            // additional commands for starting new ordeal, ending current ordeal, scheduling a new ordeal, rescheduling next upcoming ordeal

            api.World.Logger.Notification("Ordeals: Commands successfully registered.");
        }


        private TextCommandResult OnCmdShowNextOrdeal(TextCommandCallingArgs args)
        {
            string message = "";
            if (runtimeData.eventStack.Count == 0)
                message = "There are no Ordeals currently scheduled.";
            else if (runtimeData.currentEvent != null)
                message = "A " + runtimeData.currentEvent.tier.GetName() + "-tier Ordeal is currently active with " + runtimeData.currentEvent.waveStack.Count + " waves remaining.";
            else
            {
                double daysLeft = runtimeData.PeekEvent().startTimeTotalDays - api.World.Calendar.TotalDays;
                message = "In " + daysLeft + " days: " + runtimeData.PeekEvent().tier.GetName() + "-tier Ordeal with " + runtimeData.PeekEvent().waveStack.Count + " waves.";
            }

            sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);

            return TextCommandResult.Success();
        }


        private TextCommandResult OnCmdShowSchedule(TextCommandCallingArgs args)
        {
            string message = "";
            if (runtimeData.currentEvent == null && runtimeData.eventStack.Count == 0)
                message = "There are no Ordeals currently scheduled.";

            if (runtimeData.currentEvent != null)
            {
                message = "A " + runtimeData.currentEvent.tier.GetName() + "-tier Ordeal is currently active with " + runtimeData.currentEvent.waveStack.Count + " waves remaining.";
                sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);
            }

            message = "Upcoming Ordeals: ";
            sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);

            if (runtimeData.eventStack.Count > 0)
            {
                for (int i = 0; i < runtimeData.eventStack.Count; i++)
                {
                    double daysLeft = runtimeData.eventStack[i].startTimeTotalDays - api.World.Calendar.TotalDays;
                    message = "In " + daysLeft + " days: " + Enum.GetName(typeof(OrdealTier), runtimeData.eventStack[i].tier) + "-tier Ordeal of " + runtimeData.eventStack[i].waveStack.Count + " waves.";

                    sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);
                }
            }

            return TextCommandResult.Success();
        }


        private TextCommandResult OnCmdBeginOrdeal(TextCommandCallingArgs args)
        {
            // create a new ordealEvent and set currentEvent to it
            // TODO: input validation using tryParse

            // string message = "";

            if (runtimeData.currentEvent != null)
            {
                //message = "Error: cannot begin a new Ordeal while another Ordeal is currently active.";
                //sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);
                return TextCommandResult.Error("Error: cannot begin a new Ordeal while another Ordeal is currently active.");
            }

            OrdealTier tier = (OrdealTier)Enum.Parse(typeof(OrdealTier), (string)args[0]);

            if (!OrdealData.tierStrengths.ContainsKey(tier))
            {
                //message = "Tried to begin " + tier.GetName() + "-tier Ordeal, but this tier is not yet implemented.";
                //sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);
                return TextCommandResult.Error("Tried to begin " + tier.GetName() + "-tier Ordeal, but this tier is not yet implemented.");
            }

            runtimeData.currentEvent = new OrdealEvent(tier, sapi.World.Calendar.ElapsedDays);

            return TextCommandResult.Success();
        }


        private TextCommandResult OnCmdBeginWave(TextCommandCallingArgs args)
        {
            string message = "";

            if (runtimeData.currentEvent != null)
            {
                message = "Error: cannot begin a new Wave while another Ordeal is currently active.";
                sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);
                return TextCommandResult.Success();
            }

            OrdealVariant variant = (OrdealVariant) Enum.Parse(typeof(OrdealVariant), (string)args[0]);
            OrdealTier tier = (OrdealTier)Enum.Parse(typeof(OrdealTier), (string)args[1]);
            
            if (!OrdealData.entityTypes.ContainsKey(variant))
            {
                message = "Tried to begin " + variant.FirstPart() + " Wave of variant '" + variant.SecondPart() + "', but this variant is not yet implemented.";
                sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);
                return TextCommandResult.Success();
            }
        
            message = "Ok, starting Ordeal Wave of Variant '" + Enum.GetName(typeof(OrdealVariant), variant) + "'";
            sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);

            runtimeData.PushEvent(new OrdealEvent(tier, sapi.World.Calendar.ElapsedDays));
            runtimeData.PeekEvent().waveStack.Add(new OrdealWave(variant, sapi.World.Calendar.ElapsedDays));
            BeginOrdealEvent();

            return TextCommandResult.Success();
        }


        private TextCommandResult OnCmdEndOrdeal(TextCommandCallingArgs args)
        {
            if (runtimeData.currentEvent == null)
            {
                sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, "Tried to end an Ordeal, but there are none currently active.", EnumChatType.Notification);
                return TextCommandResult.Success();
            }

            string message = "Ok, ending " + runtimeData.currentEvent.tier.GetName() + "-tier Ordeal.";
            sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);

            EndOrdeal();

            return TextCommandResult.Success();
        }


        // forcibly end current wave and delete all spawned mobs without drops
        private TextCommandResult OnCmdEndWave(TextCommandCallingArgs args)
        {
            return TextCommandResult.Success();
        }


        // allow for scheduling ordeals at a specified time
        // should fail if time out of range or invalid tier given
        private TextCommandResult OnCmdScheduleOrdeal(TextCommandCallingArgs args)
        {
            if (Enum.TryParse((string)args[0], true, out OrdealTier tier) == false)
                return TextCommandResult.Error("Tried to schedule " + (string)args[0] + "-tier Ordeal, but this tier doesn't exist.");

            if (!OrdealData.tierStrengths.ContainsKey(tier))
            {
                //message = "Tried to begin " + tier.GetName() + "-tier Ordeal, but this tier is not yet implemented.";
                //sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);
                return TextCommandResult.Error("Tried to schedule " + tier.GetName() + "-tier Ordeal, but this tier is not yet implemented.");
            }

            double daysFromNow = (double)args[1];
            if (daysFromNow <= 0)
                return TextCommandResult.Error("Tried to schedule an Ordeal, but DaysFromNow must be a positive number.");

            // Finally, add the event to the stack
            runtimeData.PushEvent(new OrdealEvent(tier, Math.Round(daysFromNow, 2) + sapi.World.Calendar.ElapsedDays));

            return TextCommandResult.Success();
        }


        private void Event_OnEntityDeath(Entity targetEntity, DamageSource damageSource)
        {
            if (runtimeData.currentEvent == null)
                return;

            // only want to do this on ordeal entities
            if (!(targetEntity.Code.Path.Contains("dawn") || targetEntity.Code.Path.Contains("noon") || targetEntity.Code.Path.Contains("dusk") 
                || targetEntity.Code.Path.Contains("midnight") || targetEntity.Code.Path.Contains("night")))
                return;

            List<IServerPlayer> keys = runtimeData.currentEvent.activeGroups.Keys.ToList();
            foreach (var player in keys)
            {
                List<Entity> newEntityList = new List<Entity>();
                foreach (Entity ordealEntity in runtimeData.currentEvent.activeGroups[player])
                {
                    if (ordealEntity.EntityId == targetEntity.EntityId)
                        continue;

                    newEntityList.Add(ordealEntity);
                }

                runtimeData.currentEvent.activeGroups[player] = newEntityList;

                if (runtimeData.currentEvent.activeGroups[player].Count == 0)
                {
                    runtimeData.activeWaves = runtimeData.currentEvent.activeGroups.Count - 1;
                    sOrdealEventChannel.BroadcastPacket(runtimeData);

                    string waveAmount = runtimeData.activeWaves > 0 ? runtimeData.activeWaves.ToString() : "no";
                    string message = player.PlayerName + " has cleared their wave of the Ordeal. " + waveAmount + " waves remain.";
                    sapi.BroadcastMessageToAllGroups(message, EnumChatType.Notification);

                    runtimeData.currentEvent.activeGroups.Remove(player);
                }

                if (runtimeData.currentEvent.activeGroups.Count == 0)
                    EndOrdeal();
            }
        }


        private void Event_PlayerNowPlaying(IServerPlayer byPlayer)
        {
            if (sapi.WorldManager.SaveGame.IsNew && ordealsEnabled)
            {
                double nextOrdealDaysLeft = runtimeData.nextStartTotalDays - api.World.Calendar.TotalDays;
                byPlayer.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("{0} days until the first Ordeal.", (int)nextOrdealDaysLeft), EnumChatType.Notification);
            }
        }

        private void Event_PlayerJoin(IServerPlayer byPlayer)
        {
            sOrdealEventChannel.SendPacket(runtimeData, byPlayer);
        }


        private void Event_GameWorldSave()
        {
            sapi.WorldManager.SaveGame.StoreData("ordealEventRuntimeData", SerializerUtil.Serialize(runtimeData));
        }
    }
}
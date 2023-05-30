using System;
using System.Linq;
using System.Collections.Generic;

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

        Dictionary<string, OrdealEventConfig> configs;
        Dictionary<OrdealTier, OrdealStrength> ordealStrengths;   
        Dictionary<OrdealVariant, OrdealSpawnSettings> ordealSpawnSettings;
        Dictionary<OrdealVariant, EntityProperties> ordealEntityTypes;
        Dictionary<IServerPlayer, List<Entity>> ordealActiveWaves = new Dictionary<IServerPlayer, List<Entity>>();

        OrdealSplash splash;

        OrdealEventConfig config;
        OrdealEventRuntimeData data = new OrdealEventRuntimeData();

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

            InitConfigs();
            api.Logger.StoryEvent("The unknown, the uncontainable...");
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            sapi = api;

            sOrdealEventChannel =
                api.Network.RegisterChannel("ordealevent")
               .RegisterMessageType(typeof(OrdealEventRuntimeData));

            sShowSplashChannel =
               api.Network.RegisterChannel("showsplash")
               .RegisterMessageType(typeof(OrdealVariantTime));

            api.Event.SaveGameLoaded += () =>
            {
                bool shouldPrepNextOrdeal = sapi.WorldManager.SaveGame.IsNew;

                byte[] bytedata = sapi.WorldManager.SaveGame.GetData("ordealEventData");
                if (bytedata != null)
                {
                    try
                    {
                        data = SerializerUtil.Deserialize<OrdealEventRuntimeData>(bytedata);
                    } catch (Exception)
                    {
                        api.World.Logger.Notification("Failed loading ordeal event runtime data, will initialize new data set");
                        data = new OrdealEventRuntimeData();
                        data.nextOrdealStartTotalDays = sapi.World.Calendar.DaysPerMonth;
                        shouldPrepNextOrdeal = true;
                    }
                }
                else
                {
                    data = new OrdealEventRuntimeData();
                    data.nextOrdealStartTotalDays = sapi.World.Calendar.DaysPerMonth;
                    shouldPrepNextOrdeal = true;
                }

                if (shouldPrepNextOrdeal)
                    PrepareNextOrdeal();
            };

            InitOrdealStrengths();
            InitOrdealSpawnGroups();
            LoadEntities();
            RegisterCommands();

            api.Event.GameWorldSave += Event_GameWorldSave;
            api.Event.PlayerJoin += Event_PlayerJoin;
            api.Event.PlayerNowPlaying += Event_PlayerNowPlaying;
            api.Event.OnEntityDeath += Event_OnEntityDeath;
            api.Event.RegisterGameTickListener(OnOrdealEventTick, 2000);
        }

 
        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            capi = api;

            cOrdealEventChannel =
                api.Network.RegisterChannel("ordealevent")
                .RegisterMessageType(typeof(OrdealEventRuntimeData))
                .SetMessageHandler<OrdealEventRuntimeData>(OnServerData);
            
            cShowSplashChannel =
                api.Network.RegisterChannel("showsplash")
                .RegisterMessageType(typeof(OrdealVariantTime))
                .SetMessageHandler<OrdealVariantTime>(OnShowSplash);

            splash = new OrdealSplash(capi);
        }


        private void OnServerData(OrdealEventRuntimeData data) { this.data = data; }


        private void OnOrdealEventTick(float dt)
        {
            if (config == null) return;
            if (!ordealsEnabled)
            {
                data.isOrdealActive = false;
                return;
            }

            double nextOrdealDaysLeft = data.nextOrdealStartTotalDays - api.World.Calendar.TotalDays;

            if (nextOrdealDaysLeft > 0.03 && nextOrdealDaysLeft < 0.35 && data.noticeStatus == OrdealNotice.DoNotNotify) 
            {
                data.noticeStatus = OrdealNotice.Approaching;

                string message = "The " + data.nextOrdealVariant.Peek().FirstPart() + " of " + data.nextOrdealVariant.Peek().SecondPart() + " approaches.";
                sapi.BroadcastMessageToAllGroups(message, EnumChatType.Notification);
            }

            if (nextOrdealDaysLeft <= 0.02 && (data.noticeStatus == OrdealNotice.DoNotNotify || data.noticeStatus == OrdealNotice.Approaching))
            {
                data.noticeStatus = OrdealNotice.Imminent;

                string message = "The " + data.nextOrdealVariant.Peek().FirstPart() + " of " + data.nextOrdealVariant.Peek().SecondPart() + " is imminent.";
                sapi.BroadcastMessageToAllGroups(message, EnumChatType.Notification);
            }

            if (nextOrdealDaysLeft <= 0)
            {
                // Make sure ordeal still ends when time is fast forwarded
                if (!data.isOrdealActive && nextOrdealDaysLeft + data.ordealTimeLimit < 0)
                {
                    EndOrdeal();
                    return;
                }

                if (!data.isOrdealActive)
                {
                    //data.ordealActiveTotalDays = api.World.Calendar.TotalDays + stormActiveDays;
                    //if (data.ordealTier == EnumTempStormStrength.Medium) 
                    //    data.stormGlitchStrength = 0.67f + (float)api.World.Rand.NextDouble() / 10;
                    //if (data.ordealTier == EnumTempStormStrength.Heavy) 
                    //    data.stormGlitchStrength = 0.9f + (float)api.World.Rand.NextDouble() / 10;
                    data.currentOrdealVariant = data.nextOrdealVariant.Pop();
                    BeginOrdeal(data.currentOrdealVariant);
                }

                double activeDaysLeft = data.ordealTimeLimit - api.World.Calendar.TotalDays;
                if (activeDaysLeft < 0.02 && data.noticeStatus == OrdealNotice.Imminent)
                {
                    data.noticeStatus = OrdealNotice.DoNotNotify;

                    string message = "The " + data.nextOrdealVariant.Peek().FirstPart() + " of " + data.nextOrdealVariant.Peek().SecondPart() + " is waning.";
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


        private void BeginOrdeal(OrdealVariant variant)
        {
            if (!ordealEntityTypes.ContainsKey(variant))
            {
                sapi.BroadcastMessageToAllGroups("Error, tried to start Ordeal of variant '" + Enum.GetName(typeof(OrdealVariant), variant) + 
                    "', but this variant is not yet implemented.", EnumChatType.Notification);
                return;
            }

            if (data.nextOrdealVariant.Count == 0)
            {
                sapi.BroadcastMessageToAllGroups("Error, tried to start Ordeal, but none are currently queued.", EnumChatType.Notification);
                return;
            }

            // update runtime variables
            data.isOrdealActive = true;
            sOrdealEventChannel.BroadcastPacket(data);

            // show start splash/sound
            sShowSplashChannel.BroadcastPacket(new OrdealVariantTime
            {
                variant = variant,
                startOrEnd = false
            });

            string message = "The " + data.currentOrdealVariant.FirstPart() + " of " + data.currentOrdealVariant.SecondPart() + " begins...";
            sapi.BroadcastMessageToAllGroups(message, EnumChatType.Notification);

            SpawnWave(variant);
        }


        private void SpawnWave(OrdealVariant variant)
        {
            if (!ordealEntityTypes.ContainsKey(variant))
            {
                sapi.BroadcastMessageToAllGroups("Error, tried to spawn wave for " + variant.FirstPart() + "Ordeal of variant '" + variant.SecondPart() + 
                    "', but this variant is not yet implemented.", EnumChatType.Notification);
                return;
            }

            // get ordeal settings for correct variant
            OrdealStrength strength = ordealStrengths[data.currentOrdealTier];
            OrdealSpawnSettings spawnSettings = ordealSpawnSettings[variant];
            CollisionTester collisionTester = new CollisionTester();

            // spawn mobs
            foreach (IServerPlayer player in sapi.Server.Players)
            {
                if (player.ConnectionState != EnumClientState.Playing) // only spawn wave for active players
                    continue;

                Vec3d playerPos = player.Entity.ServerPos.XYZ;

                // TODO: adjust size by ordeal strength/variants
                // TODO: ensure groups spawn near each other

                var spawnedEntities = new List<Entity>();
                for (int i = 0; i < spawnSettings.numGroups; i++)
                {
                    int groupSize = sapi.World.Rand.Next(spawnSettings.minGroupSize, spawnSettings.maxGroupSize);
                    var entityType = ordealEntityTypes[variant];

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

                ordealActiveWaves.Add(player, spawnedEntities);
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
            string variantName = Enum.GetName(typeof(OrdealVariant), variantTime.variant);
            string time = variantTime.startOrEnd == false ? "start" : "end";

            // show splash screen
            splash.SetSplashImage(new AssetLocation("ordeals", "textures/splashes/" + variantName.ToLower() + time + ".png"));
            splash.isActive = true;

            // play event sound
            api.World.PlaySoundAt(new AssetLocation("ordeals", "sounds/event/" + variantTime.variant.SecondPart().ToLower() + time + ".ogg"), capi.World.Player, null, false, 5, 1);
        }


        private void EndOrdeal()
        {
            OrdealVariant variant = data.currentOrdealVariant;

            // show end splash/sound
            sShowSplashChannel.BroadcastPacket(new OrdealVariantTime
            {
                variant = variant,
                startOrEnd = true
            }, sapi.Server.Players);

            string message = "The " + data.currentOrdealVariant.FirstPart() + " of " + data.currentOrdealVariant.SecondPart() + " has concluded...";
            sapi.BroadcastMessageToAllGroups(message, EnumChatType.Notification);

            // update runtime data
            data.isOrdealActive = false;
            data.currentOrdealVariant = OrdealVariant.Inactive;
            data.ordealTimeLimit = 0;
            sOrdealEventChannel.BroadcastPacket(data);

            PrepareNextOrdeal();
        }


        private void PrepareNextOrdeal()
        {
            // TODO: calculate next ordeal variant based on ordealTier
            //       broadcast message when last ordeal of the day has ended
            //       broadcast message if there are still ordeals left in the day
            if (config == null) 
                return;

            // advance ordeal tier if enough time has passed
            if (sapi.World.Calendar.TotalDays / config.ordealTierIncreaseFrequency >= (double)data.currentOrdealTier
                && (int)data.currentOrdealTier < Enum.GetNames(typeof(OrdealTier)).Count())
            {
                data.currentOrdealTier = data.nextOrdealTier;
                data.nextOrdealTier = data.currentOrdealTier++;
            }

            // queue up next round of ordeals
            if (data.nextOrdealVariant.Count == 0)  
            {
                if (ordealStrengths[data.currentOrdealTier].midnight > 0)
                    data.nextOrdealVariant.Push(OrdealVariantUtil.GetMidnightVariant());
                if (ordealStrengths[data.currentOrdealTier].dusk > 0)
                    data.nextOrdealVariant.Push(OrdealVariantUtil.GetDuskVariant());
                if (ordealStrengths[data.currentOrdealTier].noon > 0)
                    data.nextOrdealVariant.Push(OrdealVariantUtil.GetNoonVariant());
                if (ordealStrengths[data.currentOrdealTier].dawn > 0)
                    data.nextOrdealVariant.Push(OrdealVariantUtil.GetDawnVariant());

                // schedule next ordeal day on the next multiple of ordealFrequency
                data.nextOrdealStartTotalDays = Math.Ceiling(sapi.World.Calendar.TotalDays / config.ordealFrequency) * config.ordealFrequency;
            }
            else // ordeals still left in the day, schedule next ordeal at the next quarter of the day
                data.nextOrdealStartTotalDays = Math.Truncate(4 * (sapi.World.Calendar.TotalDays + 0.25)) / 4; 

            sOrdealEventChannel.BroadcastPacket(data);
        }


        private void LoadEntities()
        {
            // TODO: add additional entities as they're made
            ordealEntityTypes = new Dictionary<OrdealVariant, EntityProperties>()
            {
                { OrdealVariant.DawnGreen, sapi.World.GetEntityType(new AssetLocation("ordeals:entitydawngreen")) } 
            };
        }


        // TODO: should probably replace these with spreadsheets or something...
        private void InitOrdealSpawnGroups()
        {
            // TODO: add additional entities as they're made
            ordealSpawnSettings = new Dictionary<OrdealVariant, OrdealSpawnSettings>()
            {
                { OrdealVariant.DawnGreen, new OrdealSpawnSettings { numGroups = 3 } }
            };
        }


        // TODO: make these a range of strengths rather than single values
        //       would also need to update code for determining effects of strength on enemies
        private void InitOrdealStrengths()
        {
            ordealStrengths = new Dictionary<OrdealTier, OrdealStrength>()
            {
                { OrdealTier.Malkuth, new OrdealStrength()      { dawn = 1 } },
                { OrdealTier.Yesod, new OrdealStrength()        { dawn = 2 } },
                { OrdealTier.Hod, new OrdealStrength()          { dawn = 2, noon = 1 } },
                { OrdealTier.Netzach, new OrdealStrength()      { dawn = 3, noon = 2 } },
                { OrdealTier.Tiphereth, new OrdealStrength()    { dawn = 4, noon = 2, dusk = 1 } },
                { OrdealTier.Gebura, new OrdealStrength()       { dawn = 4, noon = 3, dusk = 2 } },
                { OrdealTier.Chesed, new OrdealStrength()       { dawn = 5, noon = 3, dusk = 3 } },
                { OrdealTier.Binah, new OrdealStrength()        { dawn = 6, noon = 4, dusk = 4, midnight = 1 } },
                { OrdealTier.Hokma, new OrdealStrength()        { dawn = 6, noon = 5, dusk = 5, midnight = 2 } }
            };
        }


        // TODO: make this read from a config file
        private void InitConfigs()
        {
            configs = new Dictionary<string, OrdealEventConfig>()
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
            config = configs["default"];
        }


        private void RegisterCommands()
        {
            CommandArgumentParsers parsers = sapi.ChatCommands.Parsers;

            sapi.ChatCommands
                .GetOrCreate("ordeals")
                .IgnoreAdditionalArgs()
                .RequiresPrivilege("worldedit")
                .WithDescription("Ordeals mod commands.")

                .BeginSubCommand("nextOrdeal")
                    .WithDescription("Tells you the amount of days until the next Ordeal.")
                    .HandleWith(OnCmdNextOrdeal)
                .EndSubCommand()

                .BeginSubCommand("beginOrdeal")
                    .WithDescription("Starts a new ordeal of type ordealVariant")
                    .WithArgs(parsers.WordRange("OrdealVariant",
                                                "DawnAmber",    "DawnCrimson",  "DawnGreen",        "DawnViolet",       "DawnWhite",
                                                                "NoonCrimson",  "NoonGreen",        "NoonViolet",       "NoonWhite",
                                                "DuskAmber",    "DuskCrimson",  "DuskGreen",                            "DuskWhite",
                                                "MidnightAmber",                "MidnightGreen",    "MidnightViolet",   "MidnightWhite",
                                                "NightIndigo"))
                    .HandleWith(OnCmdBeginOrdeal)
                .EndSubCommand()

                .BeginSubCommand("endOrdeal")
                    .WithDescription("Ends current Ordeal if one is active.")
                    .HandleWith(OnCmdEndOrdeal)
                .EndSubCommand()
                ;

                // additional commands for starting new ordeal, ending current ordeal, scheduling a new ordeal, rescheduling next upcoming ordeal
        }


        private TextCommandResult OnCmdNextOrdeal(TextCommandCallingArgs args)
        {
            string message = "";
            if (data.isOrdealActive)
                message = "There is currently an active Ordeal.";
            else
            {
                double nextOrdealDaysLeft = data.nextOrdealStartTotalDays - api.World.Calendar.TotalDays;
                message = "A " + data.nextOrdealVariant.Peek().FirstPart() + " of " + data.nextOrdealVariant.Peek().SecondPart() + 
                    " will be arriving in about " + nextOrdealDaysLeft + " days.";
            }

            sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);

            return TextCommandResult.Success();
        }


        private TextCommandResult OnCmdBeginOrdeal(TextCommandCallingArgs args)
        {
            OrdealVariant variant = (OrdealVariant) Enum.Parse(typeof(OrdealVariant), (string)args[0]);
            string message = "";

            if (!ordealEntityTypes.ContainsKey(variant))
            {
                message = "Tried to begin " + variant.FirstPart() + "Ordeal of variant '" + variant.SecondPart() +
                    "', but this variant is not yet implemented.";
                sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);
                return TextCommandResult.Success();
            }

            data.currentOrdealVariant = variant;

            message = "Ok, starting Ordeal of variant '" + Enum.GetName(typeof(OrdealVariant), variant) + "'";
            sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);

            data.nextOrdealVariant.Push(variant);
            BeginOrdeal(variant);

            return TextCommandResult.Success();
        }


        private TextCommandResult OnCmdEndOrdeal(TextCommandCallingArgs args)
        {
            if (!data.isOrdealActive)
            {
                sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, "Tried to end Ordeal, but there is none currently active.", EnumChatType.Notification);
                return TextCommandResult.Success();
            }

            string message = "Ok, ending " + data.currentOrdealVariant.FirstPart() + " Ordeal of variant '" + data.currentOrdealVariant.SecondPart() + "'";
            sapi.SendMessage(args.Caller.Player, args.Caller.FromChatGroupId, message, EnumChatType.Notification);

            EndOrdeal();

            return TextCommandResult.Success();
        }


        private void Event_OnEntityDeath(Entity targetEntity, DamageSource damageSource)
        {
            // only want to do this on ordeal entities
            if (!(targetEntity.Code.Path.Contains("dawn") || targetEntity.Code.Path.Contains("noon") || targetEntity.Code.Path.Contains("dusk") 
                || targetEntity.Code.Path.Contains("midnight") || targetEntity.Code.Path.Contains("night")))
                return;

            List<IServerPlayer> keys = ordealActiveWaves.Keys.ToList();
            foreach (var player in keys)
            {
                List<Entity> newEntityList = new List<Entity>();
                foreach (Entity ordealEntity in ordealActiveWaves[player])
                {
                    if (ordealEntity.EntityId == targetEntity.EntityId)
                        continue;

                    newEntityList.Add(ordealEntity);
                }

                ordealActiveWaves[player] = newEntityList;

                if (ordealActiveWaves[player].Count == 0)
                {
                    data.activeWaves = ordealActiveWaves.Count - 1;
                    sOrdealEventChannel.BroadcastPacket(data);

                    string waveAmount = data.activeWaves > 0 ? data.activeWaves.ToString() : "no";
                    string message = player.PlayerName + " has cleared their wave of the Ordeal. " + waveAmount + " waves remain.";
                    sapi.BroadcastMessageToAllGroups(message, EnumChatType.Notification);

                    ordealActiveWaves.Remove(player);
                }

                if (ordealActiveWaves.Count == 0)
                    EndOrdeal();
            }
        }


        private void Event_PlayerNowPlaying(IServerPlayer byPlayer)
        {
            if (sapi.WorldManager.SaveGame.IsNew && ordealsEnabled)
            {
                double nextOrdealDaysLeft = data.nextOrdealStartTotalDays - api.World.Calendar.TotalDays;
                byPlayer.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("{0} days until the first Ordeal.", (int)nextOrdealDaysLeft), EnumChatType.Notification);
            }
        }

        private void Event_PlayerJoin(IServerPlayer byPlayer)
        {
            sOrdealEventChannel.SendPacket(data, byPlayer);
        }


        private void Event_GameWorldSave()
        {
            sapi.WorldManager.SaveGame.StoreData("ordealEventData", SerializerUtil.Serialize(data));
        }
    }
}
using System;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;

using ProtoBuf;


namespace ordeals.src
{
    /*
     * TODO:
     * - implement enable/disable ordeals (preferably in a way that can be set in-game)
     * - delay ordeals
     * - correct time settings for ordeal scheduling
     * - time limit settings to forcibly end ordeal
     * 
     * - all additional ordeal variants
     * 
     * - individual ordeals for each player
     * 
     */


    class OrdealsMain : ModSystem
    {
        ICoreAPI api;
        ICoreServerAPI sapi;

        IServerNetworkChannel serverChannel;
        IClientNetworkChannel clientChannel;

        Dictionary<string, OrdealEventConfig> configs;
        Dictionary<OrdealTier, OrdealStrength> ordealStrengths;
        Dictionary<OrdealVariant, OrdealEventText> texts;       
        Dictionary<OrdealVariant, OrdealSpawnSettings> ordealSpawnSettings;
        Dictionary<OrdealVariant, EntityProperties> ordealEntityTypes;

        OrdealEventConfig config;
        OrdealEventRuntimeData data = new OrdealEventRuntimeData();

        bool ordealsEnabled;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;

            // api.Logger.Warning("Loading Ordeals mod...");
            api.Logger.StoryEvent("Loading Ordeals mod...");
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            sapi = api;

            serverChannel =
                api.Network.RegisterChannel("ordealevent")
               .RegisterMessageType(typeof(OrdealEventRuntimeData));

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
                        data.nextOrdealTotalDays = sapi.World.Calendar.DaysPerMonth;
                        shouldPrepNextOrdeal = true;
                    }
                }
                else
                {
                    data = new OrdealEventRuntimeData();
                    data.nextOrdealTotalDays = sapi.World.Calendar.DaysPerMonth;
                    shouldPrepNextOrdeal = true;
                }

                if (shouldPrepNextOrdeal)
                    prepareNextOrdeal();
            };

            initTexts();
            // initConfigs();
            initOrdealStrengths();
            initOrdealSpawnGroups();
            loadEntities();
            registerCommands();

            api.Event.GameWorldSave += Event_GameWorldSave;
            api.Event.PlayerJoin += Event_PlayerJoin;
            api.Event.PlayerNowPlaying += Event_PlayerNowPlaying;
            api.Event.OnEntityDeath += Event_OnEntityDeath;
            // api.Event.RegisterGameTickListener(onOrdealEventTick, 2000); // TODO: uncomment to test actual event
        }


        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

            clientChannel =
                api.Network.RegisterChannel("ordealevent")
                .RegisterMessageType(typeof(OrdealEventRuntimeData))
                .SetMessageHandler<OrdealEventRuntimeData>(onServerData);
        }


        private void onServerData(OrdealEventRuntimeData data) { this.data = data; }


        private void onOrdealEventTick(float dt)
        {
            if (config == null) return;
            if (!ordealsEnabled)
            {
                data.isOrdealActive = false;
                return;
            }


            double nextOrdealDaysLeft = data.nextOrdealTotalDays - api.World.Calendar.TotalDays;

            if (nextOrdealDaysLeft > 0.03 && nextOrdealDaysLeft < 0.35 && data.ordealDayNotify > 1)
            {
                data.ordealDayNotify = 1;
                sapi.BroadcastMessageToAllGroups(texts[data.nextOrdealVariant].approaching, EnumChatType.Notification);
            }

            if (nextOrdealDaysLeft <= 0.02 && data.ordealDayNotify > 0)
            {
                data.ordealDayNotify = 0;
                sapi.BroadcastMessageToAllGroups(texts[data.nextOrdealVariant].imminent, EnumChatType.Notification);
            }

            if (nextOrdealDaysLeft <= 0)
            {
                //double stormActiveDays = (0.1f + data.nextOrdealVariant * 0.1f) * tempstormDurationMul;

                // Make sure it still happens when time is fast forwarded
                //if (!data.isOrdealActive && nextOrdealDaysLeft + stormActiveDays < 0)
                //{
                //    prepareNextOrdeal();
                //    serverChannel.BroadcastPacket(data);
                //    return;
                //}

                if (!data.isOrdealActive)
                {
                    //data.ordealActiveTotalDays = api.World.Calendar.TotalDays + stormActiveDays;
                    //if (data.ordealTier == EnumTempStormStrength.Medium) 
                    //    data.stormGlitchStrength = 0.67f + (float)api.World.Rand.NextDouble() / 10;
                    //if (data.ordealTier == EnumTempStormStrength.Heavy) 
                    //    data.stormGlitchStrength = 0.9f + (float)api.World.Rand.NextDouble() / 10;
                    data.isOrdealActive = true;

                    serverChannel.BroadcastPacket(data);
                }

                //double activeDaysLeft = data.ordealActiveTotalDays - api.World.Calendar.TotalDays;
                //if (activeDaysLeft < 0.02 && data.ordealDayNotify == 0)
                //{
                //    data.ordealDayNotify = -1;
                //    //sapi.BroadcastMessageToAllGroups(texts[data.ordealTier].Waning, EnumChatType.Notification);
                //}


                //// TODO: replace this condition to end the event when all spawned entities die
                //if (activeDaysLeft < 0)
                //{
                //    data.isOrdealActive = false;
                //    data.ordealDayNotify = 99;
                //    prepareNextOrdeal();

                //    serverChannel.BroadcastPacket(data);

                //    var list = (api.World as IServerWorldAccessor).LoadedEntities.Values;
                //    foreach (var e in list)
                //    {
                //        if (e.Code.Path.Contains("drifter"))
                //        {
                //            e.Attributes.RemoveAttribute("ignoreDaylightFlee");

                //            if (api.World.Rand.NextDouble() < 0.5)
                //            {
                //                sapi.World.DespawnEntity(e, new EntityDespawnData() { Reason = EnumDespawnReason.Expire });
                //            }
                //        }
                //    }
                //}
            }
        }


        private void beginOrdeal(OrdealVariant variant)
        {
            // show splash screen
            sapi.BroadcastMessageToAllGroups("Beginning ordeal...", EnumChatType.Notification);

            OrdealStrength strength = ordealStrengths[(OrdealTier) data.currentOrdealTier];
            OrdealSpawnSettings spawnSettings = ordealSpawnSettings[variant];

            CollisionTester collisionTester = new CollisionTester();

            // spawn mobs
            foreach (IPlayer player in api.World.AllOnlinePlayers)
            {
                Vec3d playerPos = player.Entity.ServerPos.XYZ;

                // TODO: adjust size by ordeal strength/variants
                // TODO: ensure groups spawn near each other
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

                        api.Logger.Warning("Attempting to spawn entity " + entityType.Code.GetName() + " at location " + spawnPos.ToString());
                        DoSpawn(entityType, spawnPos, 0);
                        numSpawned++;
                    }
                }
            }
        }


        private void DoSpawn(EntityProperties entityType, Vec3d spawnPos, long herdid)
        {
            Entity entity = api.ClassRegistry.CreateEntity(entityType);

            EntityAgent agent = entity as EntityAgent;
            if (agent != null) 
                agent.HerdId = herdid;

            entity.ServerPos.SetPos(spawnPos);
            entity.ServerPos.SetYaw((float)api.World.Rand.NextDouble() * GameMath.TWOPI);
            entity.Pos.SetFrom(entity.ServerPos);
            entity.PositionBeforeFalling.Set(entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z);

            //entity.Attributes.SetString("origin", "timedistortion");

            api.World.SpawnEntity(entity);

            //entity.WatchedAttributes.SetDouble("temporalStability", GameMath.Clamp((1 - 1.5f * StormStrength), 0, 1));
            //entity.Attributes.SetBool("ignoreDaylightFlee", true);
        }


        private void endOrdeal()
        {
            sapi.BroadcastMessageToAllGroups("Ending ordeal...", EnumChatType.Notification);


            // TODO: forcibly end ordeal after time limit? would also have to delete all spawned entities
        }


        // run when current ordeal ends
        private void prepareNextOrdeal()
        {
            // TODO: calculate next ordeal variant based on ordealTier
            if (config == null) 
                return;


        }


        private void loadEntities()
        {
            // TODO: add additional entities as they're made
            ordealEntityTypes = new Dictionary<OrdealVariant, EntityProperties>()
            {
                { OrdealVariant.DawnGreen, sapi.World.GetEntityType(new AssetLocation("ordeals:entitydawngreen")) } 
            };
        }


        private void initOrdealSpawnGroups()
        {
            // TODO: add additional entities as they're made
            ordealSpawnSettings = new Dictionary<OrdealVariant, OrdealSpawnSettings>()
            {
                { OrdealVariant.DawnGreen, new OrdealSpawnSettings { numGroups = 3 } }
            };
        }


        private void initOrdealStrengths()
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


        private void initTexts()
        {
            texts = new Dictionary<OrdealVariant, OrdealEventText>();
            
            foreach (OrdealVariant variant in (OrdealVariant[])Enum.GetValues(typeof(OrdealVariant)))
            {
                int secondPart = Array.FindLastIndex<char>(variant.ToString().ToCharArray(), Char.IsUpper);
                string first = variant.ToString().Substring(0, secondPart);
                string second = variant.ToString().Substring(secondPart);

                texts.Add(variant, new OrdealEventText() { 
                    approaching = "The " + first + " of " + second + " approaches.",
                    imminent = "The " + first + " of " + second + " is imminent."
                });;
            }
            
        }


        private void initConfigs()
        {
            configs = new Dictionary<string, OrdealEventConfig>()
            {
                {
                    "default", new OrdealEventConfig()
                    {
                        ordealFrequency = sapi.World.Calendar.DaysPerMonth,                 // default ordeals to occur at the end of every month
                        ordealTierIncreaseFrequency = sapi.World.Calendar.DaysPerMonth * 3, // increase ordeal tier every 3 months
                        indigoOrdealFrequency = sapi.World.Calendar.DaysPerMonth * 6        // indigo ordeals occur every 6 months
                    }
                }
            };
        }


        private void registerCommands()
        {
            CommandArgumentParsers parsers = sapi.ChatCommands.Parsers;

            sapi.ChatCommands
                .GetOrCreate("ordeals")
                .IgnoreAdditionalArgs()
                .RequiresPrivilege("worldedit")
                .WithDescription("Ordeals mod commands.")

                .BeginSubCommand("nextOrdeal")
                    .WithDescription("Tells you the amount of days until the next Ordeal.")
                    .HandleWith(onCmdNextOrdeal)
                .EndSubCommand()

                .BeginSubCommand("beginOrdeal")
                    .WithDescription("Starts a new ordeal of type ordealVariant")
                    .WithArgs(parsers.WordRange("DawnAmber",    "DawnCrimson",  "DawnGreen",        "DawnViolet",       "DawnWhite",
                                                                "NoonCrimson",  "NoonGreen",        "NoonViolet",       "NoonWhite",
                                                "DuskAmber",    "DuskCrimson",  "DuskGreen",                            "DuskWhite",
                                                "MidnightAmber",                "MidnightGreen",    "MidnightViolet",   "MidnightWhite",
                                                "NightIndigo"))
                    .HandleWith(onCmdBeginOrdeal)
                .EndSubCommand()
                ;

                // additional commands for starting new ordeal, ending current ordeal, scheduling a new ordeal, rescheduling next upcoming ordeal
        }


        private TextCommandResult onCmdNextOrdeal(TextCommandCallingArgs args)
        {
            // args.Caller.Player to find player

            return TextCommandResult.Success();
        }


        private TextCommandResult onCmdBeginOrdeal(TextCommandCallingArgs args)
        {
            // TODO: add guard clauses for notyetimplemented errors on ordeal variants
            // begin ordeal
            OrdealVariant variant = (OrdealVariant) Enum.Parse(typeof(OrdealVariant), (string)args[0]);
            beginOrdeal(variant);

            return TextCommandResult.Success();
        }


        private void Event_OnEntityDeath(Entity entity, DamageSource damageSource)
        {
            if (!(entity.Code.Path.Contains("Dawn") || entity.Code.Path.Contains("Noon") || entity.Code.Path.Contains("Dusk") || entity.Code.Path.Contains("Midnight")))
                return;

            // reduce mob count
        }


        private void Event_PlayerNowPlaying(IServerPlayer byPlayer)
        {
            if (sapi.WorldManager.SaveGame.IsNew && ordealsEnabled)
            {
                double nextOrdealDaysLeft = data.nextOrdealTotalDays - api.World.Calendar.TotalDays;
                byPlayer.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("{0} days until the first ordeal.", (int)nextOrdealDaysLeft), EnumChatType.Notification);
            }
        }

        private void Event_PlayerJoin(IServerPlayer byPlayer)
        {
            //byPlayer.Entity.OnCanSpawnNearby = (type, spawnPos, sc) =>
            //{
            //    return CanSpawnNearby(byPlayer, type, spawnPos, sc);
            //};

            serverChannel.SendPacket(data, byPlayer);
        }


        private void Event_GameWorldSave()
        {
            sapi.WorldManager.SaveGame.StoreData("ordealEventData", SerializerUtil.Serialize(data));
        }
    }
}
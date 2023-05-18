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
    class OrdealsMain : ModSystem
    {
        ICoreAPI api;
        ICoreServerAPI sapi;

        IServerNetworkChannel serverChannel;
        IClientNetworkChannel clientChannel;

        Dictionary<string, OrdealEventConfig> configs;
        Dictionary<OrdealVariant, OrdealEventText> texts;

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

            registerCommands();

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
                        api.World.Logger.Notification("Failed loading ordeal event data, will initialize new data set");
                        data = new OrdealEventRuntimeData();
                        data.nextOrdealDay = sapi.World.Calendar.DaysPerMonth;
                        shouldPrepNextOrdeal = true;
                    }    
                }
                else
                {
                    data = new OrdealEventRuntimeData();
                    data.nextOrdealDay = sapi.World.Calendar.DaysPerMonth;
                    shouldPrepNextOrdeal = true;
                }

                // LoadNoise();

                if (shouldPrepNextOrdeal)
                    prepareNextOrdeal();
            };

            api.Event.GameWorldSave += Event_GameWorldSave;
            api.Event.PlayerJoin += Event_PlayerJoin;
            api.Event.PlayerNowPlaying += Event_PlayerNowPlaying;
            api.Event.RegisterGameTickListener(onOrdealEventTick, 2000);
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

            if (data.isOrdealActive)
            {
                // trySpawnDrifters();
            }

            double nextOrdealDaysLeft = data.nextOrdealDay - api.World.Calendar.TotalDays;

            if (nextOrdealDaysLeft > 0.03 && nextOrdealDaysLeft < 0.35 && data.ordealDayNotify > 1)
            {
                data.ordealDayNotify = 1;
                sapi.BroadcastMessageToAllGroups(texts[data.ordealTier].approaching, EnumChatType.Notification);
            }

            if (nextOrdealDaysLeft <= 0.02 && data.ordealDayNotify > 0)
            {
                data.ordealDayNotify = 0;
                sapi.BroadcastMessageToAllGroups(texts[data.ordealTier].imminent, EnumChatType.Notification);
            }

            if (nextOrdealDaysLeft <= 0)
            {
                float tempstormDurationMul = (float)api.World.Config.GetDecimal("tempstormDurationMul", 1);
                double stormActiveDays = (0.1f + data.nextOrdealVariant * 0.1f) * tempstormDurationMul;

                // Happens when time is fast forwarded
                if (!data.isOrdealActive && nextOrdealDaysLeft + stormActiveDays < 0)
                {
                    prepareNextOrdeal();
                    serverChannel.BroadcastPacket(data);
                    return;
                }

                if (!data.isOrdealActive)
                {
                    data.ordealActiveTotalDays = api.World.Calendar.TotalDays + stormActiveDays;
                    if (data.ordealTier == EnumTempStormStrength.Medium) data.stormGlitchStrength = 0.67f + (float)api.World.Rand.NextDouble() / 10;
                    if (data.ordealTier == EnumTempStormStrength.Heavy) data.stormGlitchStrength = 0.9f + (float)api.World.Rand.NextDouble() / 10;
                    data.isOrdealActive = true;

                    serverChannel.BroadcastPacket(data);

                    var list = (api.World as IServerWorldAccessor).LoadedEntities.Values;
                    foreach (var e in list)
                    {
                        if (e.Code.Path.Contains("drifter"))
                        {
                            e.Attributes.SetBool("ignoreDaylightFlee", true);
                        }

                    }

                }

                double activeDaysLeft = data.ordealActiveTotalDays - api.World.Calendar.TotalDays;
                if (activeDaysLeft < 0.02 && data.ordealDayNotify == 0)
                {
                    data.ordealDayNotify = -1;
                    // sapi.BroadcastMessageToAllGroups(texts[data.ordealTier].Waning, EnumChatType.Notification);
                }


                // replace this condition to end the event when all spawned entities die
                if (activeDaysLeft < 0)
                {
                    data.isOrdealActive = false;
                    data.ordealDayNotify = 99;
                    prepareNextOrdeal();

                    serverChannel.BroadcastPacket(data);

                    var list = (api.World as IServerWorldAccessor).LoadedEntities.Values;
                    foreach (var e in list)
                    {
                        if (e.Code.Path.Contains("drifter"))
                        {
                            e.Attributes.RemoveAttribute("ignoreDaylightFlee");

                            if (api.World.Rand.NextDouble() < 0.5)
                            {
                                sapi.World.DespawnEntity(e, new EntityDespawnData() { Reason = EnumDespawnReason.Expire });
                            }
                        }
                    }
                }
            }
        }

        private void prepareNextOrdeal()
        {

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

            };
        }


        private void registerCommands()
        {
            CommandArgumentParsers parsers = sapi.ChatCommands.Parsers;

            sapi.ChatCommands
                .GetOrCreate("ordeals")
                .IgnoreAdditionalArgs()
                .WithDescription("Ordeals mod commands.")

                .BeginSubCommand("nextOrdeal")
                    .WithDescription("Tells you the amount of days until the next Ordeal.")
                    .HandleWith(onCmdnextOrdeal)
                .EndSubCommand();

                // additional commands for starting new ordeal, ending current ordeal, scheduling a new ordeal, rescheduling next upcoming ordeal
        }


        private TextCommandResult onCmdnextOrdeal(TextCommandCallingArgs args)
        {
            // args.Caller.Player to find player

            return TextCommandResult.Success();
        }


        private void Event_PlayerNowPlaying(IServerPlayer byPlayer)
        {
            if (sapi.WorldManager.SaveGame.IsNew && ordealsEnabled)
            {
                double nextOrdealDaysLeft = data.nextOrdealDay - api.World.Calendar.TotalDays;
                byPlayer.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("{0} days until the first temporal storm.", (int)nextOrdealDaysLeft), EnumChatType.Notification);
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
            sapi.WorldManager.SaveGame.StoreData("temporalStormData", SerializerUtil.Serialize(data));
        }
    }
}
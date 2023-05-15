using System;

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Server;


namespace ordeals.src
{
    class OrdealsMain : ModSystem
    {
        ICoreServerAPI sapi;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            // api.Logger.Warning("Loading Ordeals mod...");
            api.Logger.StoryEvent("Loading Ordeals mod...");
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            sapi = api;

            
            
        }


        public enum OrdealVariants
        {
            DawnAmber, DawnCrimson, DawnGreen, DawnViolet, DawnWhite,
            NoonCrimson, NoonGreen, NoonViolet, NoonWhite, NoonIndigo,
            DuskAmber, DuskCrimson, DuskGreen, DuskWhite,
            MidnightAmber, MidnightGreen, MidnightViolet, MidnightWhite
        }


        private void registerCommands()
        {
            CommandArgumentParsers parsers = sapi.ChatCommands.Parsers;

            sapi.ChatCommands
                .GetOrCreate("ordeals")
                .IgnoreAdditionalArgs()
                .WithDescription("Ordeals mod commands.")

                .BeginSubCommand("nextordeal")
                    .WithDescription("Tells you the amount of days until the next Ordeal.")
                    .HandleWith(onCmdNextOrdeal)
                .EndSubCommand();

                // additional commands for starting new ordeal, ending current ordeal, scheduling a new ordeal, rescheduling next upcoming ordeal
        }


        private TextCommandResult onCmdNextOrdeal(TextCommandCallingArgs args)
        {
            // args.Caller.Player to find player

            return TextCommandResult.Success();
        }
    }
}
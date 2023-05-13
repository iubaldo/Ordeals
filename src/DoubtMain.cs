using System;

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Server;


namespace doubt.src
{
    class DoubtMain : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            // api.Logger.Warning("Loading Doubt...");
            // api.Logger.StoryEvent("Loading Doubt...");
        }
    }
}
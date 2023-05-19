using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    public enum OrdealVariant
    {
        DawnAmber,      DawnCrimson,    DawnGreen,      DawnViolet,         DawnWhite,
                        NoonCrimson,    NoonGreen,      NoonViolet,         NoonWhite,      NightIndigo,
        DuskAmber,      DuskCrimson,    DuskGreen,                          DuskWhite,
        MidnightAmber,                  MidnightGreen,  MidnightViolet,     MidnightWhite
    }


    public enum OrdealTier // determines ordeal strengths and which ordealVariants can appear
    {
        Malkuth,
        Yesod,
        Hod,
        Netzach,
        Tiphereth,
        Gebura,
        Chesed,
        Binah,
        Hokma,
        Keter
    }


    public class OrdealEventConfig
    {
        public float ordealFrequency;               // How often ordeal events occur
        public float ordealTierIncreaseFrequency;   // How often ordeal tier increases
        public float indigoOrdealFrequency;         // How often noon ordeals will be replaced with sweepers
    }


    public class OrdealEventText
    {
        public string approaching;
        public string imminent;
    }


    public class OrdealStrength
    {
        public float dawn = 0;
        public float noon = 0;
        public float dusk = 0;
        public float midnight = 0;
    }


    public class OrdealSpawnSettings
    {
        public int numGroups = 1;
        public int minGroupSize = 1;
        public int maxGroupSize = 1;

        public int spawnRange = 15;
    }


    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class OrdealEventRuntimeData
    {
        public bool isOrdealActive;
        public int activeEntities = 0;

        public int ordealDayNotify = 99;

        public double nextOrdealTotalDays = 7;    // set to occur at the end of every month by default
        public OrdealVariant nextOrdealVariant = 0;
        public int currentOrdealTier = 0;
    }
}

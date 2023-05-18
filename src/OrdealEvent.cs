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


    public enum OrdealTier // determines which ordeal variants will appear when an ordeal event occurs
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
        Daat,
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


    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class OrdealEventRuntimeData
    {
        public bool isOrdealActive;

        public int ordealDayNotify = 99;
        public float ordealGlitchStrength;
        public double ordealActiveTotalDays = 0;


        public double nextOrdealDay = 7;    // set to occur at the end of every month by default
        public OrdealVariant nextOrdealVariant = 0;
        public int ordealTier = 0;
    }
}

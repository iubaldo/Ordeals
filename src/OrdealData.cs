using System.Collections.Generic;

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


    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class OrdealVariantTime
    {
        public OrdealVariant variant;

        /// <summary> false for start, true for end</summary>
        public bool startOrEnd;
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

        public int spawnRange = 30;
    }


    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class OrdealEventRuntimeData
    {
        public bool isOrdealActive;
        public int activeWaves = 0;

        public int ordealDayNotify = 99;
        public double nextOrdealTotalDays = 7;

        // TODO: make this a stack of variants to play throughout the day
        public OrdealVariant nextOrdealVariant = OrdealVariant.DawnGreen;
        public OrdealTier nextOrdealTier = OrdealTier.Malkuth;

        public OrdealVariant currentOrdealVariant = OrdealVariant.DawnGreen;
        public OrdealTier currentOrdealTier = OrdealTier.Malkuth;
    }
}

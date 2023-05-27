using System;
using System.Collections.Generic;

using ProtoBuf;

namespace ordeals.src
{
    public enum OrdealVariant
    {
        DawnAmber,      DawnCrimson,    DawnGreen,      DawnViolet,         DawnWhite,
                        NoonCrimson,    NoonGreen,      NoonViolet,         NoonWhite,      NightIndigo,
        DuskAmber,      DuskCrimson,    DuskGreen,                          DuskWhite,
        MidnightAmber,                  MidnightGreen,  MidnightViolet,     MidnightWhite,  Inactive
    }


    public static class OrdealVariantUtil 
    {
        static Random rand = new Random();
        public static string FirstPart(this OrdealVariant variant)
        {
            string variantName = Enum.GetName(typeof(OrdealVariant), variant);
            int secondPartIndex = Array.FindLastIndex(variantName.ToCharArray(), char.IsUpper);
            return variantName.Substring(0, secondPartIndex);
        }


        public static string SecondPart(this OrdealVariant variant)
        {
            string variantName = Enum.GetName(typeof(OrdealVariant), variant);
            int secondPartIndex = Array.FindLastIndex(variantName.ToCharArray(), char.IsUpper);
            return variantName.Substring(secondPartIndex);
        }


        public static OrdealVariant GetDawnVariant() { return (OrdealVariant)rand.Next(0, 4); }
        public static OrdealVariant GetNoonVariant() { return (OrdealVariant)rand.Next(5, 8); }
        public static OrdealVariant GetDuskVariant() { return (OrdealVariant)rand.Next(10, 13); }
        public static OrdealVariant GetMidnightVariant() { return (OrdealVariant)rand.Next(14, 17); }
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


    public enum OrdealNotice
    {
        Approaching, Imminent, DoNotNotify
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
        public float ordealTimeLimit;               // How long until the ordeal forcibly ends
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

        public OrdealNotice noticeStatus = OrdealNotice.DoNotNotify;
        public double ordealTimeLimit = 1;    // how long the ordeal should be active before forcibly stopping
        public double nextOrdealStartTotalDays = 7;      // day the next ordeal will start

        public Stack<OrdealVariant> nextOrdealVariant = new Stack<OrdealVariant>();
        
        public OrdealTier nextOrdealTier = OrdealTier.Malkuth;

        public OrdealVariant currentOrdealVariant = OrdealVariant.DawnGreen; // should be OrdealVariant.Inactive if isOrdealActive == false
        public OrdealTier currentOrdealTier = OrdealTier.Malkuth; 
    }
}

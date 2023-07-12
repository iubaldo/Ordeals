using System;
using System.Collections;
using System.Collections.Generic;

using Vintagestory.API.Server;
using Vintagestory.API.Common.Entities;

using ProtoBuf;

namespace ordeals.src
{
    public enum OrdealVariant
    {   
        DawnAmber,      DawnCrimson,    DawnGreen,      DawnViolet,     DawnWhite,
                        NoonCrimson,    NoonGreen,      NoonViolet,     NoonWhite,      NightIndigo,
        DuskAmber,      DuskCrimson,    DuskGreen,                      DuskWhite,
        MidnightAmber,                  MidnightGreen, MidnightViolet,  MidnightWhite,  Inactive
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


        public static string GetName(this OrdealVariant variant) { return Enum.GetName(typeof(OrdealVariant), variant); }
        public static string GetName(this OrdealTier tier) { return Enum.GetName(typeof(OrdealTier), tier); }


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


    public class WaveSpawnSettings
    {
        public int numGroups = 1;
        public int minGroupSize = 1;
        public int maxGroupSize = 1;

        public int spawnRange = 30;
    }


    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class OrdealEventRuntimeData
    {
        public int activeWaves = 0;

        public OrdealNotice noticeStatus = OrdealNotice.DoNotNotify;
        public double nextStartTotalDays = 7;      // day the next ordeal even will start in total days

        public OrdealEvent currentEvent = null; // null when no ordeals active
        public List<OrdealEvent> eventStack = new List<OrdealEvent>(); // can't serialize stacks, so just treat this list like one

        public OrdealTier nextOrdealTier = OrdealTier.Malkuth;  // the tier newly added events should be evaluated at

        public OrdealEvent PeekEvent() { return eventStack[-1]; }
        public OrdealEvent PopEvent()
        {
            OrdealEvent toReturn = eventStack[-1];
            eventStack.RemoveAt(eventStack.Count - 1);
            return toReturn;
        }
    }


    public class OrdealEvent 
    {
        public OrdealTier tier;

        public List<OrdealWave> waveStack = new List<OrdealWave>(); // can't serialize stacks, so just treat this list like one
        public OrdealWave currentWave = null;
        public Dictionary<IServerPlayer, List<Entity>> activeGroups = new Dictionary<IServerPlayer, List<Entity>>();

        public double startTimeTotalDays;
        public double timeLimitTotalDays = 1;    // how long the event should be active before forcibly stopping in total days
        public bool isEventActive;

        public OrdealEvent(OrdealTier eventTier, double newStartTime)
        {
            tier = eventTier;
            int startDay = (int)newStartTime;

            if (OrdealDict.tierStrengths[tier].midnight > 0)
                waveStack.Add(new OrdealWave(OrdealVariantUtil.GetMidnightVariant(), startDay + 1.0));
            if (OrdealDict.tierStrengths[tier].dusk > 0)
                waveStack.Add(new OrdealWave(OrdealVariantUtil.GetDuskVariant(), startDay + 0.75));
            if (OrdealDict.tierStrengths[tier].noon > 0)
                waveStack.Add(new OrdealWave(OrdealVariantUtil.GetNoonVariant(), startDay + 0.50));
            if (OrdealDict.tierStrengths[tier].dawn > 0)
                waveStack.Add(new OrdealWave(OrdealVariantUtil.GetDawnVariant(), startDay + 0.25));
        }

        public OrdealWave PeekWave() { return waveStack[-1]; }
        public OrdealWave PopWave()
        {
            OrdealWave toReturn = waveStack[-1];
            waveStack.RemoveAt(waveStack.Count - 1);
            return toReturn;
        }
    }


    public class OrdealWave
    {
        public OrdealVariant variant;
        public WaveSpawnSettings spawnSettings;
        public double waveStartTimeTotalDays;   // the time the ordeal is scheduled to start, in total days
        public double waveTimeLimit;
        public bool isWaveActive;

        public OrdealWave(OrdealVariant newVariant, double newStartTime)
        {
            variant = newVariant;
            spawnSettings = OrdealDict.spawnSettings[variant];
            waveStartTimeTotalDays = newStartTime;
        }
    }


    public static class OrdealDict
    {
        public static Dictionary<OrdealTier, OrdealStrength> tierStrengths;
        public static Dictionary<OrdealVariant, WaveSpawnSettings> spawnSettings;

        // loaded during server start due to needing sapi access
        public static Dictionary<OrdealVariant, EntityProperties> entityTypes; 
        public static Dictionary<string, OrdealEventConfig> configs;

        static OrdealDict()
        {
            tierStrengths = new Dictionary<OrdealTier, OrdealStrength>()
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

            spawnSettings = new Dictionary<OrdealVariant, WaveSpawnSettings>()
            {
                { OrdealVariant.DawnGreen, new WaveSpawnSettings { numGroups = 3 } }
            };
        }
    }
}

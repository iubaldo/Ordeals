using System;
using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Server;
using Vintagestory.API.Common.Entities;

using ProtoBuf;


namespace Ordeals.src
{
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


    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WaveSpawnSettings
    {
        public int numGroups = 1;
        public int minGroupSize = 1;
        public int maxGroupSize = 1;

        public int spawnRange = 30;
    }


    [ProtoContract]
    public class OrdealEventRuntimeData
    {
        [ProtoMember(1)]
        public int activeWaves = 0;

        [ProtoMember(2)]
        public NoticeStatus noticeStatus = NoticeStatus.None;
        [ProtoMember(3)]
        public double nextStartTotalDays = 7;      // day the next ordeal even will start in total days

        [ProtoMember(4)]
        public OrdealEvent currentEvent = null; // null when no ordeals active
        [ProtoMember(5)]
        private List<OrdealEvent> eventQueue = new(); // can't serialize queues, so just treat this list like a priority queue

        [ProtoMember(6)]
        public OrdealTier nextOrdealTier = OrdealTier.Malkuth;  // the tier newly added events should be evaluated at


        public void PushEvent(OrdealEvent newEvent)
        {
            eventQueue.Add(newEvent);
            eventQueue = eventQueue.OrderBy(item => item.startTimeTotalDays).ToList();
        }
        public OrdealEvent PeekEvent() { return eventQueue.Last(); }
        public OrdealEvent PopEvent()
        {
            OrdealEvent toReturn = eventQueue.Last();
            eventQueue.RemoveAt(eventQueue.Count - 1);
            return toReturn;
        }
        public int NumEvents() { return eventQueue.Count; }
        public bool HasEvents() { return eventQueue.Count > 0; }
        public OrdealEvent GetEvent(int index) { return eventQueue[index]; }
        public void ClearEvents() { eventQueue.Clear(); }
    }


    [ProtoContract]
    public class OrdealEvent 
    {
        [ProtoMember(1)]
        public OrdealTier tier;

        [ProtoMember(2)]
        private List<OrdealWave> waveQueue = new(); // can't serialize queues, so just treat this list like a priority queue
        [ProtoMember(3)]
        public OrdealWave currentWave = null;
        [ProtoMember(4)]
        public Dictionary<IServerPlayer, List<Entity>> activeGroups = new Dictionary<IServerPlayer, List<Entity>>();

        [ProtoMember(5)]
        public double startTimeTotalDays;
        [ProtoMember(6)]
        public double timeLimitTotalDays = 1;    // how long the event should be active before forcibly stopping in total days
        [ProtoMember(7)]
        public bool isEventActive;

        public OrdealEvent(OrdealTier eventTier, double newStartTime)
        {
            tier = eventTier;
            startTimeTotalDays = newStartTime;
            InitWaves();
        }

        public void InitWaves()
        {
            int startDay = (int)startTimeTotalDays;

            waveQueue.Add(new OrdealWave(OrdealVariant.DawnGreen, startDay + 0.25));

            // TODO: only push implemented variants
            //if (OrdealData.tierStrengths[tier].midnight > 0)
            //    waveQueue.Add(new OrdealWave(OrdealVariantUtil.GetMidnightVariant(), startDay + 1.0));
            //if (OrdealData.tierStrengths[tier].dusk > 0)
            //    waveQueue.Add(new OrdealWave(OrdealVariantUtil.GetDuskVariant(), startDay + 0.75));
            //if (OrdealData.tierStrengths[tier].noon > 0)
            //    waveQueue.Add(new OrdealWave(OrdealVariantUtil.GetNoonVariant(), startDay + 0.50));
            //if (OrdealData.tierStrengths[tier].dawn > 0)
            //    waveQueue.Add(new OrdealWave(OrdealVariantUtil.GetDawnVariant(), startDay + 0.25));
        }

        public void PushWave(OrdealWave wave)
        {
            waveQueue.Add(wave);
            waveQueue = waveQueue.OrderBy(item => item.startTimeTotalDays).ToList();
        }
        public OrdealWave PeekWave() { return waveQueue.Last(); }
        public OrdealWave PopWave()
        {
            OrdealWave toReturn = waveQueue.Last();
            waveQueue.RemoveAt(0);
            return toReturn;
        }
        public int NumWaves() { return waveQueue.Count; }
        public bool HasWaves() { return waveQueue.Count > 0; }
    }


    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class OrdealWave
    {
        public OrdealVariant variant;
        public WaveSpawnSettings spawnSettings;
        public double startTimeTotalDays;   // the time the ordeal is scheduled to start, in total days
        public double timeLimitTotalDays;
        public bool isWaveActive;

        public OrdealWave(OrdealVariant newVariant, double newStartTime)
        {
            variant = newVariant;
            spawnSettings = OrdealData.spawnSettings[variant];
            startTimeTotalDays = newStartTime;
        }
    }


    public static class OrdealData
    {
        public static Dictionary<OrdealTier, OrdealStrength> tierStrengths;
        public static Dictionary<OrdealVariant, WaveSpawnSettings> spawnSettings;

        // loaded during server start due to needing sapi access
        public static Dictionary<OrdealVariant, EntityProperties> entityTypes; 
        public static Dictionary<string, OrdealEventConfig> configs;

        static OrdealData()
        {
            // TODO: make these a range of strengths rather than single values
            //       would also need to update code for determining effects of strength on enemies
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

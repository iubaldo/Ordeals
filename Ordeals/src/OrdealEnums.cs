using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ordeals.src
{
    public enum OrdealVariant
    {
        DawnAmber, DawnCrimson, DawnGreen, DawnViolet, DawnWhite,
        NoonCrimson, NoonGreen, NoonViolet, NoonWhite, NightIndigo,
        DuskAmber, DuskCrimson, DuskGreen, DuskWhite,
        MidnightAmber, MidnightGreen, MidnightViolet, MidnightWhite, Inactive
    }

    public static class OrdealVariantUtil
    {
        static readonly Random rand = new();
        public static string FirstPart(this OrdealVariant variant)
        {
            string variantName = Enum.GetName(typeof(OrdealVariant), variant);
            if (variantName == null)
                return "";

            int secondPartIndex = Array.FindLastIndex(variantName.ToCharArray(), char.IsUpper);
            return variantName.Substring(0, secondPartIndex);
        }


        public static string SecondPart(this OrdealVariant variant)
        {
            string variantName = Enum.GetName(typeof(OrdealVariant), variant);
            if (variantName == null)
                return "";

            int secondPartIndex = Array.FindLastIndex(variantName.ToCharArray(), char.IsUpper);
            return variantName.Substring(secondPartIndex);
        }


        public static string GetName(this OrdealVariant variant) => Enum.GetName(typeof(OrdealVariant), variant);

        public static string GetName(this OrdealTier tier) => Enum.GetName(typeof(OrdealTier), tier);


        // TODO: find a more elegant way to do this
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


    public enum NoticeStatus
    {
        None, Approaching, Imminent, Waning
    }
}

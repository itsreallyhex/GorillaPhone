using System;
using System.Text;

namespace GorillaPhone.Photo
{
    /// <summary>
    /// Which map (zone) the player is in. Read from the game's ZoneManagement.instance.activeZones,
    /// a public list of GTZone values that the game clears and refills whenever a zone trigger fires
    /// (Verified in the game's code). Several zones can be active at once, so this returns all of them.
    /// </summary>
    public static class MapInfo
    {
        /// <summary>The active zones as raw GTZone names separated by commas, for example "forest,skyJungle". Empty when unknown.</summary>
        public static string CurrentZones()
        {
            try
            {
                ZoneManagement zm = ZoneManagement.instance;
                if (zm == null || zm.activeZones == null || zm.activeZones.Count == 0) return "";

                var sb = new StringBuilder();
                foreach (GTZone z in zm.activeZones)
                {
                    if (z == GTZone.none) continue;
                    if (sb.Length > 0) sb.Append(',');
                    sb.Append(z.ToString());
                }
                return sb.ToString();
            }
            catch (Exception)
            {
                return "";   // a photo without a map is better than no photo
            }
        }
    }
}

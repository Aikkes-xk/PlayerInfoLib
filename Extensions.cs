using MySql.Data.MySqlClient;
using SDG.Unturned;
using Steamworks;
using System;

namespace PlayerInfoLibrary
{
    public static class Extensions
    {
        public static DateTime FromTimeStamp(this long timestamp)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timestamp).ToLocalTime();
        }

        public static long ToTimeStamp(this DateTime datetime)
        {
            return (long)(datetime.ToUniversalTime().Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
        }

        public static bool IsDBNull (this MySqlDataReader reader, string fieldname)
        {
            return reader.IsDBNull(reader.GetOrdinal(fieldname));
        }

        // 从字符串中返回一个Steamworks.CSteamID，如果它是CSteamID，则返回true。
        public static bool isCSteamID(this string sCSteamID, out CSteamID cSteamID)
        {
            ulong ulCSteamID;
            cSteamID = (CSteamID)0;
            if (ulong.TryParse(sCSteamID, out ulCSteamID))
            {
                if ((ulCSteamID >= 0x0110000100000000 && ulCSteamID <= 0x0170000000000000) || ulCSteamID == 0)
                {
                    cSteamID = (CSteamID)ulCSteamID;
                    return true;
                }
            }
            return false;
        }

        public static string Truncate(this string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        // 返回格式化的字符串以及它们在服务器中播放了d，h，m，s的时间。
        public static string FormatTotalTime(this int totalTime)
        {
            string totalTimeFormated = "";
            if (totalTime >= (60 * 60 * 24))
            {
                totalTimeFormated = ((int)(totalTime / (60 * 60 * 24))).ToString() + "d ";
            }
            if (totalTime >= (60 * 60))
            {
                totalTimeFormated += ((int)((totalTime / (60 * 60)) % 24)).ToString() + "h ";
            }
            if (totalTime >= 60)
            {
                totalTimeFormated += ((int)((totalTime / 60) % 60)).ToString() + "m ";
            }
            totalTimeFormated += ((int)(totalTime % 60)).ToString() + "s";
            return totalTimeFormated;
        }
    }
}

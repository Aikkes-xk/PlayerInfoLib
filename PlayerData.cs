﻿using Rocket.API;
using Rocket.Core;
using Steamworks;
using System;
using System.Linq;

namespace PlayerInfoLibrary
{
    public class PlayerData
    {
        public CSteamID SteamID { get; private set; }
        public string SteamName { get; internal set; }
        public string CharacterName { get; internal set; }
        public string IP { get; internal set; }
        public DateTime LastLoginGlobal { get; internal set; }
        public int TotalPlayime { get; internal set; }
        public ushort LastServerID { get; internal set; }
        public string LastServerName { get; internal set; }
        public ushort ServerID { get; private set; }
        public DateTime LastLoginLocal { get; internal set; }
        public bool CleanedBuildables { get; internal set; }
        public bool CleanedPlayerData { get; internal set; }
        public DateTime CacheTime { get; internal set; }

        /// <summary>
        /// 检查存储在此类中的服务器特定数据是否来自此服务器（本地）.
        /// </summary>
        /// <returns>如果数据来自此服务器，则为 true。</returns>
        public bool IsLocal()
        {
            if (!IsValid())
                return false;
            return ServerID == PlayerInfoLib.Database.InstanceID;
        }

        /// <summary>
        /// 检查数据是否有效。
        /// </summary>
        /// <returns></returns>
        public bool IsValid()
        {
            return SteamID != CSteamID.Nil;
        }


        internal PlayerData()
        {
            SteamID = CSteamID.Nil;
            TotalPlayime = 0;
        }
        internal PlayerData(CSteamID steamID, string steamName, string characterName, string ip, DateTime lastLoginGlobal, ushort lastServerID, string lastServerName, ushort serverID, DateTime lastLoginLocal, bool cleanedBuildables, bool cleanedPlayerData, int totalPlayTime)
        {
            SteamID = steamID;
            SteamName = steamName;
            CharacterName = characterName;
            IP = ip;
            LastLoginGlobal = lastLoginGlobal;
            LastServerID = lastServerID;
            LastServerName = lastServerName;
            ServerID = serverID;
            LastLoginLocal = lastLoginLocal;
            CleanedBuildables = cleanedBuildables;
            CleanedPlayerData = cleanedPlayerData;
            TotalPlayime = totalPlayTime;
        }
    }
}
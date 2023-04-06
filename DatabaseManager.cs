using MySql.Data.MySqlClient;
using Rocket.Core.Logging;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayerInfoLibrary
{
    public class DatabaseManager
    {
        private Dictionary<CSteamID, PlayerData> Cache = new Dictionary<CSteamID, PlayerData>();
        public bool Initialized { get; private set; }
        private MySqlConnection Connection = null;
        private int MaxRetry = 5;
        private string Table;
        private string TableConfig;
        private string TableInstance;
        private string TableServer;
        internal ushort InstanceID { get; private set; }
        public static readonly uint DatabaseSchemaVersion = 4;
        public static readonly uint DatabaseInterfaceVersion = 2;

        // Initialization section.
        internal DatabaseManager()
        {
            new I18N.West.CP1250();
            Initialized = false;
            Table = PlayerInfoLib.Instance.Configuration.Instance.DatabaseTableName;
            TableConfig = Table + "_config";
            TableInstance = Table + "_instance";
            TableServer = Table + "_server";
            CheckSchema();
        }

        internal void Unload()
        {
            Connection.Dispose();
        }

        // 插件/数据库设置部分。
        private void CheckSchema()
        {
            try
            {
                if (!CreateConnection())
                    return;
                ushort version = 0;
                MySqlCommand command = Connection.CreateCommand();
                command.CommandText = "show tables like '" + TableConfig + "';";
                object test = command.ExecuteScalar();

                if (test == null)
                {
                    command.CommandText = "CREATE TABLE `"+TableConfig+"` (" +
                        " `key` varchar(40) COLLATE utf8_unicode_ci NOT NULL," +
                        " `value` varchar(40) COLLATE utf8_unicode_ci NOT NULL," +
                        " PRIMARY KEY(`key`)" +
                        ") ENGINE = MyISAM DEFAULT CHARSET = utf8 COLLATE = utf8_unicode_ci;";
                    command.CommandText += "CREATE TABLE `"+Table+"` (" +
                        " `SteamID` bigint(24) unsigned NOT NULL," +
                        " `SteamName` varchar(255) COLLATE utf8_unicode_ci NOT NULL," +
                        " `CharName` varchar(255) COLLATE utf8_unicode_ci NOT NULL," +
                        " `IP` varchar(16) COLLATE utf8_unicode_ci NOT NULL," +
                        " `LastLoginGlobal` bigint(32) NOT NULL," +
                        " `LastServerID` smallint(5) unsigned NOT NULL," +
                        " PRIMARY KEY (`SteamID`)," +
                        " KEY `LastServerID` (`LastServerID`)," +
                        " KEY `IP` (`IP`)" +
                        ") ENGINE = MyISAM DEFAULT CHARSET = utf8 COLLATE = utf8_unicode_ci; ";
                    command.CommandText += "CREATE TABLE `"+TableInstance+"` (" +
                        " `ServerID` smallint(5) unsigned NOT NULL AUTO_INCREMENT," +
                        " `ServerInstance` varchar(128) COLLATE utf8_unicode_ci NOT NULL," +
                        " `ServerName` varchar(60) COLLATE utf8_unicode_ci NOT NULL," +
                        " PRIMARY KEY(`ServerID`)," +
                        " UNIQUE KEY `ServerInstance` (`ServerInstance`)" +
                        ") ENGINE = MyISAM DEFAULT CHARSET = utf8 COLLATE = utf8_unicode_ci; ";
                    command.CommandText += "CREATE TABLE `"+TableServer+"` (" +
                        " `SteamID` bigint(24) unsigned NOT NULL," +
                        " `ServerID` smallint(5) unsigned NOT NULL," +
                        " `LastLoginLocal` bigint(32) NOT NULL," +
                        " `CleanedBuildables` BOOLEAN NOT NULL," +
                        " `CleanedPlayerData` BOOLEAN NOT NULL," +
                        " PRIMARY KEY(`SteamID`,`ServerID`)," +
                        " KEY `CleanedBuildables` (`CleanedBuildables`)," +
                        " KEY `CleanedPlayerData` (`CleanedPlayerData`)" +
                        ") ENGINE = MyISAM DEFAULT CHARSET = utf8 COLLATE = utf8_unicode_ci; ";
                    command.ExecuteNonQuery();
                    CheckVersion(version, command);
                    
                }
                else
                {
                    command.CommandText = "SELECT `value` FROM `" + TableConfig + "` WHERE `key` = 'version'";
                    object result = command.ExecuteScalar();
                    if (result != null)
                    {
                        if (ushort.TryParse(result.ToString(), out version))
                        {
                            if (version < DatabaseSchemaVersion)
                                CheckVersion(version, command);
                        }
                        else
                        {
                            Logger.LogError("Error: 找不到数据库版本号。");
                            return;
                        }
                    }
                    else
                    {
                        Logger.LogError("Error:找不到数据库版本号。");
                        return;
                    }
                }
                if (!GetInstanceID())
                {
                    // 如果服务器实例刚刚添加到数据库中，请重试获取实例ID。
                    if (!GetInstanceID(true))
                    {
                        Logger.LogError("Error:从数据库获取ID时出错。");
                        return;
                    }
                }
                Initialized = true;
            }
            catch (MySqlException ex)
            {
                Logger.LogException(ex);
            }
        }

        private bool GetInstanceID(bool retrying = false)
        {
            //Load server instance id.
            MySqlDataReader getInstance = null;
            try {
                MySqlCommand command = Connection.CreateCommand();
                command.Parameters.AddWithValue("@instname", Provider.serverID.ToLower());
                command.Parameters.AddWithValue("@servername", Provider.serverName);
                command.CommandText = "SELECT `ServerID`, `ServerName` FROM `" + TableInstance + "` WHERE `ServerInstance` = @instname;";
                getInstance = command.ExecuteReader();
                if (getInstance.Read())
                {
                    InstanceID = getInstance.GetUInt16("ServerID");
                    if (InstanceID == 0)
                        return false;
                    if (getInstance.GetString("ServerName") != Provider.serverName)
                    {
                        getInstance.Close();
                        getInstance.Dispose();
                        command.CommandText = "UPDATE `" + TableInstance + "` SET `ServerName` = @servername WHERE `ServerID` = " + InstanceID + ";";
                        command.ExecuteNonQuery();
                    }
                    return true;
                }
                // 没有找到实例记录，添加一个到数据库。
                else if (!retrying)
                {
                    getInstance.Close();
                    getInstance.Dispose();
                    command.CommandText = "INSERT INTO `" + TableInstance + "` (`ServerInstance`, `ServerName`) VALUES (@instname, @servername);";
                    command.ExecuteNonQuery();
                }
                return false;
            }
            catch (MySqlException ex)
            {
                HandleException(ex);
            }
            finally
            {
                if (getInstance != null)
                {
                    getInstance.Close();
                    getInstance.Dispose();
                }
            }
            return false;
        }

        internal bool SetInstanceName(string newName)
        {
            try
            {
                if (Initialized)
                {
                    MySqlCommand command = Connection.CreateCommand();
                    command.Parameters.AddWithValue("@newname", newName);
                    command.Parameters.AddWithValue("@instance", InstanceID);
                    command.CommandText = "UPDATE `" + TableInstance + "` SET `ServerInstance` = @newname WHERE `ServerID` = @instance;";
                    command.ExecuteNonQuery();
                    return true;
                }
            }
            catch (MySqlException ex)
            {
                HandleException(ex);
            }
            return false;
        }

        private void CheckVersion(ushort version, MySqlCommand command)
        {
            ushort updatingVersion = 0;
            try
            {
                if (version < 1)
                {
                    updatingVersion = 1;
                    command.CommandText = "INSERT INTO `" + TableConfig + "` (`key`, `value`) VALUES ('version', '1');";
                    command.ExecuteNonQuery();
                }
                if (version < 2)
                {
                    updatingVersion = 2;
                    Logger.LogWarning("Updating Playerinfo DB to version: " + updatingVersion);
                    command.CommandText = "ALTER TABLE `" + Table + "` DROP INDEX IP;" +
                        "ALTER TABLE `" + Table + "` CHANGE `IP` `IP_old` VARCHAR(16) CHARACTER SET utf8 COLLATE utf8_unicode_ci NOT NULL;" +
                        "ALTER TABLE `" + Table + "` ADD `IP` INT(10) UNSIGNED NOT NULL AFTER `CharName`;";
                    command.ExecuteNonQuery();
                    Dictionary<CSteamID, uint> New = new Dictionary<CSteamID, uint>();
                    command.CommandText = "SELECT SteamID, IP_old FROM `" + Table + "`";
                    MySqlDataReader result = command.ExecuteReader();
                    if (result.HasRows)
                    {
                        while (result.Read())
                        {
                            if (!result.IsDBNull("IP_old"))
                            {
                                if (Parser.checkIP(result.GetString("IP_old")))
                                {
                                    New.Add((CSteamID)result.GetUInt64("SteamID"), Parser.getUInt32FromIP(result.GetString("IP_old")));
                                }
                            }
                        }
                    }
                    result.Close();
                    result.Dispose();
                    if (New.Count != 0)
                    {
                        foreach (KeyValuePair<CSteamID, uint> record in New)
                        {
                            command.CommandText = "UPDATE `" + Table + "` SET `IP` = " + record.Value + " WHERE `SteamID` = " + record.Key + ";";
                            command.ExecuteNonQuery();
                        }
                    }
                    command.CommandText = "ALTER TABLE `" + Table + "` ADD INDEX(`IP`);" +
                        "ALTER TABLE `" + Table + "` DROP `IP_old`;" +
                        "UPDATE `" + TableConfig + "` SET `value` = '2' WHERE `key` = 'version';";
                    command.ExecuteNonQuery();
                }
                if (version < 3)
                {
                    updatingVersion = 3;
                    Logger.LogWarning("Updating Playerinfo DB to version: " + updatingVersion);
                    command.CommandText = "ALTER TABLE `" + Table + "` ADD `TotalPlayTime` INT NOT NULL AFTER `LastLoginGlobal`;" +
                        "UPDATE `" + TableConfig + "` SET `value` = '3' WHERE `key` = 'version';";
                    command.ExecuteNonQuery();
                    Logger.LogWarning("Finished.");
                }
                if (version < 4)
                {
                    updatingVersion = 4;
                    // Updating tables to handle Special UTF8 characters(like emoji characters.)
                    Logger.LogWarning("Updating Playerinfo DB to version: " + updatingVersion);
                    command.CommandText = "ALTER TABLE `" + Table + "` MODIFY `SteamName` VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL, MODIFY `CharName` VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;" +
                        "ALTER TABLE `"+ TableInstance + "` MODIFY `ServerInstance` VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL, MODIFY `ServerName` VARCHAR(60) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL;" +
                        "REPAIR TABLE `" + Table + "`, `" + TableInstance + "`;" +
                        "UPDATE `" + TableConfig + "` SET `value` = '4' WHERE `key` = 'version';";
                    command.ExecuteNonQuery();
                }
            }
            catch (MySqlException ex)
            {
                HandleException(ex, "无法将数据库架构更新到版本 " + updatingVersion + ", 你可能需要手动更新数据库架构！");
            }
        }

        // 连接处理部分。
        internal void CheckConnection()
        {
            try
            {
                if (Initialized)
                {
                    MySqlCommand command = Connection.CreateCommand();
                    command.CommandText = "SELECT 1";
                    command.ExecuteNonQuery();
                }
            }
            catch (MySqlException ex)
            {
                HandleException(ex);
            }
        }
        //创建连接
        private bool CreateConnection(int count = 1)
        {
            try
            {
                Connection = null;
                if (PlayerInfoLib.Instance.Configuration.Instance.DatabasePort == 0)
                    PlayerInfoLib.Instance.Configuration.Instance.DatabasePort = 3306;
                Connection = new MySqlConnection(string.Format("SERVER={0};DATABASE={1};UID={2};PASSWORD={3};PORT={4};CHARSET=utf8mb4", PlayerInfoLib.Instance.Configuration.Instance.DatabaseAddress, PlayerInfoLib.Instance.Configuration.Instance.DatabaseName, PlayerInfoLib.Instance.Configuration.Instance.DatabaseUserName, PlayerInfoLib.Instance.Configuration.Instance.DatabasePassword, PlayerInfoLib.Instance.Configuration.Instance.DatabasePort));
                Connection.Open();
                return true;
            }
            catch(MySqlException ex)
            {
                if (count < MaxRetry)
                {
                    return CreateConnection(count + 1);
                }
                Logger.LogException(ex, "Failed to connect to the database server!");
                return false;
            }
        }

        private bool HandleException(MySqlException ex, string msg = null)
        {
            if (ex.Number == 0)
            {
                Logger.LogException(ex, "Error: 与数据库服务器的连接丢失，尝试重新连接。");
                if (CreateConnection())
                {
                    Logger.Log("成功.");
                    return true;
                }
                Logger.LogError("重新连接失败。");
            }
            else
            {
                Logger.LogWarning(ex.Number.ToString() + ":" + ((MySqlErrorCode)ex.Number).ToString());
                Logger.LogException(ex , msg != null ? msg : null);
            }
            return false;
        }

        //查询部分。
        /// <summary>
        /// 通过 Steam ID 查询存储的玩家信息。
        /// </summary>
        /// <param name="steamId">字符串：您要为其获取玩家数据的玩家的 SteamID。</param>
        /// <param name="cached">Bool: 在检查数据库之前首先检查缓存信息的可选参数（更快地检查以前缓存的数据。）</param>
        /// <returns>如果找到玩家数据，则返回 PlayerData 类型对象，如果未找到玩家，则返回空 PlayerData 对象。</returns>
        public PlayerData QueryById(CSteamID steamId, bool cached = true)
        {
            PlayerData UnsetData = new PlayerData();
            PlayerData playerData = UnsetData;
            MySqlDataReader reader = null;
            if (Cache.ContainsKey(steamId) && cached == true)
            {
                playerData = Cache[steamId];
                return playerData;

            }
            try
            {
                if (!Initialized)
                {
                    Logger.LogError("Error:无法从数据库加载玩家信息，插件未正确初始化。");
                    return UnsetData;
                }
                MySqlCommand command = Connection.CreateCommand();
                command.Parameters.AddWithValue("@steamid", steamId);
                command.Parameters.AddWithValue("@instance", InstanceID);
                command.CommandText = "SELECT * FROM (SELECT a.SteamID, a.SteamName, a.CharName, a.IP, a.LastLoginGlobal, a.TotalPlayTime, a.LastServerID, b.ServerID, b.LastLoginLocal, b.CleanedBuildables, b.CleanedPlayerData, c.ServerName AS LastServerName FROM `" + Table + "` AS a LEFT JOIN `" + TableServer + "` AS b ON a.SteamID = b.SteamID LEFT JOIN `" + TableInstance + "` AS c ON a.LastServerID = c.ServerID WHERE (b.ServerID = @instance OR b.ServerID = a.LastServerID OR b.ServerID IS NULL) AND a.SteamID = @steamid ORDER BY b.LastLoginLocal ASC) AS g";
                reader = command.ExecuteReader();
                if (reader.Read())
                {
                    //构建玩家数据
                    playerData = BuildPlayerData(reader);
                }
                if (Cache.ContainsKey(steamId))
                    Cache.Remove(steamId);
                playerData.CacheTime = DateTime.Now;
                Cache.Add(steamId, playerData);
            }
            catch (MySqlException ex)
            {
                HandleException(ex);
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                    reader.Dispose();
                }
            }
            return playerData;
        }

        /// <summary>
        /// 按名称查询数据库。
        /// </summary>
        /// <param name="playerName">Player name to search for in the database.</param>
        /// <param name="queryType">Sets what type of lookup it is: by steam name, by char name, or by both.</param>
        /// <param name="totalRecods">Returns the total number of records found in the database for the search query.</param>
        /// <param name="pagination">Enables or disables pagination prior to the return.</param>
        /// <param name="page">For pagination, set the page to return.</param>
        /// <param name="limit">Limits the number of records to return.</param>
        /// <returns>A list of PlayerData typed data.</returns>
        public List<PlayerData> QueryByName(string playerName, QueryType queryType, out uint totalRecods, bool pagination = true, uint page = 1, uint limit = 4)
        {
            List<PlayerData> playerList = new List<PlayerData>();
            MySqlDataReader reader = null;
            totalRecods = 0;
            uint limitStart = (page - 1) * limit;
            MySqlCommand command = Connection.CreateCommand();
            try
            {
                if (!Initialized)
                {
                    Logger.LogError("Error: 无法从数据库加载玩家信息，插件未正确初始化。");
                    return playerList;
                }
                if (page == 0 || limit == 0)
                {
                    Logger.LogError("Error: 分页值无效，这些值必须大于0。");
                    return playerList;
                }
                if (playerName.Trim() == string.Empty)
                {
                    Logger.LogWarning("警告: 玩家名称中至少需要一个字符。");
                    return playerList;
                }

                command.Parameters.AddWithValue("@name", "%" + playerName + "%");
                command.Parameters.AddWithValue("@instance", InstanceID);
                string type;
                switch (queryType)
                {
                    case QueryType.Both:
                        type = "AND (a.SteamName LIKE @name OR a.CharName LIKE @name)";
                        break;
                    case QueryType.CharName:
                        type = "AND a.CharName LIKE @name";
                        break;
                    case QueryType.SteamName:
                        type = "AND a.SteamName LIKE @name";
                        break;
                    case QueryType.IP:
                        type = "AND a.IP = " + Parser.getUInt32FromIP(playerName);
                        break;
                    default:
                        type = string.Empty;
                        break;
                }
                if (pagination)
                    command.CommandText = "SELECT IFNULL(Count(a.steamid),0) AS count FROM  `" + Table + "` AS a LEFT JOIN `" + TableServer + "` AS b ON a.steamid = b.steamid  WHERE  ( b.serverid = @instance OR b.serverid = a.lastserverid OR b.serverid IS NULL )  " + type + " GROUP BY a.steamid;";
                command.CommandText += "SELECT a.steamid, a.steamname, a.charname, a.ip, a.lastloginglobal, a.totalplaytime, a.lastserverid, b.serverid, b.lastloginlocal, b.cleanedbuildables, b.cleanedplayerdata, c.servername AS LastServerName FROM `" + Table + "` AS a LEFT JOIN `" + TableServer + "` AS b ON a.steamid = b.steamid LEFT JOIN `" + TableInstance + "` AS c ON a.lastserverid = c.serverid WHERE (b.serverid = @instance OR b.serverid = a.lastserverid OR b.serverid IS NULL ) " + type + " ORDER BY a.lastloginglobal DESC LIMIT  0, 10; ";
                reader = command.ExecuteReader();
                if (pagination)
                {
                    if (reader.Read())
                        totalRecods = reader.GetUInt32("count");
                    if (!reader.NextResult())
                    {
                        return playerList;
                    }
                }
                if (!reader.HasRows)
                {
                    return playerList;
                }
                while (reader.Read())
                {
                    PlayerData record = BuildPlayerData(reader);
                    record.CacheTime = DateTime.Now;
                    playerList.Add(record);

                }
                if (!pagination)
                    totalRecods = (uint)playerList.Count;
            }
            catch (MySqlException ex)
            {
                HandleException(ex, "Failed to execute: "+ command.CommandText);
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                    reader.Dispose();
                }
            }
            return playerList;
        }

        public List<object[]> GetCleanupList(OptionType optionType, long beforeTime)
        {
            List<object[]> tmp = new List<object[]>();
            MySqlDataReader reader = null;
            if (!Initialized)
            {
                Logger.LogError("Error:无法从数据库加载播放器信息，插件未正确初始化。");
                return tmp;
            }
            string type = ParseOption(optionType);
            if (type == null)
                return tmp;
            try
            {
                MySqlCommand command = Connection.CreateCommand();
                command.Parameters.AddWithValue("@time", beforeTime);
                command.Parameters.AddWithValue("@instance", InstanceID);
                command.CommandText = "SELECT a.SteamID, b.CharName, b.SteamName  FROM `" + TableServer + "` AS a LEFT JOIN `" + Table + "` AS b ON a.SteamID = b.SteamID WHERE a.ServerID = @instance AND a.LastLoginLocal < @time AND a." + type + " = 0 AND b.SteamID IS NOT NULL ORDER BY a.LastLoginLocal  ASC;";
                reader = command.ExecuteReader();
                if (!reader.HasRows)
                {
                    return tmp;
                }
                while (reader.Read())
                {
                    tmp.Add(new object[]
                    {
                        reader.GetUInt64("SteamID"),
                        reader.GetString("CharName"),
                        reader.GetString("SteamName"),
                    });
                }
                return tmp;
            }
            catch (MySqlException ex)
            {
                HandleException(ex);
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                    reader.Dispose();
                }
            }
            return tmp;
        }

        public void SetOption(CSteamID SteamID, OptionType optionType, bool setValue)
        {
            if (!Initialized)
            {
                Logger.LogError("Error: 无法从数据库加载玩家信息信息，因为插件未正确初始化。");
                return;
            }
            string type = ParseOption(optionType);
            if (type == null)
                return;
            try
            {
                MySqlCommand command = Connection.CreateCommand();
                command.Parameters.AddWithValue("@steamid", SteamID);
                command.Parameters.AddWithValue("@instance", InstanceID);
                command.Parameters.AddWithValue("@setvalue", setValue);
                command.CommandText = "UPDATE `" + TableServer + "` SET " + type + " = @setvalue WHERE SteamID = @steamid AND ServerID = @instance;";
                command.ExecuteNonQuery();
            }
            catch (MySqlException ex)
            {
                HandleException(ex);
            }
        }

        private string ParseOption(OptionType optionType)
        {
            string type = null;
            switch (optionType)
            {
                case OptionType.Buildables:
                    type = "CleanedBuildables";
                    break;
                case OptionType.PlayerFiles:
                    type = "CleanedPlayerData";
                    break;
                default:
                    return type;
            }
            return type;
        }
        //构建PlayerData数据！
        private PlayerData BuildPlayerData(MySqlDataReader reader)
        {
            return new PlayerData((CSteamID)reader.GetUInt64("SteamID"), reader.GetString("SteamName"), reader.GetString("CharName"), Parser.getIPFromUInt32(reader.GetUInt32("IP")), reader.GetInt64("LastLoginGlobal").FromTimeStamp(), reader.GetUInt16("LastServerID"), !reader.IsDBNull("LastServerName") ? reader.GetString("LastServerName") : string.Empty, !reader.IsDBNull("ServerID") ? reader.GetUInt16("ServerID") : (ushort)0, !reader.IsDBNull("LastLoginLocal") ? reader.GetInt64("LastLoginLocal").FromTimeStamp() : (0L).FromTimeStamp(), !reader.IsDBNull("CleanedBuildables") ? reader.GetBoolean("CleanedBuildables") : false, !reader.IsDBNull("CleanedPlayerData") ? reader.GetBoolean("CleanedPlayerData") : false, reader.GetInt32("TotalPlayTime"));
        }

        internal void Clear_player_cache(CSteamID steamID) 
        {
            //删除缓存信息
            Cache.Remove(steamID);
        }


        // 数据保存部分。
        internal void SaveToDB(PlayerData pdata, bool retry = false)
        {
            try
            {
                if (!Initialized)
                {
                    Logger.LogError("Error: 无法保存玩家信息，因为插件未正确初始化。");
                    return;
                }
                if (!pdata.IsValid())
                {
                    Logger.LogError("Error: 无效的玩家数据信息。");
                    return;
                }
                MySqlCommand command = Connection.CreateCommand();
                command.Parameters.AddWithValue("@steamid", pdata.SteamID);
                command.Parameters.AddWithValue("@steamname", pdata.SteamName.Truncate(200));
                command.Parameters.AddWithValue("@charname", pdata.CharacterName.Truncate(200));
                command.Parameters.AddWithValue("@ip", Parser.getUInt32FromIP(pdata.IP));
                command.Parameters.AddWithValue("@instanceid", pdata.ServerID);
                command.Parameters.AddWithValue("@lastinstanceid", pdata.LastServerID);
                command.Parameters.AddWithValue("@lastloginglobal", pdata.LastLoginGlobal.ToTimeStamp());
                command.Parameters.AddWithValue("@totalplaytime", pdata.TotalPlayime);
                command.Parameters.AddWithValue("@lastloginlocal", pdata.LastLoginLocal.ToTimeStamp());
                command.Parameters.AddWithValue("@cleanedbuildables", pdata.CleanedBuildables);
                command.Parameters.AddWithValue("@cleanedplayerdata", pdata.CleanedPlayerData);
                command.CommandText = "INSERT INTO `" + Table + "` (`SteamID`, `SteamName`, `CharName`, `IP`, `LastLoginGlobal`, `TotalPlayTime`, `LastServerID`) VALUES (@steamid, @steamname, @charname, @ip, @lastloginglobal, @totalplaytime, @lastinstanceid) ON DUPLICATE KEY UPDATE `SteamName` = VALUES(`SteamName`), `CharName` = VALUES(`CharName`), `IP` = VALUES(`IP`), `LastLoginGlobal` = VALUES(`LastLoginglobal`), `TotalPlayTime` = VALUES(`TotalPlayTime`), `LastServerID` = VALUES(`LastServerID`);" +
                    "INSERT INTO `" + TableServer + "` (`SteamID`, `ServerID`, `LastLoginLocal`, `CleanedBuildables`, `CleanedPlayerData`) VALUES (@steamid, @instanceid, @lastloginlocal, @cleanedplayerdata, @cleanedplayerdata) ON DUPLICATE KEY UPDATE `LastLoginLocal` = VALUES(`LastLoginLocal`), `CleanedBuildables` = VALUES(`CleanedBuildables`), `CleanedPlayerData` = VALUES(`CleanedPlayerData`);";
                command.ExecuteNonQuery();
                if (Cache.ContainsKey(pdata.SteamID))
                    Cache.Remove(pdata.SteamID);
                pdata.CacheTime = DateTime.Now;
                Cache.Add(pdata.SteamID, pdata);
            }
            catch(MySqlException ex)
            {
                if (!retry)
                {
                    if (HandleException(ex))
                        SaveToDB(pdata, true);
                }
            }
        }
    }

    public enum QueryType
    {
        SteamName,
        CharName,
        Both,
        IP,
    }

    public enum OptionType
    {
        Buildables,
        PlayerFiles,
    }
}

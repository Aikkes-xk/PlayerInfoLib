using Rocket.Core.Logging;
using Rocket.Core.Plugins;
using Rocket.Unturned;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections.Generic;
using Rocket.API.Collections;
using Rocket.API;
using System.Threading.Tasks;

namespace PlayerInfoLibrary
{
    public class PlayerInfoLib : RocketPlugin<PlayerInfoLibConfig>
    {
        public static PlayerInfoLib Instance;
        public static DatabaseManager Database;
        private DateTime lastCheck = DateTime.Now;
        internal static Dictionary<CSteamID, DateTime> LoginTime = new Dictionary<CSteamID, DateTime>();

        protected override void Load()
        {
            Instance = this;
            Database = new DatabaseManager();
            U.Events.OnPlayerConnected += Events_OnPlayerConnected;
            U.Events.OnPlayerDisconnected += Events_OnPlayerDisconnected;
            Logger.Log("PlayerInfoLib[星空-xk 优化版]", ConsoleColor.Green);
            Logger.Log("V1.0", ConsoleColor.Green);
            if (Instance.Configuration.Instance.KeepaliveInterval <= 0)
            {
                Logger.LogWarning("Error:数据库连接状态检测选项必须大于0。");
                Instance.Configuration.Instance.KeepaliveInterval = 10;
            }
            Instance.Configuration.Save();
            if (Database.Initialized)
                Logger.Log(string.Format("PlayerInfoLib插件已加载，服务器实例ID为: {0}", Database.InstanceID), ConsoleColor.Yellow);
            else
                Logger.Log("加载插件时出现问题，请检查您的配置。", ConsoleColor.Red);
        }

        protected override void Unload()
        {
            U.Events.OnPlayerConnected -= Events_OnPlayerConnected;
            U.Events.OnPlayerDisconnected -= Events_OnPlayerDisconnected;

            Database.Unload();
            Database = null;
        }

        private void Events_OnPlayerConnected(UnturnedPlayer player)
        {
            if (LoginTime.ContainsKey(player.CSteamID))
                LoginTime.Remove(player.CSteamID);
            LoginTime.Add(player.CSteamID, DateTime.Now);
            PlayerData pData = Database.QueryById(player.CSteamID, false);
            int totalTime = pData.TotalPlayime;
            DateTime loginTime = PlayerInfoLib.LoginTime[player.CSteamID];
            pData = new PlayerData(player.CSteamID, player.SteamName, player.CharacterName, player.IP, loginTime, Database.InstanceID, Provider.serverName, Database.InstanceID, loginTime, false, false, totalTime);
            var task1 = new Task(() =>
            {
                Database.SaveToDB(pData);
            });
            task1.Start();
            Logger.Log($"玩家:{player.CharacterName}进入服务器 Ip:{player.IP}", ConsoleColor.Green);
        }

        private void Events_OnPlayerDisconnected(UnturnedPlayer player)
        {
            //玩家离开！
            if (player != null)
            {
                if (LoginTime.ContainsKey(player.CSteamID))
                {
                    PlayerData pData = Database.QueryById(player.CSteamID, false);
                    if (pData.IsValid() && pData.IsLocal())
                    {
                        int totalSessionTime = (int)(DateTime.Now - LoginTime[player.CSteamID]).TotalSeconds;
                        pData.TotalPlayime += totalSessionTime;
                        var task1 = new Task(() =>
                        {
                            Database.SaveToDB(pData);
                        });
                        task1.Start();
                    }
                    Database.Clear_player_cache(player.CSteamID);
                    LoginTime.Remove(player.CSteamID);

                }
            }
        }

        public void FixedUpdate()
        {
            if (State == PluginState.Loaded)
            {
                if ((DateTime.Now - lastCheck).TotalMinutes >= Configuration.Instance.KeepaliveInterval)
                {
                    lastCheck = DateTime.Now;
                    var task1 = new Task(() =>
                    {
                        Database.CheckConnection();
                    });
                    task1.Start();
                }
            }
        }

        public override TranslationList DefaultTranslations
        {
            get
            {
                return new TranslationList
                {
                    { "too_many_parameters", "参数太多。" },
                    { "investigate_help", CommandInvestigate.syntax + " - " + CommandInvestigate.help },
                    { "rnint_help", CommandRnInstance.syntax + " - " + CommandRnInstance.help },
                    { "invalid_page", "错误: 无效参数！." },
                    { "number_of_records_found", "{0} 找到记录: {1}, 页: {2} 的 {3}" },
                    { "delint_invalid", "错误，无效的实例ID。" },
                    { "delint_not_found", "错误：在数据库中找不到实例ID。在数据库中找不到实例ID。" },
                    { "rnint_success", "成功更改数据库中该服务器的实例名称，现在应该可以重启服务器了。" },
                    { "rnint_not_found", "错误：未能将新的实例名称设置为数据库！" },
                };
            }
        }
    }
}

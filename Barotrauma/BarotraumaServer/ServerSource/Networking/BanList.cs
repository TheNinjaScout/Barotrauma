﻿using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Net;
using Barotrauma.Steam;

namespace Barotrauma.Networking
{
    partial class BannedPlayer
    {
        private static UInt16 LastIdentifier = 0;

        public BannedPlayer(string name, string endPoint, string reason, DateTime? expirationTime)
        {
            this.Name = name;
            this.EndPoint = endPoint;
            ParseEndPointAsSteamId();
            this.Reason = reason;
            this.ExpirationTime = expirationTime;
            this.UniqueIdentifier = LastIdentifier; LastIdentifier++;

            this.IsRangeBan = EndPoint.IndexOf(".x") > -1;
        }

        public BannedPlayer(string name, ulong steamID, string reason, DateTime? expirationTime)
        {
            this.Name = name;
            this.SteamID = steamID;
            this.Reason = reason;
            this.ExpirationTime = expirationTime;
            this.UniqueIdentifier = LastIdentifier; LastIdentifier++;

            this.IsRangeBan = false;

            this.EndPoint = "";
        }

        public bool CompareTo(string endpointCompare)
        {
            if (string.IsNullOrEmpty(EndPoint) || string.IsNullOrEmpty(EndPoint)) { return false; }
            if (!IsRangeBan)
            {
                return endpointCompare == EndPoint;
            }
            else
            {
                int rangeBanIndex = EndPoint.IndexOf(".x");
                if (endpointCompare.Length < rangeBanIndex) return false;
                return endpointCompare.Substring(0, rangeBanIndex) == EndPoint.Substring(0, rangeBanIndex);
            }
        }

        public bool CompareTo(IPAddress ipCompare)
        {
            if (string.IsNullOrEmpty(EndPoint) || ipCompare == null) { return false; }
            if (ipCompare.IsIPv4MappedToIPv6 && CompareTo(ipCompare.MapToIPv4NoThrow().ToString()))
            {
                return true;
            }
            return CompareTo(ipCompare.ToString());
        }
    }

    partial class BanList
    {
        const string SavePath = "Data/bannedplayers.txt";

        partial void InitProjectSpecific()
        {
            if (!File.Exists(SavePath)) { return; }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(SavePath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to open the list of banned players in " + SavePath, e);
                return;
            }

            foreach (string line in lines)
            {
                string[] separatedLine = line.Split(',');
                if (separatedLine.Length < 2) continue;

                string name = separatedLine[0];
                string identifier = separatedLine[1];

                DateTime? expirationTime = null;
                if (separatedLine.Length > 2 && !string.IsNullOrEmpty(separatedLine[2]))
                {
                    if (DateTime.TryParse(separatedLine[2], out DateTime parsedTime))
                    {
                        expirationTime = parsedTime;
                    }
                }
                string reason = separatedLine.Length > 3 ? string.Join(",", separatedLine.Skip(3)) : "";

                if (expirationTime.HasValue && DateTime.Now > expirationTime.Value) continue;

                if (identifier.Contains(".") || identifier.Contains(":"))
                {
                    //identifier is an ip
                    bannedPlayers.Add(new BannedPlayer(name, identifier, reason, expirationTime));
                }
                else
                {
                    //identifier should be a steam id
                    if (ulong.TryParse(identifier, out ulong steamID))
                    {
                        bannedPlayers.Add(new BannedPlayer(name, steamID, reason, expirationTime));
                    }
                    else
                    {
                        DebugConsole.ThrowError("Error in banlist: \"" + identifier + "\" is not a valid IP or a Steam ID");
                    }
                }
            }
        }

        public bool IsBanned(IPAddress IP, ulong steamID, ulong ownerSteamID, out string reason)
        {
            reason = string.Empty;
            if (IPAddress.IsLoopback(IP)) { return false; }
            var bannedPlayer = bannedPlayers.Find(bp =>
                bp.CompareTo(IP) ||
                (steamID > 0 && (bp.SteamID == steamID || SteamManager.SteamIDStringToUInt64(bp.EndPoint) == steamID)) ||
                (ownerSteamID > 0 && (bp.SteamID == ownerSteamID || SteamManager.SteamIDStringToUInt64(bp.EndPoint) == ownerSteamID)));
            reason = bannedPlayer?.Reason;
            return bannedPlayer != null;
        }

        public bool IsBanned(IPAddress IP, out string reason)
        {
            reason = string.Empty;
            if (IPAddress.IsLoopback(IP)) { return false; }
            bannedPlayers.RemoveAll(bp => bp.ExpirationTime.HasValue && DateTime.Now > bp.ExpirationTime.Value);
            var bannedPlayer = bannedPlayers.Find(bp => bp.CompareTo(IP));
            reason = bannedPlayer?.Reason;
            return bannedPlayer != null;
        }

        public bool IsBanned(ulong steamID, out string reason)
        {
            reason = string.Empty;
            bannedPlayers.RemoveAll(bp => bp.ExpirationTime.HasValue && DateTime.Now > bp.ExpirationTime.Value);
            var bannedPlayer = bannedPlayers.Find(bp =>
                steamID > 0 &&
                (bp.SteamID == steamID || SteamManager.SteamIDStringToUInt64(bp.EndPoint) == steamID));
            reason = bannedPlayer?.Reason;
            return bannedPlayer != null;
        }

        public void BanPlayer(string name, IPAddress ip, string reason, TimeSpan? duration)
        {
            string ipStr = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4NoThrow().ToString() : ip.ToString();
            BanPlayer(name, ipStr, 0, reason, duration);
        }

        public void BanPlayer(string name, string endPoint, string reason, TimeSpan? duration)
        {
            BanPlayer(name, endPoint, 0, reason, duration);
        }

        public void BanPlayer(string name, ulong steamID, string reason, TimeSpan? duration)
        {
            if (steamID == 0) { return; }
            BanPlayer(name, "", steamID, reason, duration);
        }

        private void BanPlayer(string name, string endPoint, ulong steamID, string reason, TimeSpan? duration)
        {
            var existingBan = bannedPlayers.Find(bp => bp.EndPoint == endPoint && bp.SteamID == steamID);
            if (existingBan != null)
            {
                if (!duration.HasValue) return;

                DebugConsole.Log("Set \"" + name + "\"'s ban duration to " + duration.Value);
                existingBan.ExpirationTime = DateTime.Now + duration.Value;
                Save();
                return;
            }

            System.Diagnostics.Debug.Assert(!name.Contains(','));

            string logMsg = "Banned " + name;
            if (!string.IsNullOrEmpty(reason)) logMsg += ", reason: " + reason;
            if (duration.HasValue) logMsg += ", duration: " + duration.Value.ToString();

            DebugConsole.Log(logMsg);

            DateTime? expirationTime = null;
            if (duration.HasValue)
            {
                expirationTime = DateTime.Now + duration.Value;
            }

            if (!string.IsNullOrEmpty(endPoint))
            {
                bannedPlayers.Add(new BannedPlayer(name, endPoint, reason, expirationTime));
            }
            else if (steamID > 0)
            {
                bannedPlayers.Add(new BannedPlayer(name, steamID, reason, expirationTime));
            }
            else
            {
                DebugConsole.ThrowError("Failed to ban a client (no valid IP or Steam ID given)");
                return;
            }

            Save();
        }

        public void UnbanPlayer(string name)
        {
            name = name.ToLower();
            var player = bannedPlayers.Find(bp => bp.Name.ToLower() == name);
            if (player == null)
            {
                DebugConsole.Log("Could not unban player \"" + name + "\". Matching player not found.");
            }
            else
            {
                RemoveBan(player);
            }
        }

        public void UnbanEndPoint(string endPoint)
        {
            ulong steamId = SteamManager.SteamIDStringToUInt64(endPoint);
            var player = bannedPlayers.Find(bp =>
                bp.EndPoint == endPoint ||
                (steamId != 0 && steamId == SteamManager.SteamIDStringToUInt64(bp.EndPoint)));
            if (player == null)
            {
                DebugConsole.Log("Could not unban endpoint \"" + endPoint + "\". Matching player not found.");
            }
            else
            {
                RemoveBan(player);
            }
        }

        private void RemoveBan(BannedPlayer banned)
        {
            DebugConsole.Log("Removing ban from " + banned.Name);
            GameServer.Log("Removing ban from " + banned.Name, ServerLog.MessageType.ServerMessage);

            bannedPlayers.Remove(banned);

            Save();
        }

        private void RangeBan(BannedPlayer banned)
        {
            banned.EndPoint = ToRange(banned.EndPoint);

            BannedPlayer bp;
            while ((bp = bannedPlayers.Find(x => banned.CompareTo(x.EndPoint))) != null)
            {
                //remove all specific bans that are now covered by the rangeban
                bannedPlayers.Remove(bp);
            }

            bannedPlayers.Add(banned);

            Save();
        }

        public void Save()
        {
            GameServer.Log("Saving banlist", ServerLog.MessageType.ServerMessage);

            bannedPlayers.RemoveAll(bp => bp.ExpirationTime.HasValue && DateTime.Now > bp.ExpirationTime.Value);

            List<string> lines = new List<string>();
            foreach (BannedPlayer banned in bannedPlayers)
            {
                string line = banned.Name;
                line += "," + ((banned.SteamID > 0) ? SteamManager.SteamIDUInt64ToString(banned.SteamID) : banned.EndPoint);
                line += "," + (banned.ExpirationTime.HasValue ? banned.ExpirationTime.Value.ToString() : "");
                if (!string.IsNullOrWhiteSpace(banned.Reason)) line += "," + banned.Reason;

                lines.Add(line);
            }

            try
            {
                File.WriteAllLines(SavePath, lines);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving the list of banned players to " + SavePath + " failed", e);
            }
        }

        public void ServerAdminWrite(IWriteMessage outMsg, Client c)
        {
            try
            {
                if (outMsg == null) { throw new ArgumentException("OutMsg was null"); }
                if (GameMain.Server == null) { throw new Exception("GameMain.Server was null"); }

                if (!c.HasPermission(ClientPermissions.Ban))
                {
                    outMsg.Write(false); outMsg.WritePadBits();
                    return;
                }

                outMsg.Write(true);
                outMsg.Write(c.Connection == GameMain.Server.OwnerConnection);

                outMsg.WritePadBits();
                outMsg.WriteVariableUInt32((UInt32)bannedPlayers.Count);
                for (int i = 0; i < bannedPlayers.Count; i++)
                {
                    BannedPlayer bannedPlayer = bannedPlayers[i];

                    outMsg.Write(bannedPlayer.Name);
                    outMsg.Write(bannedPlayer.UniqueIdentifier);
                    outMsg.Write(bannedPlayer.IsRangeBan);
                    outMsg.Write(bannedPlayer.ExpirationTime != null);
                    outMsg.WritePadBits();
                    if (bannedPlayer.ExpirationTime != null)
                    {
                        double hoursFromNow = (bannedPlayer.ExpirationTime.Value - DateTime.Now).TotalHours;
                        outMsg.Write(hoursFromNow);
                    }

                    outMsg.Write(bannedPlayer.Reason ?? "");

                    if (c.Connection == GameMain.Server.OwnerConnection)
                    {
                        outMsg.Write(bannedPlayer.EndPoint);
                        outMsg.Write(bannedPlayer.SteamID);
                    }
                }
            }

            catch (Exception e)
            {
                string errorMsg = "Error while writing banlist. {" + e + "}\n" + e.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce("Banlist.ServerAdminWrite", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                throw;
            }
        }

        public bool ServerAdminRead(IReadMessage incMsg, Client c)
        {
            if (!c.HasPermission(ClientPermissions.Ban))
            {
                UInt16 removeCount = incMsg.ReadUInt16();
                incMsg.BitPosition += removeCount * 4 * 8;
                UInt16 rangeBanCount = incMsg.ReadUInt16();
                incMsg.BitPosition += rangeBanCount * 4 * 8;
                return false;
            }
            else
            {
                UInt16 removeCount = incMsg.ReadUInt16();
                for (int i = 0; i < removeCount; i++)
                {
                    UInt16 id = incMsg.ReadUInt16();
                    BannedPlayer bannedPlayer = bannedPlayers.Find(p => p.UniqueIdentifier == id);
                    if (bannedPlayer != null)
                    {
                        GameServer.Log(GameServer.ClientLogName(c) + " unbanned " + bannedPlayer.Name + " (" + bannedPlayer.EndPoint + ")", ServerLog.MessageType.ConsoleUsage);
                        RemoveBan(bannedPlayer);
                    }
                }
                Int16 rangeBanCount = incMsg.ReadInt16();
                for (int i = 0; i < rangeBanCount; i++)
                {
                    UInt16 id = incMsg.ReadUInt16();
                    BannedPlayer bannedPlayer = bannedPlayers.Find(p => p.UniqueIdentifier == id);
                    if (bannedPlayer != null)
                    {
                        GameServer.Log(GameServer.ClientLogName(c) + " rangebanned " + bannedPlayer.Name + " (" + bannedPlayer.EndPoint + ")", ServerLog.MessageType.ConsoleUsage);
                        RangeBan(bannedPlayer);
                    }
                }

                return removeCount > 0 || rangeBanCount > 0;
            }
        }
    }
}

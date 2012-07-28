/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;

using Aurora.Framework;
using Aurora.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using FriendInfo = OpenSim.Services.Interfaces.FriendInfo;
using Aurora.DataManager;
using Aurora.Simulation.Base;

using OpenMetaverse;
using Nini.Config;

namespace Aurora.Addon.HyperGrid
{
    /// <summary>
    /// This service is for HG1.5 only, to make up for the fact that clients don't
    /// keep any private information in themselves, and that their 'home service'
    /// needs to do it for them.
    /// Once we have better clients, this shouldn't be needed.
    /// </summary>
    public class UserAgentService : IUserAgentService, IService
    {
        // This will need to go into a DB table
        static Dictionary<UUID, TravelingAgentInfo> m_TravelingAgents = new Dictionary<UUID, TravelingAgentInfo> ();

        protected static IGridService m_GridService;
        protected static IRegistryCore m_registry;
        protected static IAsyncMessagePostService m_asyncPostService;
        protected static GatekeeperServiceConnector m_GatekeeperConnector;
        protected static IGatekeeperService m_GatekeeperService;
        protected static IFriendsService m_FriendsService;
        protected static IAgentInfoService m_PresenceService;
        protected static IUserAccountService m_UserAccountService;

        protected static string m_GridName;

        protected static bool m_BypassClientVerification;

        public void Initialize (IConfigSource config, IRegistryCore registry)
        {
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            m_registry = registry;
            registry.RegisterModuleInterface<IUserAgentService> (this);
        }

        public void Start (IConfigSource config, IRegistryCore registry)
        {
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            MainConsole.Instance.DebugFormat ("[HOME USERS SECURITY]: Starting...");

            IConfig serverConfig = config.Configs["UserAgentService"];
            if (serverConfig == null || !serverConfig.GetBoolean ("Enabled", false))
                return;

            m_BypassClientVerification = serverConfig.GetBoolean ("BypassClientVerification", false);

            m_GridName = serverConfig.GetString ("ExternalName", string.Empty);
            if (m_GridName == string.Empty)
            {
                IHttpServer server = MainServer.Instance;
                m_GridName = server.FullHostName + ":" + server.Port + "/";
            }
        }

        public void FinishedStartup ()
        {
            if (m_registry == null)
                return;//Not enabled
            m_GridService = m_registry.RequestModuleInterface<IGridService> ();
            m_asyncPostService = m_registry.RequestModuleInterface<IAsyncMessagePostService> ();
            m_GatekeeperConnector = new GatekeeperServiceConnector (m_registry.RequestModuleInterface<IAssetService> ());
            m_GatekeeperService = m_registry.RequestModuleInterface<IGatekeeperService> ();
            m_FriendsService = m_registry.RequestModuleInterface<IFriendsService> ();
            m_PresenceService = m_registry.RequestModuleInterface<IAgentInfoService> ();
            m_UserAccountService = m_registry.RequestModuleInterface<IUserAccountService> ();
        }

        public GridRegion GetHomeRegion (AgentCircuitData circuit, out Vector3 position, out Vector3 lookAt)
        {
            if (circuit.ServiceURLs.ContainsKey ("HomeURI"))
            {
                IUserAgentService userAgentService = new UserAgentServiceConnector (circuit.ServiceURLs["HomeURI"].ToString ());
                GridRegion region = userAgentService.GetHomeRegion (circuit, out position, out lookAt);
                if (region != null)
                {
                    Uri uri = null;
                    if (!circuit.ServiceURLs.ContainsKey ("HomeURI") ||
                        (circuit.ServiceURLs.ContainsKey ("HomeURI") && !Uri.TryCreate (circuit.ServiceURLs["HomeURI"].ToString (), UriKind.Absolute, out uri)))
                        return null;

                    region.ExternalHostName = uri.Host;
                    region.HttpPort = (uint)uri.Port;
                    region.ServerURI = region.ServerURI;
                    region.RegionName = string.Empty;
                    region.InternalEndPoint = new System.Net.IPEndPoint (System.Net.IPAddress.Parse ("0.0.0.0"), (int)0);
                    bool isComingHome = userAgentService.AgentIsComingHome (circuit.SessionID, m_GridName);
                    return region;
                }
            }
            return GetHomeRegion (circuit.AgentID, out position, out lookAt);
        }

        public GridRegion GetHomeRegion (UUID userID, out Vector3 position, out Vector3 lookAt)
        {
            position = new Vector3 (128, 128, 0);
            lookAt = Vector3.UnitY;

            MainConsole.Instance.DebugFormat ("[USER AGENT SERVICE]: Request to get home region of user {0}", userID);

            GridRegion home = null;
            UserInfo uinfo = m_PresenceService.GetUserInfo (userID.ToString ());
            if (uinfo != null)
            {
                if (uinfo.HomeRegionID != UUID.Zero)
                {
                    home = m_GridService.GetRegionByUUID(null, uinfo.HomeRegionID);
                    position = uinfo.HomePosition;
                    lookAt = uinfo.HomeLookAt;
                }
                if (home == null || ((home.Flags & (int)Aurora.Framework.RegionFlags.Safe) 
                    != (int)Aurora.Framework.RegionFlags.Safe))
                {
                    home = m_GatekeeperService.GetHyperlinkRegion (UUID.Zero);
                    if (home != null)
                    {
                        position = new Vector3(home.RegionSizeX / 2, home.RegionSizeY / 2, 20);
                        lookAt = Vector3.Zero;
                    }
                }
            }

            return home;
        }

        public bool LoginAgentToGrid (AgentCircuitData agentCircuit, GridRegion gatekeeper, GridRegion finalDestination, IPEndPoint clientIP, out string reason)
        {
            MainConsole.Instance.DebugFormat ("[USER AGENT SERVICE]: Request to login user {0} (@{1}) to grid {2}",
                agentCircuit.AgentID, ((clientIP == null) ? "stored IP" : clientIP.Address.ToString ()), gatekeeper.ServerURI);
            // Take the IP address + port of the gatekeeper (reg) plus the info of finalDestination
            GridRegion region = new GridRegion ();
            region.FromOSD (gatekeeper.ToOSD ());
            region.ServerURI = gatekeeper.ServerURI;
            region.ExternalHostName = finalDestination.ExternalHostName;
            region.InternalEndPoint = finalDestination.InternalEndPoint;
            region.RegionName = finalDestination.RegionName;
            region.RegionID = finalDestination.RegionID;
            region.RegionLocX = finalDestination.RegionLocX;
            region.RegionLocY = finalDestination.RegionLocY;

            // Generate a new service session
            agentCircuit.ServiceSessionID = region.ServerURI + ";" + UUID.Random ();
            TravelingAgentInfo old = UpdateTravelInfo (agentCircuit, region);

            bool success = false;
            string myExternalIP = string.Empty;
            string gridName = gatekeeper.ServerURI;

            MainConsole.Instance.DebugFormat ("[USER AGENT SERVICE]: this grid: {0}, desired grid: {1}", m_GridName, gridName);

            if (m_GridName == gridName)
                success = m_GatekeeperService.LoginAgent (agentCircuit, finalDestination, out reason);
            else
            {
                success = m_GatekeeperConnector.CreateAgent (region, agentCircuit, (uint)TeleportFlags.ViaLogin, out myExternalIP, out reason);
                if (success)
                    // Report them as nowhere with the LOGIN_STATUS_LOCKED so that they don't get logged out automatically after an hour of not responding via HG
                    m_PresenceService.SetLastPosition (agentCircuit.AgentID.ToString (), AgentInfoHelpers.LOGIN_STATUS_LOCKED, Vector3.Zero, Vector3.Zero);
            }

            if (!success)
            {
                MainConsole.Instance.DebugFormat ("[USER AGENT SERVICE]: Unable to login user {0} to grid {1}, reason: {2}",
                    agentCircuit.AgentID, region.ServerURI, reason);

                // restore the old travel info
                lock (m_TravelingAgents)
                {
                    if (old == null)
                        m_TravelingAgents.Remove (agentCircuit.SessionID);
                    else
                        m_TravelingAgents[agentCircuit.SessionID] = old;
                }

                return false;
            }
            else
                reason = "";

            MainConsole.Instance.DebugFormat ("[USER AGENT SERVICE]: Gatekeeper sees me as {0}", myExternalIP);
            // else set the IP addresses associated with this client
            if (clientIP != null)
                m_TravelingAgents[agentCircuit.SessionID].ClientIPAddress = clientIP.Address.ToString ();
            m_TravelingAgents[agentCircuit.SessionID].MyIpAddress = myExternalIP;

            return true;
        }

        public bool LoginAgentToGrid (AgentCircuitData agentCircuit, GridRegion gatekeeper, GridRegion finalDestination, out string reason)
        {
            reason = string.Empty;
            return LoginAgentToGrid (agentCircuit, gatekeeper, finalDestination, null, out reason);
        }

        private void SetClientIP (UUID sessionID, string ip)
        {
            if (m_TravelingAgents.ContainsKey (sessionID))
            {
                MainConsole.Instance.DebugFormat ("[USER AGENT SERVICE]: Setting IP {0} for session {1}", ip, sessionID);
                m_TravelingAgents[sessionID].ClientIPAddress = ip;
            }
        }

        TravelingAgentInfo UpdateTravelInfo (AgentCircuitData agentCircuit, GridRegion region)
        {
            TravelingAgentInfo travel = new TravelingAgentInfo ();
            TravelingAgentInfo old = null;
            lock (m_TravelingAgents)
            {
                if (m_TravelingAgents.ContainsKey (agentCircuit.SessionID))
                {
                    // Very important! Override whatever this agent comes with.
                    // UserAgentService always sets the IP for every new agent
                    // with the original IP address.
                    agentCircuit.IPAddress = m_TravelingAgents[agentCircuit.SessionID].ClientIPAddress;

                    old = m_TravelingAgents[agentCircuit.SessionID];
                }

                m_TravelingAgents[agentCircuit.SessionID] = travel;
            }
            travel.UserID = agentCircuit.AgentID;
            travel.GridExternalName = region.ServerURI;
            travel.ServiceToken = agentCircuit.ServiceSessionID;
            if (old != null)
                travel.ClientIPAddress = old.ClientIPAddress;

            return old;
        }

        public void LogoutAgent (UUID userID, UUID sessionID)
        {
            MainConsole.Instance.DebugFormat ("[USER AGENT SERVICE]: User {0} logged out", userID);

            lock (m_TravelingAgents)
            {
                List<UUID> travels = new List<UUID> ();
                foreach (KeyValuePair<UUID, TravelingAgentInfo> kvp in m_TravelingAgents)
                    if (kvp.Value == null) // do some clean up
                        travels.Add (kvp.Key);
                    else if (kvp.Value.UserID == userID)
                        travels.Add (kvp.Key);
                foreach (UUID session in travels)
                    m_TravelingAgents.Remove (session);
            }

            m_PresenceService.SetLoggedIn (userID.ToString (), false, true, UUID.Zero);
        }

        // We need to prevent foreign users with the same UUID as a local user
        public bool AgentIsComingHome (UUID sessionID, string thisGridExternalName)
        {
            if (!m_TravelingAgents.ContainsKey (sessionID))
                return false;

            TravelingAgentInfo travel = m_TravelingAgents[sessionID];

            string a = travel.GridExternalName, b = thisGridExternalName;
            try
            {
                a = NetworkUtils.GetHostFromDNS(travel.GridExternalName).ToString().ToLower();
            }
            catch
            {
                a = travel.GridExternalName;
            }
            try
            {
                b = NetworkUtils.GetHostFromDNS(thisGridExternalName).ToString().ToLower();
            }
            catch
            {
                b = thisGridExternalName;
            }

            return a == b;
        }

        public bool VerifyClient (UUID sessionID, string reportedIP)
        {
            if (m_BypassClientVerification)
                return true;

            MainConsole.Instance.InfoFormat ("[USER AGENT SERVICE]: Verifying Client session {0} with reported IP {1}.",
                sessionID, reportedIP);

            if (m_TravelingAgents.ContainsKey (sessionID))
            {
                bool result = m_TravelingAgents[sessionID].ClientIPAddress == reportedIP ||
                    m_TravelingAgents[sessionID].MyIpAddress == reportedIP; // NATed

                MainConsole.Instance.DebugFormat ("[USER AGENT SERVICE]: Comparing {0} with login IP {1} and MyIP {1}; result is {3}",
                                    reportedIP, m_TravelingAgents[sessionID].ClientIPAddress, m_TravelingAgents[sessionID].MyIpAddress, result);

                return result;
            }

            return false;
        }

        public bool VerifyAgent (UUID sessionID, string token)
        {
            if (m_TravelingAgents.ContainsKey (sessionID))
            {
                MainConsole.Instance.DebugFormat ("[USER AGENT SERVICE]: Verifying agent token {0} against {1}", token, m_TravelingAgents[sessionID].ServiceToken);
                return m_TravelingAgents[sessionID].ServiceToken == token;
            }

            MainConsole.Instance.DebugFormat ("[USER AGENT SERVICE]: Token verification for session {0}: no such session", sessionID);

            return false;
        }

        public bool VerifyAgent (AgentCircuitData circuit)
        {
            if (circuit.ServiceURLs.ContainsKey ("HomeURI"))
            {
                string url = circuit.ServiceURLs["HomeURI"].ToString ();
                UserAgentServiceConnector security = new UserAgentServiceConnector (url);
                return security.VerifyAgent (circuit.SessionID, circuit.ServiceSessionID);
            }
            else
                MainConsole.Instance.DebugFormat ("[HG ENTITY TRANSFER MODULE]: Agent {0} {1} does not have a HomeURI OH NO!", circuit.firstname, circuit.lastname);
            return VerifyAgent (circuit.SessionID, circuit.ServiceSessionID);
        }

        public bool RemoteStatusNotification (FriendInfo friend, UUID userID, bool online)
        {
            string url, first, last, secret;
            UUID FriendToInform;
            if (HGUtil.ParseUniversalUserIdentifier (friend.Friend, out FriendToInform, out url, out first, out last, out secret))
            {
                UserAgentServiceConnector connector = new UserAgentServiceConnector (url);
                List<UUID> informedFriends = connector.StatusNotification (new List<string> (new string[1] { FriendToInform.ToString () }),
                    userID, online);
                return informedFriends.Count > 0;
            }
            return false;
        }

        public List<UUID> StatusNotification (List<string> friends, UUID foreignUserID, bool online)
        {
            if (m_FriendsService == null || m_PresenceService == null)
            {
                MainConsole.Instance.WarnFormat ("[USER AGENT SERVICE]: Unable to perform status notifications because friends or presence services are missing");
                return new List<UUID> ();
            }

            List<UUID> localFriendsOnline = new List<UUID> ();

            MainConsole.Instance.DebugFormat ("[USER AGENT SERVICE]: Status notification: foreign user {0} wants to notify {1} local friends", foreignUserID, friends.Count);

            // First, let's double check that the reported friends are, indeed, friends of that user
            // And let's check that the secret matches
            List<string> usersToBeNotified = new List<string> ();
            foreach (string uui in friends)
            {
                UUID localUserID;
                string secret = string.Empty, tmp = string.Empty;
                if (HGUtil.ParseUniversalUserIdentifier (uui, out localUserID, out tmp, out tmp, out tmp, out secret))
                {
                    List<FriendInfo> friendInfos = m_FriendsService.GetFriends (localUserID);
                    foreach (FriendInfo finfo in friendInfos)
                    {
                        if (finfo.Friend.StartsWith (foreignUserID.ToString ()) && finfo.Friend.EndsWith (secret))
                        {
                            // great!
                            usersToBeNotified.Add (localUserID.ToString ());
                        }
                    }
                }
            }

            // Now, let's send the notifications
            MainConsole.Instance.DebugFormat ("[USER AGENT SERVICE]: Status notification: user has {0} local friends", usersToBeNotified.Count);

            // First, let's send notifications to local users who are online in the home grid

            //Send "" because if we pass the UUID, it will get the locations for all friends, even on the grid they came from
            List<UserInfo> friendSessions = m_PresenceService.GetUserInfos (usersToBeNotified);
            foreach (UserInfo friend in friendSessions)
            {
                if (friend.IsOnline)
                {
                    GridRegion ourRegion = m_GridService.GetRegionByUUID(null, friend.CurrentRegionID);
                    if (ourRegion != null)
                        m_asyncPostService.Post (ourRegion.RegionHandle,
                            SyncMessageHelper.AgentStatusChange (foreignUserID, UUID.Parse (friend.UserID), true));
                }
            }

            // Lastly, let's notify the rest who may be online somewhere else
            foreach (string user in usersToBeNotified)
            {
                UUID id = new UUID (user);
                if (m_TravelingAgents.ContainsKey (id) && m_TravelingAgents[id].GridExternalName != m_GridName)
                {
                    string url = m_TravelingAgents[id].GridExternalName;
                    // forward
                    MainConsole.Instance.WarnFormat ("[USER AGENT SERVICE]: User {0} is visiting {1}. HG Status notifications still not implemented.", user, url);
                }
            }

            // and finally, let's send the online friends
            if (online)
            {
                return localFriendsOnline;
            }
            else
                return new List<UUID> ();
        }

        public List<UUID> GetOnlineFriends (UUID foreignUserID, List<string> friends)
        {
            List<UUID> online = new List<UUID> ();

            if (m_FriendsService == null || m_PresenceService == null)
            {
                MainConsole.Instance.WarnFormat ("[USER AGENT SERVICE]: Unable to get online friends because friends or presence services are missing");
                return online;
            }

            MainConsole.Instance.DebugFormat ("[USER AGENT SERVICE]: Foreign user {0} wants to know status of {1} local friends", foreignUserID, friends.Count);

            // First, let's double check that the reported friends are, indeed, friends of that user
            // And let's check that the secret matches and the rights
            List<string> usersToBeNotified = new List<string> ();
            foreach (string uui in friends)
            {
                UUID localUserID;
                string secret = string.Empty, tmp = string.Empty;
                if (HGUtil.ParseUniversalUserIdentifier (uui, out localUserID, out tmp, out tmp, out tmp, out secret))
                {
                    List<FriendInfo> friendInfos = m_FriendsService.GetFriends (localUserID);
                    foreach (FriendInfo finfo in friendInfos)
                    {
                        if (finfo.Friend.StartsWith (foreignUserID.ToString ()) && finfo.Friend.EndsWith (secret) &&
                            (finfo.TheirFlags & (int)FriendRights.CanSeeOnline) != 0 && (finfo.TheirFlags != -1))
                        {
                            // great!
                            usersToBeNotified.Add (localUserID.ToString ());
                        }
                    }
                }
            }

            // Now, let's find out their status
            MainConsole.Instance.DebugFormat ("[USER AGENT SERVICE]: GetOnlineFriends: user has {0} local friends with status rights", usersToBeNotified.Count);

            // First, let's send notifications to local users who are online in the home grid
            List<UserInfo> friendSessions = m_PresenceService.GetUserInfos (usersToBeNotified);
            if (friendSessions != null && friendSessions.Count > 0)
            {
                foreach (UserInfo pi in friendSessions)
                {
                    UUID presenceID;
                    if (UUID.TryParse (pi.UserID, out presenceID))
                        online.Add (presenceID);
                }
            }

            return online;
        }

        public Dictionary<string, object> GetUserInfo(UUID userID)
        {
            Dictionary<string, object> info = new Dictionary<string, object>();

            if (m_UserAccountService == null)
            {
                MainConsole.Instance.WarnFormat("[USER AGENT SERVICE]: Unable to get user flags because user account service is missing");
                info["result"] = "fail";
                info["message"] = "UserAccountService is missing!";
                return info;
            }

            UserAccount account = m_UserAccountService.GetUserAccount(null, userID);

            if (account != null)
            {
                info.Add("user_flags", (object)account.UserFlags);
                info.Add("user_created", (object)account.Created);
                info.Add("user_title", (object)account.UserTitle);
                info.Add("result", "success");
            }

            return info;
        }

        public Dictionary<string, object> GetServerURLs (UUID userID)
        {
            if (m_UserAccountService == null)
            {
                MainConsole.Instance.WarnFormat ("[USER AGENT SERVICE]: Unable to get server URLs because user account service is missing");
                return new Dictionary<string, object> ();
            }
            UserAccount account = m_UserAccountService.GetUserAccount (null /*!!!*/, userID);
            if (account != null)
                return account.ServiceURLs;

            return new Dictionary<string, object> ();
        }

        public string LocateUser (UUID userID)
        {
            foreach (TravelingAgentInfo t in m_TravelingAgents.Values)
            {
                if (t == null)
                {
                    MainConsole.Instance.ErrorFormat ("[USER AGENT SERVICE]: Oops! Null TravelingAgentInfo. Please report this on mantis");
                    continue;
                }
                if (t.UserID == userID && !m_GridName.Equals (t.GridExternalName))
                    return t.GridExternalName;
            }

            return string.Empty;
        }

        public string GetUUI (UUID userID, UUID targetUserID)
        {
            // Let's see if it's a local user
            UserAccount account = m_UserAccountService.GetUserAccount (null, targetUserID);
            if (account != null)
                return targetUserID.ToString () + ";" + m_GridName + ";" + account.FirstName + " " + account.LastName;

            // Let's try the list of friends
            List<FriendInfo> friends = m_FriendsService.GetFriends(userID);
            if (friends != null && friends.Count > 0)
            {
                foreach (FriendInfo f in friends)
                    if (f.Friend.StartsWith (targetUserID.ToString ()))
                    {
                        // Let's remove the secret 
                        UUID id;
                        string tmp = string.Empty, secret = string.Empty;
                        if (HGUtil.ParseUniversalUserIdentifier (f.Friend, out id, out tmp, out tmp, out tmp, out secret))
                            return f.Friend.Replace (secret, "0");
                    }
            }

            return string.Empty;
        }
    }

    class TravelingAgentInfo
    {
        public UUID UserID;
        public string GridExternalName = string.Empty;
        public string ServiceToken = string.Empty;
        public string ClientIPAddress = string.Empty; // as seen from this user agent service
        public string MyIpAddress = string.Empty; // the user agent service's external IP, as seen from the next gatekeeper
    }

}

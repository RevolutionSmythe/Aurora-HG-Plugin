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
using System.Text.RegularExpressions;

using Aurora.Framework;
using Aurora.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

using OpenMetaverse;
using OpenMetaverse.StructuredData;

using Nini.Config;
using Aurora.Simulation.Base;

namespace Aurora.Addon.HyperGrid
{
    public class GatekeeperService : IService, IGatekeeperService
    {
        private static IGridService m_GridService;
        private static IRegistryCore m_registry;
        private static IAgentInfoService m_PresenceService;
        private static IUserAccountService m_UserAccountService;
        private static IUserAgentService m_UserAgentService;
        private static ISimulationService m_SimulationService;
        private static ICapsService m_CapsService;
        private static IUserFinder m_userFinder;

        protected string m_AllowedClients = string.Empty;
        protected string m_DeniedClients = string.Empty;
        protected string m_defaultRegion = string.Empty;

        private static bool m_AllowTeleportsToAnyRegion;
        private static string m_ExternalName;
        private static GridRegion m_DefaultGatewayRegion;
        private static bool m_foundDefaultRegion = false;

        public void Initialize (IConfigSource config, IRegistryCore registry)
        {
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            IConfig serverConfig = config.Configs["GatekeeperService"];
            bool enabled = false;
            if (serverConfig != null)
            {
                m_AllowTeleportsToAnyRegion = hgConfig.GetBoolean ("AllowTeleportsToAnyRegion", true);
                m_defaultRegion = hgConfig.GetString ("DefaultTeleportRegion", "");
                enabled = serverConfig.GetBoolean ("Enabled", enabled);
            }
            if (!enabled)
                return;

            m_registry = registry;
            
            IHttpServer server = MainServer.Instance;
            m_ExternalName = server.FullHostName + ":" + server.Port + "/";
            Uri m_Uri = new Uri (m_ExternalName);
            IPAddress ip = NetworkUtils.GetHostFromDNS(m_Uri.Host);
            m_ExternalName = m_ExternalName.Replace (m_Uri.Host, ip.ToString ());
            registry.RegisterModuleInterface<IGatekeeperService> (this);
        }

        public void Start (IConfigSource config, IRegistryCore registry)
        {
        }

        public void FinishedStartup ()
        {
            if (m_registry == null)
                return; //Not enabled
            m_CapsService = m_registry.RequestModuleInterface<ICapsService> ();
            m_GridService = m_registry.RequestModuleInterface<IGridService> ();
            m_PresenceService = m_registry.RequestModuleInterface<IAgentInfoService> ();
            m_UserAccountService = m_registry.RequestModuleInterface<IUserAccountService> ();
            m_UserAgentService = m_registry.RequestModuleInterface<IUserAgentService>();
            m_SimulationService = m_registry.RequestModuleInterface<ISimulationService>();
            m_userFinder = m_registry.RequestModuleInterface<IUserFinder>();
            m_DefaultGatewayRegion = FindDefaultRegion ();
        }

        private GridRegion FindDefaultRegion ()
        {
            GridRegion region = null;
            if (m_defaultRegion != "")//This overrides all
            {
                region = m_GridService.GetRegionByName(null, m_defaultRegion);
                if (region != null)
                {
                    m_foundDefaultRegion = true;
                    return region;
                }
            }
            List<GridRegion> defs = m_GridService.GetDefaultRegions(null);
            if (defs != null && defs.Count > 0)
                region = FindRegion(defs);
            if (region == null)
            {
                defs = m_GridService.GetFallbackRegions(null, 0, 0);
                if (defs != null && defs.Count > 0)
                    region = FindRegion (defs);
                if (region == null)
                {
                    defs = m_GridService.GetSafeRegions(null, 0, 0);
                    if (defs != null && defs.Count > 0)
                        region = FindRegion (defs);
                    if (region == null)
                        MainConsole.Instance.WarnFormat ("[GATEKEEPER SERVICE]: Please specify a default region for this grid!");
                }
            }
            if(region != null)
                m_foundDefaultRegion = true;
            return region;
        }

        private GridRegion FindRegion (List<GridRegion> defs)
        {
            for (int i = 0; i < defs.Count; i++)
            {
                if ((defs[i].Flags & (int)Aurora.Framework.RegionFlags.Safe) == (int)Aurora.Framework.RegionFlags.Safe)
                    return defs[i];
            }
            return null;
        }

        public bool LinkRegion (string regionName, out UUID regionID, out ulong regionHandle, out string externalName, out string imageURL, out string reason)
        {
            regionID = UUID.Zero;
            regionHandle = 0;
            externalName = m_ExternalName + ((regionName != string.Empty) ? " " + regionName : "");
            imageURL = string.Empty;
            reason = string.Empty;
            GridRegion region = null;

            MainConsole.Instance.DebugFormat ("[GATEKEEPER SERVICE]: Request to link to {0}", (regionName == string.Empty) ? "default region" : regionName);
            if (!m_AllowTeleportsToAnyRegion || regionName == string.Empty)
            {
                if(!m_foundDefaultRegion)
                    m_DefaultGatewayRegion = FindDefaultRegion();
                if (m_DefaultGatewayRegion != null)
                    region = m_DefaultGatewayRegion;
                else
                {
                    reason = "Grid setup problem. Try specifying a particular region here.";
                    return false;
                }
            }
            else
            {
                region = m_GridService.GetRegionByName(null, regionName);
                if (region == null)
                {
                    if(!m_foundDefaultRegion)
                        m_DefaultGatewayRegion = FindDefaultRegion();
                    if (m_DefaultGatewayRegion != null)
                        region = m_DefaultGatewayRegion;
                    if (region == null)
                    {
                        reason = "Region not found";
                        return false;
                    }
                }
            }

            regionID = region.RegionID;
            regionHandle = region.RegionHandle;

            string regionimage = "regionImage" + regionID.ToString ();
            regionimage = regionimage.Replace ("-", "");
            imageURL = region.ServerURI + "/" + "index.php?method=" + regionimage;

            return true;
        }

        public GridRegion GetHyperlinkRegion (UUID regionID)
        {
            MainConsole.Instance.DebugFormat ("[GATEKEEPER SERVICE]: Request to get hyperlink region {0}", regionID);

            if(!m_AllowTeleportsToAnyRegion)
            {
                if (!m_foundDefaultRegion || m_DefaultGatewayRegion == null)
                    m_DefaultGatewayRegion = FindDefaultRegion();
                // Don't even check the given regionID
                return m_DefaultGatewayRegion;
            }

            GridRegion region = m_GridService.GetRegionByUUID(null, regionID);
            if(region != null && (region.Flags & (int)Aurora.Framework.RegionFlags.Safe) == (int)Aurora.Framework.RegionFlags.Safe)
                return region;
            if (!m_foundDefaultRegion || m_DefaultGatewayRegion == null)
                m_DefaultGatewayRegion = FindDefaultRegion();
            if (m_DefaultGatewayRegion != null && (m_DefaultGatewayRegion.Flags & (int)Aurora.Framework.RegionFlags.Safe) == (int)Aurora.Framework.RegionFlags.Safe)
                return m_DefaultGatewayRegion;
            return (m_DefaultGatewayRegion = FindDefaultRegion ());
        }

        #region Login Agent

        public bool LoginAgent (AgentCircuitData aCircuit, GridRegion destination, out string reason)
        {
            reason = string.Empty;

            string authURL = string.Empty;
            if (aCircuit.ServiceURLs.ContainsKey ("HomeURI"))
                authURL = aCircuit.ServiceURLs["HomeURI"].ToString ();
            MainConsole.Instance.InfoFormat ("[GATEKEEPER SERVICE]: Login request for {0} {1} @ {2}",
                authURL, aCircuit.AgentID, destination.RegionName);

            //
            // Authenticate the user
            //
            if (!Authenticate (aCircuit))
            {
                reason = "Unable to verify identity";
                MainConsole.Instance.InfoFormat ("[GATEKEEPER SERVICE]: Unable to verify identity of agent {0}. Refusing service.", aCircuit.AgentID);
                return false;
            }
            MainConsole.Instance.DebugFormat ("[GATEKEEPER SERVICE]: Identity verified for {0} @ {1}", aCircuit.AgentID, authURL);

            //
            // Check for impersonations
            //
            UserAccount account = null;
            if (m_UserAccountService != null)
            {
                // Check to see if we have a local user with that UUID
                account = m_UserAccountService.GetUserAccount (null, aCircuit.AgentID);
                if (account != null && m_userFinder.IsLocalGridUser(account.PrincipalID))
                {
                    // Make sure this is the user coming home, and not a foreign user with same UUID as a local user
                    if (m_UserAgentService != null)
                    {
                        if (!m_UserAgentService.AgentIsComingHome (aCircuit.SessionID, m_ExternalName))
                        {
                            // Can't do, sorry
                            reason = "Unauthorized";
                            MainConsole.Instance.InfoFormat ("[GATEKEEPER SERVICE]: Foreign agent {0} has same ID as local user. Refusing service.",
                                aCircuit.AgentID);
                            return false;

                        }
                    }
                }
            }
            MainConsole.Instance.InfoFormat ("[GATEKEEPER SERVICE]: User is ok");

            // May want to authorize

            //bool isFirstLogin = false;
            //
            // Login the presence, if it's not there yet (by the login service)
            //
            UserInfo presence = m_PresenceService.GetUserInfo (aCircuit.AgentID.ToString());
            if (m_userFinder.IsLocalGridUser(aCircuit.AgentID) && presence != null && presence.IsOnline) // it has been placed there by the login service
            {
                //    isFirstLogin = true;
            }
            else
            {
                IUserAgentService userAgentService = new UserAgentServiceConnector(aCircuit.ServiceURLs["HomeURI"].ToString());
                Vector3 position = Vector3.UnitY, lookAt = Vector3.UnitY;
                GridRegion finalDestination = userAgentService.GetHomeRegion(aCircuit.AgentID, out position, out lookAt);
                if (finalDestination == null)
                {
                    reason = "You do not have a home position set.";
                    return false;
                }
                m_PresenceService.SetHomePosition(aCircuit.AgentID.ToString(), finalDestination.RegionID, position, lookAt);
                m_PresenceService.SetLoggedIn(aCircuit.AgentID.ToString(), true, true, destination.RegionID);
            }

            MainConsole.Instance.DebugFormat ("[GATEKEEPER SERVICE]: Login presence ok");

            //
            // Get the region
            //
            destination = m_GridService.GetRegionByUUID(null, destination.RegionID);
            if (destination == null)
            {
                reason = "Destination region not found";
                return false;
            }

            //
            // Adjust the visible name
            //
            if (account != null)
            {
                aCircuit.firstname = account.FirstName;
                aCircuit.lastname = account.LastName;
            }
            if (account == null && !aCircuit.lastname.StartsWith ("@"))
            {
                aCircuit.firstname = aCircuit.firstname + "." + aCircuit.lastname;
                try
                {
                    Uri uri = new Uri (aCircuit.ServiceURLs["HomeURI"].ToString ());
                    aCircuit.lastname = "@" + uri.Host; // + ":" + uri.Port;
                }
                catch
                {
                    MainConsole.Instance.WarnFormat ("[GATEKEEPER SERVICE]: Malformed HomeURI (this should never happen): {0}", aCircuit.ServiceURLs["HomeURI"]);
                    aCircuit.lastname = "@" + aCircuit.ServiceURLs["HomeURI"].ToString ();
                }
                m_userFinder.AddUser(aCircuit.AgentID, aCircuit.firstname, aCircuit.lastname, aCircuit.ServiceURLs);
                m_UserAccountService.CacheAccount(new UserAccount(UUID.Zero, aCircuit.AgentID, aCircuit.firstname + aCircuit.lastname, "") { UserFlags = 1024 });
            }

            retry:
            //
            // Finally launch the agent at the destination
            //
            TeleportFlags loginFlag = /*isFirstLogin ? */TeleportFlags.ViaLogin/* : TeleportFlags.ViaHGLogin*/;
            IRegionClientCapsService regionClientCaps = null;
            if (m_CapsService != null)
            {
                //Remove any previous users
                string ServerCapsBase = Aurora.Framework.Capabilities.CapsUtil.GetRandomCapsObjectPath ();
                m_CapsService.CreateCAPS(aCircuit.AgentID,
                    Aurora.Framework.Capabilities.CapsUtil.GetCapsSeedPath(ServerCapsBase),
                    destination.RegionHandle, true, aCircuit, 0);

                regionClientCaps = m_CapsService.GetClientCapsService (aCircuit.AgentID).GetCapsService (destination.RegionHandle);
                if (aCircuit.ServiceURLs == null)
                    aCircuit.ServiceURLs = new Dictionary<string, object>();
                aCircuit.ServiceURLs["IncomingCAPSHandler"] = regionClientCaps.CapsUrl;
            }
            aCircuit.child = false;//FIX THIS, OPENSIM ALWAYS SENDS CHILD!
            int requestedUDPPort = 0;
            bool success = m_SimulationService.CreateAgent (destination, aCircuit, (uint)loginFlag, null, out requestedUDPPort, out reason);
            if (success)
            {
                if (regionClientCaps != null)
                {
                    if (requestedUDPPort == 0)
                        requestedUDPPort = destination.ExternalEndPoint.Port;
                    IPAddress ipAddress = destination.ExternalEndPoint.Address;
                    aCircuit.RegionUDPPort = requestedUDPPort;
                    regionClientCaps.LoopbackRegionIP = ipAddress;
                    regionClientCaps.CircuitData.RegionUDPPort = requestedUDPPort;
                    OSDMap responseMap = (OSDMap)OSDParser.DeserializeJson (reason);
                    OSDMap SimSeedCaps = (OSDMap)responseMap["CapsUrls"];
                    regionClientCaps.AddCAPS (SimSeedCaps);
                }
            }
            else
            {
                if (m_CapsService != null)
                    m_CapsService.RemoveCAPS (aCircuit.AgentID);
                m_GridService.SetRegionUnsafe(destination.RegionID);
                if(!m_foundDefaultRegion)
                    m_DefaultGatewayRegion = FindDefaultRegion();
                if (destination != m_DefaultGatewayRegion)
                {
                    destination = m_DefaultGatewayRegion;
                    goto retry;
                }
                else
                {
                    m_DefaultGatewayRegion = FindDefaultRegion ();
                    if (m_DefaultGatewayRegion == destination)
                        return false;//It failed to find a new one
                    destination = m_DefaultGatewayRegion;
                    goto retry;//It found a new default region
                }
            }
            return success;
        }

        protected bool Authenticate (AgentCircuitData aCircuit)
        {
            if (!CheckAddress (aCircuit.ServiceSessionID))
                return false;

            string userURL = string.Empty;
            if (aCircuit.ServiceURLs.ContainsKey ("HomeURI"))
                userURL = aCircuit.ServiceURLs["HomeURI"].ToString ();

            if (userURL == string.Empty)
            {
                MainConsole.Instance.DebugFormat ("[GATEKEEPER SERVICE]: Agent did not provide an authentication server URL");
                return false;
            }

            if (userURL == m_ExternalName)
                return m_UserAgentService.VerifyAgent (aCircuit.SessionID, aCircuit.ServiceSessionID);
            else
            {
                //                Object[] args = new Object[] { userURL };
                IUserAgentService userAgentService = new UserAgentServiceConnector (userURL);
                if (userAgentService != null)
                {
                    try
                    {
                        return userAgentService.VerifyAgent (aCircuit.SessionID, aCircuit.ServiceSessionID);
                    }
                    catch
                    {
                        MainConsole.Instance.DebugFormat ("[GATEKEEPER SERVICE]: Unable to contact authentication service at {0}", userURL);
                        return false;
                    }
                }
            }

            return false;
        }

        // Check that the service token was generated for *this* grid.
        // If it wasn't then that's a fake agent.
        protected bool CheckAddress (string serviceToken)
        {
            string[] parts = serviceToken.Split (new char[] { ';' });
            if (parts.Length < 2)
                return false;

            char[] trailing_slash = new char[] { '/' };
            string addressee = parts[0].TrimEnd (trailing_slash);
            string externalname = m_ExternalName.TrimEnd (trailing_slash);
            MainConsole.Instance.DebugFormat ("[GATEKEEPER SERVICE]: Verifying {0} against {1}", addressee, externalname);
            Uri m_Uri = new Uri (addressee);
            IPAddress ip = NetworkUtils.GetHostFromDNS(m_Uri.Host);
            addressee = addressee.Replace (m_Uri.Host, ip.ToString ());
            return string.Equals (addressee, externalname, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
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

using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

using OpenMetaverse;

using Nini.Config;
using log4net;
using Aurora.Framework;
using Aurora.Simulation.Base;

namespace Aurora.Addon.Hypergrid
{
    public class GatekeeperService : IService, IGatekeeperService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger (
                MethodBase.GetCurrentMethod ().DeclaringType);

        private static bool m_Initialized = false;

        private static IGridService m_GridService;
        private static IAgentInfoService m_PresenceService;
        private static IUserAccountService m_UserAccountService;
        private static IUserAgentService m_UserAgentService;
        private static ISimulationService m_SimulationService;

        protected string m_AllowedClients = string.Empty;
        protected string m_DeniedClients = string.Empty;

        private static UUID m_ScopeID;
        private static bool m_AllowTeleportsToAnyRegion;
        private static string m_ExternalName;
        private static GridRegion m_DefaultGatewayRegion;

        public void Initialize (IConfigSource config, IRegistryCore registry)
        {
            IConfig serverConfig = config.Configs["GatekeeperService"];

            if(serverConfig != null)
                m_AllowTeleportsToAnyRegion = serverConfig.GetBoolean ("AllowTeleportsToAnyRegion", true);
            uint port = serverConfig.GetUInt ("GatekeeperServicePort", 8003);

            IHttpServer server = registry.RequestModuleInterface<ISimulationBase> ().GetHttpServer (port);
            m_ExternalName = server.HostName + ":" + port + "/";
            registry.RegisterModuleInterface<IGatekeeperService> (this);
        }

        public void Start (IConfigSource config, IRegistryCore registry)
        {
            m_GridService = registry.RequestModuleInterface<IGridService> ();
            m_PresenceService = registry.RequestModuleInterface<IAgentInfoService>();
            m_UserAccountService = registry.RequestModuleInterface<IUserAccountService>();
            m_UserAgentService = registry.RequestModuleInterface<IUserAgentService>();
            m_SimulationService = registry.RequestModuleInterface<ISimulationService> ();
        }

        public void FinishedStartup ()
        {
        }

        public bool LinkRegion (string regionName, out UUID regionID, out ulong regionHandle, out string externalName, out string imageURL, out string reason)
        {
            regionID = UUID.Zero;
            regionHandle = 0;
            externalName = m_ExternalName + ((regionName != string.Empty) ? " " + regionName : "");
            imageURL = string.Empty;
            reason = string.Empty;
            GridRegion region = null;

            m_log.DebugFormat ("[GATEKEEPER SERVICE]: Request to link to {0}", (regionName == string.Empty) ? "default region" : regionName);
            if (!m_AllowTeleportsToAnyRegion || regionName == string.Empty)
            {
                List<GridRegion> defs = m_GridService.GetDefaultRegions (m_ScopeID);
                if (defs != null && defs.Count > 0)
                {
                    region = defs[0];
                    m_DefaultGatewayRegion = region;
                }
                else
                {
                    defs = m_GridService.GetFallbackRegions (m_ScopeID, 0, 0);
                    if (defs != null && defs.Count > 0)
                    {
                        region = defs[0];
                        m_DefaultGatewayRegion = region;
                    }
                    else
                    {
                        defs = m_GridService.GetSafeRegions (m_ScopeID, 0, 0);
                        if (defs != null && defs.Count > 0)
                        {
                            region = defs[0];
                            m_DefaultGatewayRegion = region;
                        }
                        else
                        {
                            reason = "Grid setup problem. Try specifying a particular region here.";
                            m_log.DebugFormat ("[GATEKEEPER SERVICE]: Unable to send information. Please specify a default region for this grid!");
                            return false;
                        }
                    }
                }
            }
            else
            {
                region = m_GridService.GetRegionByName (m_ScopeID, regionName);
                if (region == null)
                {
                    reason = "Region not found";
                    return false;
                }
            }

            regionID = region.RegionID;
            regionHandle = region.RegionHandle;

            string regionimage = "regionImage" + regionID.ToString ();
            regionimage = regionimage.Replace ("-", "");
            imageURL = region.ServerURI + "index.php?method=" + regionimage;

            return true;
        }

        public GridRegion GetHyperlinkRegion (UUID regionID)
        {
            m_log.DebugFormat ("[GATEKEEPER SERVICE]: Request to get hyperlink region {0}", regionID);

            if (!m_AllowTeleportsToAnyRegion)
                // Don't even check the given regionID
                return m_DefaultGatewayRegion;

            GridRegion region = m_GridService.GetRegionByUUID (m_ScopeID, regionID);
            return region;
        }

        #region Login Agent

        public bool LoginAgent (AgentCircuitData aCircuit, GridRegion destination, out string reason)
        {
            reason = string.Empty;

            string authURL = string.Empty;
            if (aCircuit.ServiceURLs.ContainsKey ("HomeURI"))
                authURL = aCircuit.ServiceURLs["HomeURI"].ToString ();
            m_log.InfoFormat ("[GATEKEEPER SERVICE]: Login request for {0} {1} @ {2}",
                authURL, aCircuit.AgentID, destination.RegionName);

            //
            // Check client
            //
            /*if (m_AllowedClients != string.Empty)
            {
                Regex arx = new Regex (m_AllowedClients);
                Match am = arx.Match (aCircuit.Viewer);

                if (!am.Success)
                {
                    m_log.InfoFormat ("[GATEKEEPER SERVICE]: Login failed, reason: client {0} is not allowed", aCircuit.Viewer);
                    return false;
                }
            }

            if (m_DeniedClients != string.Empty)
            {
                Regex drx = new Regex (m_DeniedClients);
                Match dm = drx.Match (aCircuit.Viewer);

                if (dm.Success)
                {
                    m_log.InfoFormat ("[GATEKEEPER SERVICE]: Login failed, reason: client {0} is denied", aCircuit.Viewer);
                    return false;
                }
            }*/

            //
            // Authenticate the user
            //
            if (!Authenticate (aCircuit))
            {
                reason = "Unable to verify identity";
                m_log.InfoFormat ("[GATEKEEPER SERVICE]: Unable to verify identity of agent {0}. Refusing service.", aCircuit.AgentID);
                return false;
            }
            m_log.DebugFormat ("[GATEKEEPER SERVICE]: Identity verified for {0} @ {1}", aCircuit.AgentID, authURL);

            //
            // Check for impersonations
            //
            UserAccount account = null;
            if (m_UserAccountService != null)
            {
                // Check to see if we have a local user with that UUID
                account = m_UserAccountService.GetUserAccount (m_ScopeID, aCircuit.AgentID);
                if (account != null)
                {
                    // Make sure this is the user coming home, and not a foreign user with same UUID as a local user
                    if (m_UserAgentService != null)
                    {
                        if (!m_UserAgentService.AgentIsComingHome (aCircuit.SessionID, m_ExternalName))
                        {
                            // Can't do, sorry
                            reason = "Unauthorized";
                            m_log.InfoFormat ("[GATEKEEPER SERVICE]: Foreign agent {0} has same ID as local user. Refusing service.",
                                aCircuit.AgentID);
                            return false;

                        }
                    }
                }
            }
            m_log.DebugFormat ("[GATEKEEPER SERVICE]: User is ok");

            // May want to authorize

            bool isFirstLogin = false;
            //
            // Login the presence, if it's not there yet (by the login service)
            //
            UserInfo presence = m_PresenceService.GetUserInfo (aCircuit.AgentID.ToString());
            if (presence.IsOnline) // it has been placed there by the login service
                isFirstLogin = true;
            else
                m_PresenceService.SetLoggedIn (aCircuit.AgentID.ToString (), true, true, UUID.Zero);

            m_log.DebugFormat ("[GATEKEEPER SERVICE]: Login presence ok");

            //
            // Get the region
            //
            destination = m_GridService.GetRegionByUUID (m_ScopeID, destination.RegionID);
            if (destination == null)
            {
                reason = "Destination region not found";
                return false;
            }
            m_log.DebugFormat ("[GATEKEEPER SERVICE]: destination ok: {0}", destination.RegionName);

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
                    m_log.WarnFormat ("[GATEKEEPER SERVICE]: Malformed HomeURI (this should never happen): {0}", aCircuit.ServiceURLs["HomeURI"]);
                    aCircuit.lastname = "@" + aCircuit.ServiceURLs["HomeURI"].ToString ();
                }
            }

            //
            // Finally launch the agent at the destination
            //
            TeleportFlags loginFlag = /*isFirstLogin ? */TeleportFlags.ViaLogin/* : TeleportFlags.ViaHGLogin*/;
            m_log.DebugFormat ("[GATEKEEPER SERVICE]: launching agent {0}", loginFlag);
            return m_SimulationService.CreateAgent (destination, ref aCircuit, (uint)loginFlag, null, out reason);
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
                m_log.DebugFormat ("[GATEKEEPER SERVICE]: Agent did not provide an authentication server URL");
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
                        m_log.DebugFormat ("[GATEKEEPER SERVICE]: Unable to contact authentication service at {0}", userURL);
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
            m_log.DebugFormat ("[GATEKEEPER SERVICE]: Verifying {0} against {1}", addressee, externalname);

            return string.Equals (addressee, externalname, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
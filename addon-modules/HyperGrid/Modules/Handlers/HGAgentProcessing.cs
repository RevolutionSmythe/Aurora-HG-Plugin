using System;
using System.Collections.Generic;
using System.Text;
using Aurora.Framework;
using Aurora.Framework.Capabilities;
using Aurora.Simulation.Base;
using OpenSim.Services.MessagingService;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using System.IO;
using System.Net;
using System.Threading;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using OpenSim.Services.Interfaces;

namespace Aurora.Addon.HyperGrid
{
    public class HGAgentProcessing : AgentProcessing
    {
        protected GatekeeperServiceConnector m_GatekeeperConnector;
        public override void Initialize (IConfigSource config, IRegistryCore registry)
        {
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            m_registry = registry;
            _config = config;
            IConfig agentConfig = config.Configs["AgentProcessing"];
            if (agentConfig != null)
            {
                m_enabled = agentConfig.GetString ("Module", "AgentProcessing") == "HGAgentProcessing";
                VariableRegionSight = agentConfig.GetBoolean ("UseVariableRegionSightDistance", VariableRegionSight);
                MaxVariableRegionSight = agentConfig.GetInt ("MaxDistanceVariableRegionSightDistance", MaxVariableRegionSight);
            }
            if (m_enabled)
                m_registry.RegisterModuleInterface<IAgentProcessing> (this);
        }

        public override void FinishedStartup ()
        {
            if (m_registry != null)//Enabled
            {
                base.FinishedStartup ();
                m_GatekeeperConnector = new GatekeeperServiceConnector (m_registry.RequestModuleInterface<IAssetService> ());
            }
        }

        public override bool InformClientOfNeighbor (UUID AgentID, ulong requestingRegion, AgentCircuitData circuitData, ref GridRegion neighbor, uint TeleportFlags, AgentData agentData, out string reason, out bool useCallbacks)
        {
            useCallbacks = true;
            if (neighbor == null)
            {
                reason = "Could not find neighbor to inform";
                return false;
            }
            
            MainConsole.Instance.Info ("[AgentProcessing]: Starting to inform client about neighbor " + neighbor.RegionName);

            //Notes on this method
            // 1) the SimulationService.CreateAgent MUST have a fixed CapsUrl for the region, so we have to create (if needed)
            //       a new Caps handler for it.
            // 2) Then we can call the methods (EnableSimulator and EstatablishAgentComm) to tell the client the new Urls
            // 3) This allows us to make the Caps on the grid server without telling any other regions about what the
            //       Urls are.

            ISimulationService SimulationService = m_registry.RequestModuleInterface<ISimulationService> ();
            if (SimulationService != null)
            {
                ICapsService capsService = m_registry.RequestModuleInterface<ICapsService> ();
                IClientCapsService clientCaps = capsService.GetClientCapsService (AgentID);
                GridRegion originalDest = neighbor;
                if ((neighbor.Flags & (int)Aurora.Framework.RegionFlags.Hyperlink) == (int)Aurora.Framework.RegionFlags.Hyperlink)
                {
                    neighbor = GetFinalDestination (neighbor);
                    if (neighbor == null || neighbor.RegionHandle == 0)
                    {
                        reason = "Could not find neighbor to inform";
                        return false;
                    }
                    //Remove any offenders
                    clientCaps.RemoveCAPS (originalDest.RegionHandle);
                    clientCaps.RemoveCAPS (neighbor.RegionHandle);
                }

                IRegionClientCapsService oldRegionService = clientCaps.GetCapsService (neighbor.RegionHandle);

                //If its disabled, it should be removed, so kill it!
                if (oldRegionService != null && oldRegionService.Disabled)
                {
                    clientCaps.RemoveCAPS (neighbor.RegionHandle);
                    oldRegionService = null;
                }

                bool newAgent = oldRegionService == null;
                IRegionClientCapsService otherRegionService = clientCaps.GetOrCreateCapsService (neighbor.RegionHandle,
                    CapsUtil.GetCapsSeedPath (CapsUtil.GetRandomCapsObjectPath ()), circuitData, 0);

                if (!newAgent)
                {
                    //Note: if the agent is already there, send an agent update then
                    bool result = true;
                    if (agentData != null)
                    {
                        agentData.IsCrossing = false;
                        result = SimulationService.UpdateAgent (neighbor, agentData);
                    }
                    if (result)
                        oldRegionService.Disabled = false;
                    reason = "";
                    return result;
                }

                ICommunicationService commsService = m_registry.RequestModuleInterface<ICommunicationService> ();
                if (commsService != null)
                    commsService.GetUrlsForUser (neighbor, circuitData.AgentID);//Make sure that we make userURLs if we need to

                circuitData.CapsPath = CapsUtil.GetCapsPathFromCapsSeed (otherRegionService.CapsUrl);
                if (clientCaps.AccountInfo != null)
                {
                    circuitData.firstname = clientCaps.AccountInfo.FirstName;
                    circuitData.lastname = clientCaps.AccountInfo.LastName;
                }
                bool regionAccepted = false;
                int requestedUDPPort = 0;
                if ((originalDest.Flags & (int)Aurora.Framework.RegionFlags.Hyperlink) == (int)Aurora.Framework.RegionFlags.Hyperlink)
                {
                    if (circuitData.ServiceURLs == null || circuitData.ServiceURLs.Count == 0)
                    {
                        if (clientCaps.AccountInfo != null)
                        {
                            circuitData.ServiceURLs = new Dictionary<string, object> ();
                            circuitData.ServiceURLs[GetHandlers.Helpers_HomeURI] = GetHandlers.GATEKEEPER_URL;
                            circuitData.ServiceURLs[GetHandlers.Helpers_GatekeeperURI] = GetHandlers.GATEKEEPER_URL;
                            circuitData.ServiceURLs[GetHandlers.Helpers_InventoryServerURI] = GetHandlers.GATEKEEPER_URL;
                            circuitData.ServiceURLs[GetHandlers.Helpers_AssetServerURI] = GetHandlers.GATEKEEPER_URL;
                            circuitData.ServiceURLs[GetHandlers.Helpers_ProfileServerURI] = GetHandlers.GATEKEEPER_URL;
                            circuitData.ServiceURLs[GetHandlers.Helpers_FriendsServerURI] = GetHandlers.GATEKEEPER_URL;
                            circuitData.ServiceURLs[GetHandlers.Helpers_IMServerURI] = GetHandlers.IM_URL;
                            clientCaps.AccountInfo.ServiceURLs = circuitData.ServiceURLs;
                            //Store the new urls
                            m_registry.RequestModuleInterface<IUserAccountService> ().StoreUserAccount (clientCaps.AccountInfo);
                        }
                    }
                    string userAgentDriver = circuitData.ServiceURLs[GetHandlers.Helpers_HomeURI].ToString ();
                    IUserAgentService connector = new UserAgentServiceConnector (userAgentDriver);
                    regionAccepted = connector.LoginAgentToGrid (circuitData, originalDest, neighbor, new IPEndPoint(IPAddress.Parse(circuitData.IPAddress), circuitData.RegionUDPPort), out reason);
                }
                else
                {
                    if(circuitData.child)
                        circuitData.reallyischild = true;
                    regionAccepted = SimulationService.CreateAgent (neighbor, circuitData,
                            TeleportFlags, agentData, out requestedUDPPort, out reason);
                }
                if (regionAccepted)
                {
                    IPAddress ipAddress = neighbor.ExternalEndPoint.Address;
                    string otherRegionsCapsURL;
                    //If the region accepted us, we should get a CAPS url back as the reason, if not, its not updated or not an Aurora region, so don't touch it.
                    if (reason != "" && reason != "authorized")
                    {
                        OSDMap responseMap = (OSDMap)OSDParser.DeserializeJson(reason);
                        OSDMap SimSeedCaps = (OSDMap)responseMap["CapsUrls"];
                        if (responseMap.ContainsKey("OurIPForClient"))
                        {
                            string ip = responseMap["OurIPForClient"].AsString();
                            ipAddress = IPAddress.Parse(ip);
                        }
                        otherRegionService.AddCAPS(SimSeedCaps);
                        otherRegionsCapsURL = otherRegionService.CapsUrl;
                    }
                    else
                    {
                        //We are assuming an OpenSim region now!
                        #region OpenSim teleport compatibility!

                        useCallbacks = false;
                        otherRegionsCapsURL = neighbor.ServerURI +
                            CapsUtil.GetCapsSeedPath(circuitData.CapsPath);
                        otherRegionService.CapsUrl = otherRegionsCapsURL;

                        #endregion
                    }

                    if (requestedUDPPort == 0)
                        requestedUDPPort = neighbor.ExternalEndPoint.Port;
                    if(ipAddress == null)
                        ipAddress = neighbor.ExternalEndPoint.Address;
                    circuitData.RegionUDPPort = requestedUDPPort;
                    otherRegionService = clientCaps.GetCapsService(neighbor.RegionHandle);
                    otherRegionService.LoopbackRegionIP = ipAddress;
                    otherRegionService.CircuitData.RegionUDPPort = requestedUDPPort;

                    IEventQueueService EQService = m_registry.RequestModuleInterface<IEventQueueService> ();

                    EQService.EnableSimulator (neighbor.RegionHandle,
                        ipAddress.GetAddressBytes(),
                        requestedUDPPort, AgentID,
                        neighbor.RegionSizeX, neighbor.RegionSizeY, requestingRegion);

                    // EnableSimulator makes the client send a UseCircuitCode message to the destination, 
                    // which triggers a bunch of things there.
                    // So let's wait
                    Thread.Sleep (300);
                    EQService.EstablishAgentCommunication (AgentID, neighbor.RegionHandle,
                        ipAddress.GetAddressBytes(),
                        requestedUDPPort, otherRegionsCapsURL, neighbor.RegionSizeX,
                        neighbor.RegionSizeY,
                        requestingRegion);

                    if (!useCallbacks)
                        Thread.Sleep (3000); //Give it a bit of time, only for OpenSim...

                    MainConsole.Instance.Info ("[AgentProcessing]: Completed inform client about neighbor " + neighbor.RegionName);
                }
                else
                {
                    clientCaps.RemoveCAPS (neighbor.RegionHandle);
                    MainConsole.Instance.Error ("[AgentProcessing]: Failed to inform client about neighbor " + neighbor.RegionName + ", reason: " + reason);
                    return false;
                }
                return true;
            }
            reason = "SimulationService does not exist";
            MainConsole.Instance.Error ("[AgentProcessing]: Failed to inform client about neighbor " + neighbor.RegionName + ", reason: " + reason + "!");
            return false;
        }

        //public override bool LoginAgent (GridRegion region, AgentCircuitData aCircuit, out string reason)
        //{//This requires a bunch more work...
        //}

        protected GridRegion GetFinalDestination (GridRegion region)
        {
            IGridService GridService = m_registry.RequestModuleInterface<IGridService> ();
            int flags = GridService.GetRegionFlags (null, region.RegionID);
            MainConsole.Instance.DebugFormat ("[HG ENTITY TRANSFER MODULE]: region {0} flags: {1}", region.RegionID, flags);
            if ((flags & (int)Aurora.Framework.RegionFlags.Hyperlink) != 0)
            {
                MainConsole.Instance.DebugFormat ("[HG ENTITY TRANSFER MODULE]: Destination region {0} is hyperlink", region.RegionID);
                GridRegion real_destination = m_GatekeeperConnector.GetHyperlinkRegion (region, region.RegionID);
                if(real_destination != null)
                    MainConsole.Instance.DebugFormat ("[HG ENTITY TRANSFER MODULE]: GetFinalDestination serveruri -> {0}", real_destination.ServerURI);
                return real_destination;
            }
            return region;
        }
    }
}

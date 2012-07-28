using System;
using System.Collections.Generic;
using System.Text;
using Aurora.Framework;
using OpenMetaverse;
using OpenSim.Services.Interfaces;

namespace Aurora.Addon.HyperGrid
{
    public class GetHandlers
    {
        public static string Helpers_HomeURI = "HomeURI";
        public static string Helpers_GatekeeperURI = "GatekeeperURI";
        public static string Helpers_InventoryServerURI = "InventoryServerURI";
        public static string Helpers_AssetServerURI = "AssetServerURI";
        public static string Helpers_ProfileServerURI = "ProfileServerURI";
        public static string Helpers_FriendsServerURI = "FriendsServerURI";
        public static string Helpers_IMServerURI = "IMServerURI";


        public static string PROFILE_URL = MainServer.Instance.FullHostName + ":" + MainServer.Instance.Port + "/profiles";
        public static string GATEKEEPER_URL = MainServer.Instance.FullHostName + ":" + MainServer.Instance.Port + "/";
        public static uint IM_PORT = MainServer.Instance.Port;
        public static string IM_URL = MainServer.Instance.FullHostName + ":" + IM_PORT + "/";

        public static bool GetIsForeign (string AgentID, string server, IRegistryCore registry, out string serverURL)
        {
            return GetIsForeign (UUID.Parse (AgentID), server, registry, out serverURL);
        }

        public static bool GetIsForeign (UUID AgentID, string server, IRegistryCore registry, out string serverURL)
        {
            serverURL = "";
            ICapsService caps = registry.RequestModuleInterface<ICapsService> ();
            IClientCapsService clientCaps = caps.GetClientCapsService (AgentID);
            if (clientCaps == null)
                return false;
            IRegionClientCapsService regionClientCaps = clientCaps.GetRootCapsService ();
            if (regionClientCaps == null)
                return false;
            Dictionary<string, object> urls = regionClientCaps.CircuitData.ServiceURLs;
            if (urls != null && urls.Count > 0)
            {
                serverURL = urls[server].ToString ();
                return true;
            }
            return false;
        }
    }
}

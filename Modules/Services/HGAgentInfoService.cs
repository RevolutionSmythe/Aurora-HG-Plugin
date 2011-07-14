using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Services.Interfaces;
using OpenSim.Services;
using OpenMetaverse;

namespace Aurora.Addon.Hypergrid
{
    public class HGAgentInfoService : AgentInfoService
    {
        public override void Initialize (Nini.Config.IConfigSource config, IRegistryCore registry)
        {
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            m_registry = registry;
            IConfig handlerConfig = config.Configs["Handlers"];
            if (handlerConfig.GetString ("AgentInfoHandler", "") != Name)
                return;

            registry.RegisterModuleInterface<IAgentInfoService> (this);
        }

        public override string[] GetAgentsLocations (string requestor, string[] userIDs)
        {
            List<string> locations = new List<string> ();
            string imServer;
            if (GetHandlers.GetIsForeign (requestor, "IMServerURI", m_registry, out imServer))
            {
                foreach (string userID in userIDs)
                {
                    string[] l = base.GetAgentsLocations (requestor, new string[1] { userID });
                    if (l[0] == "NotOnline")
                    {
                        UserAccount acc = m_registry.RequestModuleInterface<IUserAccountService> ().GetUserAccount (UUID.Zero, UUID.Parse (userID));
                        if (acc == null)
                            l[0] = imServer;//Forward to the IM server of the foreign grid
                    }
                    locations.Add (l[0]);
                }
                return locations.ToArray();
            }
            return base.GetAgentsLocations (requestor, userIDs);
        }
    }
}

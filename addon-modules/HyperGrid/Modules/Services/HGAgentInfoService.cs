using System;
using System.Collections.Generic;
using System.Text;
using Nini.Config;
using Aurora.Framework;
using OpenSim.Services.Interfaces;
using OpenSim.Services;
using OpenMetaverse;

namespace Aurora.Addon.HyperGrid
{
    public class HGAgentInfoService : AgentInfoService
    {
        public override void Initialize (Nini.Config.IConfigSource config, IRegistryCore registry)
        {
            m_registry = registry;
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            IConfig handlerConfig = config.Configs["Handlers"];
            if (handlerConfig.GetString ("AgentInfoHandler", "") != Name)
                return;

            registry.RegisterModuleInterface<IAgentInfoService>(this);
            Init(registry, Name);
        }

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Low)]
        public override List<string> GetAgentsLocations(string requestor, List<string> userIDs)
        {
            List<string> locations = new List<string> ();
            foreach (string userID in userIDs)
            {
                List<string> l = base.GetAgentsLocations(requestor, new List<string>() { userID });
                if (l[0] == "NotOnline")
                {
                    UserAccount acc = m_registry.RequestModuleInterface<IUserAccountService> ().GetUserAccount (null, UUID.Parse (userID));
                    if (acc == null)
                    {
                        IUserFinder userFinder = m_registry.RequestModuleInterface<IUserFinder> ();
                        string url = "";
                        if (userFinder != null && (url = userFinder.GetUserServerURL (UUID.Parse (userID), GetHandlers.Helpers_IMServerURI)) != "")
                            l[0] = url;
                    }
                }
                locations.Add (l[0]);
            }
            return locations;
        }
    }
}

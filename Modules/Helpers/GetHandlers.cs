using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenSim.Framework;

namespace Aurora.Addon.Hypergrid
{
    public class GetHandlers
    {
        public static string PROFILE_URL = MainServer.Instance.HostName + ":" + MainServer.Instance.Port + "/profiles";
        public static string GATEKEEPER_URL = MainServer.Instance.HostName + ":" + MainServer.Instance.Port + "/";
    }
}

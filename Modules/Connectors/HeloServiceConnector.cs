using log4net;
using System;
using System.Net;
using System.Reflection;
using Nini.Config;

namespace Aurora.Addon.Hypergrid
{
    public class HeloServicesConnector
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private string m_ServerURI = String.Empty;

        public HeloServicesConnector()
        {
        }

        public HeloServicesConnector (string serverURI)
        {
            m_ServerURI = serverURI.TrimEnd('/');
        }

        public virtual string Helo()
        {
            HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(m_ServerURI + "/helo");

            try
            {
                WebResponse response = req.GetResponse();
                if (response.Headers.Get("X-Handlers-Provided") == null) // just in case this ever returns a null
                    return string.Empty;
                return response.Headers.Get("X-Handlers-Provided");
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[HELO SERVICE]: Unable to perform HELO request to {0}: {1}", m_ServerURI, e.Message);
            }

            // fail
            return string.Empty;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Aurora.Framework;
using Aurora.Simulation.Base;
using OpenMetaverse;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

namespace Aurora.Addon.HyperGrid
{
    public interface IGatekeeperService
    {
        bool LinkRegion (string regionDescriptor, out UUID regionID, out ulong regionHandle, out string externalName, out string imageURL, out string reason);
        GridRegion GetHyperlinkRegion (UUID regionID);

        bool LoginAgent (AgentCircuitData aCircuit, GridRegion destination, out string reason);

    }

    public interface IInstantMessage
    {
        bool IncomingInstantMessage (GridInstantMessage im);
        bool OutgoingInstantMessage (GridInstantMessage im, string url, bool foreigner);
    }
    public interface IFriendsSimConnector
    {
        bool StatusNotify (UUID userID, UUID friendID, bool online);
    }

    public interface IInstantMessageSimConnector
    {
        bool SendInstantMessage (GridInstantMessage im);
    }

    public static class ServerUtils
    {
        public static byte[] SerializeResult(XmlSerializer xs, object data)
        {
            MemoryStream ms = new MemoryStream();
            XmlTextWriter xw = new XmlTextWriter(ms, Util.UTF8);
            xw.Formatting = Formatting.Indented;
            xs.Serialize(xw, data);
            xw.Flush();

            ms.Seek(0, SeekOrigin.Begin);
            byte[] ret = ms.GetBuffer();
            Array.Resize(ref ret, (int)ms.Length);

            return ret;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using System.Text;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using Aurora.Framework;
using Aurora.Simulation.Base;
using OpenMetaverse;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

namespace Aurora.Addon.Hypergrid
{
    public interface IGatekeeperService
    {
        bool LinkRegion (string regionDescriptor, out UUID regionID, out ulong regionHandle, out string externalName, out string imageURL, out string reason);
        GridRegion GetHyperlinkRegion (UUID regionID);

        bool LoginAgent (AgentCircuitData aCircuit, GridRegion destination, out string reason);

    }

    /// <summary>
    /// HG1.5 only
    /// </summary>
    public interface IUserAgentService
    {
        // called by login service only
        bool LoginAgentToGrid (AgentCircuitData agent, GridRegion gatekeeper, GridRegion finalDestination, IPEndPoint clientIP, out string reason);
        // called by simulators
        bool LoginAgentToGrid (AgentCircuitData agent, GridRegion gatekeeper, GridRegion finalDestination, out string reason);
        void LogoutAgent (UUID userID, UUID sessionID);
        GridRegion GetHomeRegion (UUID userID, out Vector3 position, out Vector3 lookAt);
        Dictionary<string, object> GetServerURLs (UUID userID);

        string LocateUser (UUID userID);
        // Tries to get the universal user identifier for the targetUserId
        // on behalf of the userID
        string GetUUI (UUID userID, UUID targetUserID);

        // Returns the local friends online
        List<UUID> StatusNotification (List<string> friends, UUID userID, bool online);
        //List<UUID> GetOnlineFriends(UUID userID, List<string> friends);

        bool AgentIsComingHome (UUID sessionID, string thisGridExternalName);
        bool VerifyAgent (UUID sessionID, string token);
        bool VerifyClient (UUID sessionID, string reportedIP);
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
}

using OpenMetaverse;
using Aurora.Framework;
using System;
using System.Collections.Generic;
using OpenSim.Services.Interfaces;
using Nini.Config;
using FriendInfo = OpenSim.Services.Interfaces.FriendInfo;
using Aurora.Simulation.Base;
using OpenSim.Services.Friends;

namespace Aurora.Addon.HyperGrid.Handlers
{
    public class HGFriendsService : FriendsService
    {
        public override string Name
        {
            get { return GetType().Name; }
        }

        public override bool StoreFriend (UUID PrincipalID, string Friend, int flags)
        {
            IUserAccountService userAccountService = m_registry.RequestModuleInterface<IUserAccountService> ();
            UserAccount agentAccount = userAccountService.GetUserAccount (null, PrincipalID);
            UUID FriendUUID;
            if (!UUID.TryParse(Friend, out FriendUUID))
                return base.StoreFriend(PrincipalID, Friend, flags);//Already set to a UUI
            else
            {
                UserAccount friendAccount = userAccountService.GetUserAccount (null, FriendUUID);
                if (agentAccount == null || friendAccount == null)
                {
                    // remote grid users
                    ICapsService capsService = m_registry.RequestModuleInterface<ICapsService> ();
                    IClientCapsService FriendCaps = capsService.GetClientCapsService (UUID.Parse (Friend));
                    if (FriendCaps != null && FriendCaps.GetRootCapsService () != null)
                        Friend = HGUtil.ProduceUserUniversalIdentifier (FriendCaps.GetRootCapsService ().CircuitData);
                }
                return base.StoreFriend (PrincipalID, Friend, flags);
            }
        }
    }
}

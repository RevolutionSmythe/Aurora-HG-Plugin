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
using OpenMetaverse;
using Nini.Config;
using System.Reflection;
using OpenSim.Services.Interfaces;
using OpenSim.Services.InventoryService;
using Aurora.Framework;
using OpenSim.Services;
using OpenSim.Services.Connectors;

namespace Aurora.Addon.HyperGrid
{
    /// <summary>
    /// Hypergrid inventory service. It serves the IInventoryService interface,
    /// but implements it in ways that are appropriate for inter-grid
    /// inventory exchanges. Specifically, it does not performs deletions
    /// and it responds to GetRootFolder requests with the ID of the
    /// Suitcase folder, not the actual "My Inventory" folder.
    /// </summary>
    public class HGInternalInventoryService : InventoryService
    {
        public override void Initialize (IConfigSource config, IRegistryCore registry)
        {
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            IConfig handlerConfig = config.Configs["Handlers"];
            if (handlerConfig.GetString ("InventoryHandler", "") != Name)
                return;

            m_registry = registry;
            m_UserAccountService = registry.RequestModuleInterface<IUserAccountService> ();

            registry.RegisterModuleInterface<IInventoryService>(this);
            Init(registry, Name);
        }

        public override void FinishedStartup ()
        {
            if (m_registry == null)//Not initialized
                return;
            base.FinishedStartup ();
        }

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Low)]
        public override bool AddItem(InventoryItemBase item)
        {
            string invserverURL = "";
            if (GetHandlers.GetIsForeign (item.Owner, "InventoryServerURI", m_registry, out invserverURL))
            {
                XInventoryServicesConnector xinv = new XInventoryServicesConnector (invserverURL + "xinventory");
                bool success = xinv.AddItem (item);
                return success;
            }
            return base.AddItem (item);
        }

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Low)]
        public override bool AddFolder(InventoryFolderBase folder)
        {
            string invserverURL = "";
            if (GetHandlers.GetIsForeign (folder.Owner, "InventoryServerURI", m_registry, out invserverURL))
            {
                XInventoryServicesConnector xinv = new XInventoryServicesConnector (invserverURL + "xinventory");
                return xinv.AddFolder (folder);
            }
            return base.AddFolder (folder);
        }

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Low)]
        public override InventoryFolderBase GetFolderForType(UUID principalID, InventoryType invType, AssetType type)
        {
            string invserverURL = "";
            if (GetHandlers.GetIsForeign (principalID, "InventoryServerURI", m_registry, out invserverURL))
            {
                XInventoryServicesConnector xinv = new XInventoryServicesConnector (invserverURL + "xinventory");
                return xinv.GetFolderForType (principalID, invType, type);
            }
            return base.GetFolderForType (principalID, invType, type);
        }

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Medium)]
        public override InventoryFolderBase GetRootFolder(UUID principalID)
        {
            string invserverURL = "";
            if (GetHandlers.GetIsForeign (principalID, "InventoryServerURI", m_registry, out invserverURL))
            {
                XInventoryServicesConnector xinv = new XInventoryServicesConnector (invserverURL + "xinventory");
                return xinv.GetRootFolder (principalID);
            }
            return base.GetRootFolder (principalID);
        }

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Low)]
        public override InventoryFolderBase GetFolder(InventoryFolderBase folder)
        {
            string invserverURL = "";
            if (GetHandlers.GetIsForeign (folder.Owner, "InventoryServerURI", m_registry, out invserverURL))
            {
                XInventoryServicesConnector xinv = new XInventoryServicesConnector (invserverURL + "xinventory");
                return xinv.GetFolder (folder);
            }
            return base.GetFolder (folder);
        }

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Low)]
        public override InventoryItemBase GetItem(InventoryItemBase item)
        {
            string invServerURL = "", assetServerURL = "";
            if (GetHandlers.GetIsForeign (item.Owner, "InventoryServerURI", m_registry, out invServerURL))
            {
                XInventoryServicesConnector xinv = new XInventoryServicesConnector (invServerURL + "xinventory");
                InventoryItemBase it = xinv.GetItem (item);
                if (GetHandlers.GetIsForeign (item.Owner, "AssetServerURI", m_registry, out assetServerURL))
                {
                    GetAssets (it, assetServerURL + "assets");
                }
                return it;
            }
            else
            {
                InventoryItemBase it = base.GetItem (item);
                if(it != null)
                {
                    UserAccount user = m_UserAccountService.GetUserAccount(null, UUID.Parse(it.CreatorId));

                    // Adjust the creator data
                    if(user != null && it != null && (it.CreatorData == null || it.CreatorData == string.Empty))
                        it.CreatorData = GetHandlers.PROFILE_URL + "/" + it.CreatorId + ";" + user.FirstName + " " + user.LastName;
                }
                return it;
            }
        }

        private void GetAssets (InventoryItemBase it, string assetServer)
        {
            Dictionary<UUID, AssetType> ids = new Dictionary<UUID, AssetType> ();
            OpenSim.Region.Framework.Scenes.UuidGatherer uuidg = new OpenSim.Region.Framework.Scenes.UuidGatherer (m_registry.RequestModuleInterface<IAssetService> ());
            uuidg.GatherAssetUuids (it.AssetID, (AssetType)it.AssetType, ids, m_registry);
            //if (ids.ContainsKey (it.AssetID))
            //    ids.Remove (it.AssetID);
            foreach (UUID uuid in ids.Keys)
                FetchAsset (assetServer, uuid);
        }

        public AssetBase FetchAsset (string url, UUID assetID)
        {
            AssetBase asset = m_registry.RequestModuleInterface<IAssetService>().Get (url + "/" + assetID.ToString ());
            if (asset != null)
            {
                m_registry.RequestModuleInterface<IAssetService> ().Store (asset);
                MainConsole.Instance.DebugFormat ("[HG ASSET MAPPER]: Copied asset {0} from {1} to local asset server. ", asset.ID, url);
                return asset;
            }
            return null;
        }
    }
}
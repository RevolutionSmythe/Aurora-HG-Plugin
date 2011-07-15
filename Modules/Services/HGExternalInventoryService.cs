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
using log4net;
using Nini.Config;
using System.Reflection;
using OpenSim.Services.Interfaces;
using OpenSim.Services.InventoryService;
using OpenSim.Framework;
using OpenSim.Services;
using OpenSim.Services.Connectors;

namespace Aurora.Addon.Hypergrid
{
    /// <summary>
    /// Hypergrid inventory service. It serves the IInventoryService interface,
    /// but implements it in ways that are appropriate for inter-grid
    /// inventory exchanges. Specifically, it does not performs deletions
    /// and it responds to GetRootFolder requests with the ID of the
    /// Suitcase folder, not the actual "My Inventory" folder.
    /// </summary>
    public class HGInventoryService : InventoryService, IExternalInventoryService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger (
                MethodBase.GetCurrentMethod ().DeclaringType);

        private string m_ProfileServiceURL;
        private IRegistryCore m_registry;

        public override void Initialize (IConfigSource config, IRegistryCore registry)
        {
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            IConfig handlerConfig = config.Configs["Handlers"];
            if (handlerConfig.GetString ("ExternalInventoryHandler", "") != Name)
                return;

            m_registry = registry;
            registry.RegisterModuleInterface<IExternalInventoryService> (this);
            m_UserAccountService = registry.RequestModuleInterface<IUserAccountService> ();
            m_ProfileServiceURL = GetHandlers.PROFILE_URL;

            if (handlerConfig.GetString ("InventoryHandler", "") != Name)
                return;
            registry.RegisterModuleInterface<IInventoryService> (this);
        }

        public override void FinishedStartup ()
        {
            if (m_registry == null)//Not initialized
                return;
            IInventoryData invData = Aurora.DataManager.DataManager.RequestPlugin<IInventoryData> ();
            if (invData != null && invData is HGInventoryConnector)
                ((HGInventoryConnector)invData).AddInvHooks (m_registry);//Tell it about any refs it needs for HG inv

            InventoryInConnector inConnector = m_registry.RequestModuleInterface<InventoryInConnector> ();
            if (inConnector != null)//Add the external handler
                inConnector.AddExistingUrlForClient ("", "/xinventory", 0);
            base.FinishedStartup ();
        }

        public override List<InventoryFolderBase> GetInventorySkeleton (UUID principalID)
        {
            // NOGO for this inventory service
            return new List<InventoryFolderBase> ();
        }

        public override bool AddItem (InventoryItemBase item)
        {
            //            m_log.DebugFormat(
            //                "[XINVENTORY SERVICE]: Adding item {0} to folder {1} for {2}", item.ID, item.Folder, item.Owner);

            item.Folder = GetRootFolder (item.Owner).ID;//All items into the foreign folder please!
            m_Database.IncrementFolder (item.Folder);
            return m_Database.StoreItem (item);
        }

        public override InventoryFolderBase GetRootFolder (UUID principalID)
        {
            //m_log.DebugFormat("[HG INVENTORY SERVICE]: GetRootFolder for {0}", principalID);
            // Warp! Root folder for travelers
            List<InventoryFolderBase> folders = m_Database.GetFolders (
                    new string[] { "agentID", "type", "folderName" },
                    new string[] { principalID.ToString (), ((int)AssetType.LostAndFoundFolder).ToString (), "My Foreign Items" });
            if (folders.Count > 0)
                return folders[0];
            InventoryFolderBase realRoot = base.GetRootFolder (principalID);
            // make one
            InventoryFolderBase suitcase = CreateFolder (principalID, realRoot.ID, (int)AssetType.LostAndFoundFolder, "My Foreign Items");
            return suitcase;
        }

        //private bool CreateSystemFolders(UUID principalID, XInventoryFolder suitcase)
        //{

        //    CreateFolder(principalID, suitcase.folderID, (int)AssetType.Animation, "Animations");
        //    CreateFolder(principalID, suitcase.folderID, (int)AssetType.Bodypart, "Body Parts");
        //    CreateFolder(principalID, suitcase.folderID, (int)AssetType.CallingCard, "Calling Cards");
        //    CreateFolder(principalID, suitcase.folderID, (int)AssetType.Clothing, "Clothing");
        //    CreateFolder(principalID, suitcase.folderID, (int)AssetType.Gesture, "Gestures");
        //    CreateFolder(principalID, suitcase.folderID, (int)AssetType.Landmark, "Landmarks");
        //    CreateFolder(principalID, suitcase.folderID, (int)AssetType.LostAndFoundFolder, "Lost And Found");
        //    CreateFolder(principalID, suitcase.folderID, (int)AssetType.Notecard, "Notecards");
        //    CreateFolder(principalID, suitcase.folderID, (int)AssetType.Object, "Objects");
        //    CreateFolder(principalID, suitcase.folderID, (int)AssetType.SnapshotFolder, "Photo Album");
        //    CreateFolder(principalID, suitcase.folderID, (int)AssetType.LSLText, "Scripts");
        //    CreateFolder(principalID, suitcase.folderID, (int)AssetType.Sound, "Sounds");
        //    CreateFolder(principalID, suitcase.folderID, (int)AssetType.Texture, "Textures");
        //    CreateFolder(principalID, suitcase.folderID, (int)AssetType.TrashFolder, "Trash");

        //    return true;
        //}

        public override bool CreateUserInventory (UUID principalID, bool createDefaultItems)
        {
            return false;
        }

        public override bool CreateUserRootFolder (UUID principalID)
        {
            return false;
        }

        public override InventoryFolderBase GetFolderForType (UUID principalID, InventoryType invType, AssetType type)
        {
            return GetRootFolder (principalID);
        }

        //
        // Use the inherited methods
        //
        //public InventoryCollection GetFolderContent(UUID principalID, UUID folderID)
        //{
        //}

        //public List<InventoryItemBase> GetFolderItems(UUID principalID, UUID folderID)
        //{
        //}

        //public override bool AddFolder(InventoryFolderBase folder)
        //{
        //    // Check if it's under the Suitcase folder
        //    List<InventoryFolderBase> skel = base.GetInventorySkeleton(folder.Owner);
        //    InventoryFolderBase suitcase = GetRootFolder(folder.Owner);
        //    List<InventoryFolderBase> suitDescendents = GetDescendents(skel, suitcase.ID);

        //    foreach (InventoryFolderBase f in suitDescendents)
        //        if (folder.ParentID == f.ID)
        //        {
        //            XInventoryFolder xFolder = ConvertFromOpenSim(folder);
        //            return m_Database.StoreFolder(xFolder);
        //        }
        //    return false;
        //}

        private List<InventoryFolderBase> GetDescendents (List<InventoryFolderBase> lst, UUID root)
        {
            List<InventoryFolderBase> direct = lst.FindAll (delegate (InventoryFolderBase f)
            {
                return f.ParentID == root;
            });
            if (direct == null)
                return new List<InventoryFolderBase> ();

            List<InventoryFolderBase> indirect = new List<InventoryFolderBase> ();
            foreach (InventoryFolderBase f in direct)
                indirect.AddRange (GetDescendents (lst, f.ID));

            direct.AddRange (indirect);
            return direct;
        }

        // Use inherited method
        //public bool UpdateFolder(InventoryFolderBase folder)
        //{
        //}

        //public override bool MoveFolder(InventoryFolderBase folder)
        //{
        //    XInventoryFolder[] x = m_Database.GetFolders(
        //            new string[] { "folderID" },
        //            new string[] { folder.ID.ToString() });

        //    if (x.Length == 0)
        //        return false;

        //    // Check if it's under the Suitcase folder
        //    List<InventoryFolderBase> skel = base.GetInventorySkeleton(folder.Owner);
        //    InventoryFolderBase suitcase = GetRootFolder(folder.Owner);
        //    List<InventoryFolderBase> suitDescendents = GetDescendents(skel, suitcase.ID);

        //    foreach (InventoryFolderBase f in suitDescendents)
        //        if (folder.ParentID == f.ID)
        //        {
        //            x[0].parentFolderID = folder.ParentID;
        //            return m_Database.StoreFolder(x[0]);
        //        }

        //    return false;
        //}

        public override bool DeleteFolders (UUID principalID, List<UUID> folderIDs)
        {
            // NOGO
            return false;
        }

        public override bool PurgeFolder (InventoryFolderBase folder)
        {
            // NOGO
            return false;
        }

        // Unfortunately we need to use the inherited method because of how DeRez works.
        // The viewer sends the folderID hard-wired in the derez message
        //public override bool AddItem(InventoryItemBase item)
        //{
        //    // Check if it's under the Suitcase folder
        //    List<InventoryFolderBase> skel = base.GetInventorySkeleton(item.Owner);
        //    InventoryFolderBase suitcase = GetRootFolder(item.Owner);
        //    List<InventoryFolderBase> suitDescendents = GetDescendents(skel, suitcase.ID);

        //    foreach (InventoryFolderBase f in suitDescendents)
        //        if (item.Folder == f.ID)
        //            return m_Database.StoreItem(ConvertFromOpenSim(item));

        //    return false;
        //}

        //public override bool UpdateItem(InventoryItemBase item)
        //{
        //    // Check if it's under the Suitcase folder
        //    List<InventoryFolderBase> skel = base.GetInventorySkeleton(item.Owner);
        //    InventoryFolderBase suitcase = GetRootFolder(item.Owner);
        //    List<InventoryFolderBase> suitDescendents = GetDescendents(skel, suitcase.ID);

        //    foreach (InventoryFolderBase f in suitDescendents)
        //        if (item.Folder == f.ID)
        //            return m_Database.StoreItem(ConvertFromOpenSim(item));

        //    return false;
        //}

        //public override bool MoveItems(UUID principalID, List<InventoryItemBase> items)
        //{
        //    // Principal is b0rked. *sigh*
        //    //
        //    // Let's assume they all have the same principal
        //    // Check if it's under the Suitcase folder
        //    List<InventoryFolderBase> skel = base.GetInventorySkeleton(items[0].Owner);
        //    InventoryFolderBase suitcase = GetRootFolder(items[0].Owner);
        //    List<InventoryFolderBase> suitDescendents = GetDescendents(skel, suitcase.ID);

        //    foreach (InventoryItemBase i in items)
        //    {
        //        foreach (InventoryFolderBase f in suitDescendents)
        //            if (i.Folder == f.ID)
        //                m_Database.MoveItem(i.ID.ToString(), i.Folder.ToString());
        //    }

        //    return true;
        //}

        // Let these pass. Use inherited methods.
        //public bool DeleteItems(UUID principalID, List<UUID> itemIDs)
        //{
        //}

        public override InventoryItemBase GetItem (InventoryItemBase item)
        {
            InventoryItemBase it = base.GetItem (item);

            //UserAccount user = m_UserAccountService.GetUserAccount (UUID.Zero, UUID.Parse(it.CreatorId));

            // Adjust the creator data
            //if (user != null && it != null && (it.CreatorData == null || it.CreatorData == string.Empty))
            //    it.CreatorData = m_ProfileServiceURL + "/" + it.CreatorId + ";" + user.FirstName + " " + user.LastName;

            return it;
        }
    }
}
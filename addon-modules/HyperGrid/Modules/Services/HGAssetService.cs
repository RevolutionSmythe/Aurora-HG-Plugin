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
using System.IO;
using System.Reflection;
using System.Xml;

using Nini.Config;
using OpenMetaverse;

using Aurora.Framework;
using Aurora.Framework.Serialization.External;
using OpenSim.Services.Interfaces;
using OpenSim.Services.AssetService;
using OpenSim.Services;

namespace Aurora.Addon.HyperGrid
{
    /// <summary>
    /// Hypergrid asset service. It serves the IAssetService interface,
    /// but implements it in ways that are appropriate for inter-grid
    /// asset exchanges.
    /// </summary>
    public class HGAssetService : AssetService, IExternalAssetService
    {
        private string m_ProfileServiceURL;
        private IUserAccountService m_UserAccountService;

        public override string Name
        {
            get
            {
                return GetType ().Name;
            }
        }

        public override void Initialize (IConfigSource config, IRegistryCore registry)
        {
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            Configure (config, registry);
            m_ProfileServiceURL = GetHandlers.PROFILE_URL;
            registry.RegisterModuleInterface<IExternalAssetService> (this);

            IConfig handlerConfig = config.Configs["Handlers"];
            if (handlerConfig.GetString ("AssetHandler", "") != Name)
                return;

            registry.RegisterModuleInterface<IAssetService>(this);
            Init(registry, Name);
        }

        public override void Start (IConfigSource config, IRegistryCore registry)
        {
            m_UserAccountService = registry.RequestModuleInterface<IUserAccountService> ();
        }

        public override void FinishedStartup ()
        {
            if (m_registry == null)
                return;
            AssetServiceConnector assetHandler = m_registry.RequestModuleInterface<AssetServiceConnector> ();
            if (assetHandler != null)//Add the external handler
                assetHandler.AddExistingUrlForClient ("", "/assets", 0);
        }

        #region IAssetService overrides

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Low)]
        public override AssetBase Get(string id)
        {
            AssetBase asset = base.Get (id);

            if (asset == null)
                return null;

            if (asset.Type == (sbyte)AssetType.Object)
                asset.Data = AdjustIdentifiers (asset.Data);

            AdjustIdentifiers (asset);

            return asset;
        }

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Low)]
        public override byte[] GetData(string id)
        {
            byte[] data = base.GetData (id);

            if (data == null)
                return null;

            return AdjustIdentifiers (data);
        }

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Low)]
        public override AssetBase GetCached(string id)
        {
            AssetBase asset = base.GetCached (id);
            if(asset != null)
                AdjustIdentifiers (asset);
            return asset;
        }

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Full)]
        public override bool Delete(UUID id)
        {
            // NOGO
            return false;
        }

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Low)]
        public override UUID Store(AssetBase asset)
        {
            return base.Store(asset);
        }

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Low)]
        public override void Get(string id, object sender, AssetRetrieved handler)
        {
            base.Get(id, sender, handler);
        }

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Low)]
        public override bool GetExists(string id)
        {
            return base.GetExists(id);
        }

        [CanBeReflected(ThreatLevel = OpenSim.Services.Interfaces.ThreatLevel.Low)]
        public override UUID UpdateContent(UUID id, byte[] data)
        {
            return base.UpdateContent(id, data);
        }

        #endregion

        /*protected override void FixAssetID (ref AssetBase asset)
        {
            if (asset == null || asset.Metadata.URL != "")//Don't reappend
                return;
            asset.URL = MainServer.Instance.FullHostName + ":" + MainServer.Instance.Port + "/assets/" + asset.ID;
            //asset.ID = MainServer.Instance.FullHostName + ":" + MainServer.Instance.Port + "/assets/" + asset.ID;
        }*/

        protected void AdjustIdentifiers (AssetBase meta)
        {
            /*if (meta.CreatorID != null && meta.CreatorID != UUID.Zero)
            {
                UserAccount creator = m_UserAccountService.GetUserAccount (null, uuid);
                if (creator != null)
                    meta.CreatorID = m_ProfileServiceURL + "/" + meta.CreatorID + ";" + creator.FirstName + " " + creator.LastName;
            }*/
        }

        protected byte[] AdjustIdentifiers (byte[] data)
        {
            string xml = Utils.BytesToString (data);
            return Utils.StringToBytes (ExternalRepresentationUtils.RewriteSOP (xml, m_ProfileServiceURL, m_UserAccountService, UUID.Zero));
        }
    }

}

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
using log4net;
using OpenMetaverse;

using OpenSim.Framework;
using OpenSim.Framework.Serialization.External;
using OpenSim.Services.Interfaces;
using OpenSim.Services.AssetService;
using OpenSim.Services;

namespace Aurora.Addon.Hypergrid
{
    /// <summary>
    /// Hypergrid asset service. It serves the IAssetService interface,
    /// but implements it in ways that are appropriate for inter-grid
    /// asset exchanges.
    /// </summary>
    public class HGAssetService : OpenSim.Services.AssetService.AssetService, IAssetService
    {
        private static readonly ILog m_log =
            LogManager.GetLogger (
            MethodBase.GetCurrentMethod ().DeclaringType);

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
            IConfig handlerConfig = config.Configs["Handlers"];
            if (handlerConfig.GetString ("AssetHandler", "") != Name)
                return;
            m_log.Debug ("[HGAsset Service]: Starting");
            Configure (config, registry);
            m_ProfileServiceURL = GetHandlers.PROFILE_URL;
        }

        public override void Start (IConfigSource config, IRegistryCore registry)
        {
            m_UserAccountService = registry.RequestModuleInterface<IUserAccountService> ();
        }

        public override void FinishedStartup ()
        {
            AssetServiceConnector assetHandler = m_registry.RequestModuleInterface<AssetServiceConnector> ();
            if (assetHandler != null)//Add the external handler
                assetHandler.AddExistingUrlForClient ("", "/assets", 0);
        }

        #region IAssetService overrides
        public override AssetBase Get (string id)
        {
            string url = string.Empty;
            string assetID = id;
            if (StringToUrlAndAssetID (id, out url, out assetID))
            {
                IAssetService connector = GetConnector (url);
                return connector.Get (assetID);
            }

            AssetBase asset = base.Get (id);

            if (asset == null)
                return null;

            if (asset.Metadata.Type == (sbyte)AssetType.Object)
                asset.Data = AdjustIdentifiers (asset.Data);

            AdjustIdentifiers (asset.Metadata);

            return asset;
        }

        public override AssetMetadata GetMetadata (string id)
        {
            string url = string.Empty;
            string assetID = string.Empty;

            if (StringToUrlAndAssetID (id, out url, out assetID))
            {
                IAssetService connector = GetConnector (url);
                return connector.GetMetadata (assetID);
            }

            AssetMetadata meta = base.GetMetadata (id);

            if (meta == null)
                return null;

            AdjustIdentifiers (meta);

            return meta;
        }

        public override byte[] GetData (string id)
        {
            byte[] data = base.GetData (id);

            if (data == null)
                return null;

            return AdjustIdentifiers (data);
        }

        //public virtual bool Get(string id, Object sender, AssetRetrieved handler)

        public override AssetBase GetCached (string id)
        {
            string url = string.Empty;
            string assetID = string.Empty;

            if (StringToUrlAndAssetID (id, out url, out assetID))
            {
                IAssetService connector = GetConnector (url);
                return connector.GetCached (assetID);
            }
            AssetBase asset = base.GetCached (id);
            AdjustIdentifiers (asset.Metadata);
            return asset;
        }

        public override bool Get (string id, object sender, AssetRetrieved handler)
        {
            string url = string.Empty;
            string assetID = string.Empty;

            if (StringToUrlAndAssetID (id, out url, out assetID))
            {
                IAssetService connector = GetConnector (url);
                return connector.Get (assetID, sender, handler);
            }
            return base.Get (id, sender, handler);
        }

        public override string Store (AssetBase asset)
        {
            string url = string.Empty;
            string assetID = string.Empty;

            if (StringToUrlAndAssetID (asset.ID, out url, out assetID))
            {
                IAssetService connector = GetConnector (url);
                // Restore the assetID to a simple UUID
                asset.ID = assetID;
                return connector.Store (asset);
            }
            return base.Store (asset);
        }

        public override bool Delete (string id)
        {
            // NOGO
            return false;
        }

        #endregion

        protected void AdjustIdentifiers (AssetMetadata meta)
        {
            UserAccount creator = m_UserAccountService.GetUserAccount (UUID.Zero, UUID.Parse(meta.CreatorID));
            if (creator != null)
                meta.CreatorID = m_ProfileServiceURL + "/" + meta.CreatorID + ";" + creator.FirstName + " " + creator.LastName;
        }

        protected byte[] AdjustIdentifiers (byte[] data)
        {
            string xml = Utils.BytesToString (data);
            return Utils.StringToBytes (ExternalRepresentationUtils.RewriteSOP (xml, m_ProfileServiceURL, m_UserAccountService, UUID.Zero));
        }

        private Dictionary<string, IAssetService> m_connectors = new Dictionary<string, IAssetService> ();

        private IAssetService GetConnector (string url)
        {
            IAssetService connector = null;
            lock (m_connectors)
            {
                if (m_connectors.ContainsKey (url))
                {
                    connector = m_connectors[url];
                }
                else
                {
                    // Still not as flexible as I would like this to be,
                    // but good enough for now
                    string connectorType = new HeloServicesConnector (url).Helo ();
                    m_log.DebugFormat ("[HG ASSET SERVICE]: HELO returned {0}", connectorType);
                    if (connectorType == "opensim-simian")
                        connector = new OpenSim.Services.Connectors.SimianGrid.SimianAssetServiceConnector (url);
                    else
                        connector = new OpenSim.Services.Connectors.AssetServicesConnector (url);

                    m_connectors.Add (url, connector);
                }
            }
            return connector;
        }

        private bool StringToUrlAndAssetID (string id, out string url, out string assetID)
        {
            url = String.Empty;
            assetID = String.Empty;

            Uri assetUri;

            if (Uri.TryCreate (id, UriKind.Absolute, out assetUri) &&
                    assetUri.Scheme == Uri.UriSchemeHttp)
            {
                url = "http://" + assetUri.Authority;
                assetID = assetUri.LocalPath.Trim (new char[] { '/' });
                return true;
            }

            return false;
        }
    }

}

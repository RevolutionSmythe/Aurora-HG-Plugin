/*
 * Copyright (c) Contributors, http://aurora-sim.org/, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Aurora-Sim Project nor the
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
using System.IO;
using System.Net;
using System.Reflection;
using System.Xml.Serialization;
using Aurora.Simulation.Base;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Aurora.Framework;
using Aurora.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using Aurora.Addon.HyperGrid;

namespace OpenSim.Services
{
    public class AssetServiceConnector : IService, IGridRegistrationUrlModule
    {
        private string m_ConfigName = "AssetService";
        private bool m_allowDelete;
        private IRegistryCore m_registry;

        public string Name
        {
            get { return GetType().Name; }
        }

        #region IGridRegistrationUrlModule Members

        public string UrlName
        {
            get { return "AssetServerURI"; }
        }

        public void AddExistingUrlForClient(string SessionID, string url, uint port)
        {
            IHttpServer server = m_registry.RequestModuleInterface<ISimulationBase>().GetHttpServer(port);

            server.AddStreamHandler(new AssetServerGetHandler(GetService(SessionID != "").InnerService, url, SessionID,
                                                              m_registry));
            server.AddStreamHandler(new AssetServerPostHandler(GetService(SessionID != "").InnerService, url, SessionID,
                                                               m_registry));
            server.AddStreamHandler(new AssetServerDeleteHandler(GetService(SessionID != "").InnerService, m_allowDelete,
                                                                 url, SessionID, m_registry));
        }

        public string GetUrlForRegisteringClient(string SessionID, uint port)
        {
            IHttpServer server = m_registry.RequestModuleInterface<ISimulationBase>().GetHttpServer(port);
            string url = "/assets" + UUID.Random();

            server.AddStreamHandler(new AssetServerGetHandler(GetService(SessionID != "").InnerService, url, SessionID,
                                                              m_registry));
            server.AddStreamHandler(new AssetServerPostHandler(GetService(SessionID != "").InnerService, url, SessionID,
                                                               m_registry));
            server.AddStreamHandler(new AssetServerDeleteHandler(GetService(SessionID != "").InnerService, m_allowDelete,
                                                                 url, SessionID, m_registry));

            return url;
        }

        public void RemoveUrlForClient(string sessionID, string url, uint port)
        {
            IHttpServer server = m_registry.RequestModuleInterface<ISimulationBase>().GetHttpServer(port);
            server.RemoveHTTPHandler("POST", url);
            server.RemoveHTTPHandler("GET", url);
            server.RemoveHTTPHandler("DELETE", url);
        }

        #endregion

        #region IService Members

        public void Initialize(IConfigSource config, IRegistryCore registry)
        {
        }

        public void Start(IConfigSource config, IRegistryCore registry)
        {
            IConfig handlerConfig = config.Configs["Handlers"];
            if (handlerConfig.GetString("AssetInHandler", "") != Name)
                return;

            m_registry = registry;
            m_registry.RegisterModuleInterface(this);

            IConfig serverConfig = config.Configs[m_ConfigName];
            m_allowDelete = serverConfig != null && serverConfig.GetBoolean("AllowRemoteDelete", false);

            m_registry.RequestModuleInterface<IGridRegistrationService>().RegisterModule(this);
        }

        public void FinishedStartup()
        {
        }

        #endregion

        public IAssetService GetService(bool isSecure)
        {
            IAssetService assetService = m_registry.RequestModuleInterface<IExternalAssetService>();
            if (!isSecure && assetService != null)
                return assetService;
            return m_registry.RequestModuleInterface<IAssetService>();
        }


        public bool DoMultiplePorts
        {
            get { return false; }
        }
    }

    public class AssetServerPostHandler : BaseRequestHandler
    {
        // private static readonly ILog MainConsole.Instance = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly IAssetService m_AssetService;
        protected string m_SessionID;
        protected IRegistryCore m_registry;

        public AssetServerPostHandler(IAssetService service, string url, string SessionID, IRegistryCore registry) :
            base("POST", url)
        {
            m_AssetService = service;
            m_SessionID = SessionID;
            m_registry = registry;
        }

        public override byte[] Handle(string path, Stream request,
                OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            XmlSerializer xs = new XmlSerializer(typeof(AssetBase));
            AssetBase asset = (AssetBase)xs.Deserialize(request);

            string[] p = SplitParams(path);
            if (p.Length > 1)
            {
                bool result =
                        m_AssetService.UpdateContent(UUID.Parse(p[1]), asset.Data) != UUID.Zero;

                xs = new XmlSerializer(typeof(bool));
                return ServerUtils.SerializeResult(xs, result);
            }

            string id = m_AssetService.Store(asset).ToString();

            xs = new XmlSerializer(typeof(string));
            return ServerUtils.SerializeResult(xs, id);
        }
    }

    public class AssetServerDeleteHandler : BaseRequestHandler
    {
        // private static readonly ILog MainConsole.Instance = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly IAssetService m_AssetService;
        protected string m_SessionID;
        protected bool m_allowDelete;
        protected IRegistryCore m_registry;

        public AssetServerDeleteHandler(IAssetService service, bool allowDelete, string url, string SessionID,
                                        IRegistryCore registry) :
            base("DELETE", url)
        {
            m_AssetService = service;
            m_allowDelete = allowDelete;
            m_SessionID = SessionID;
            m_registry = registry;
        }

        public override byte[] Handle(string path, Stream request,
                                      OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            bool result = false;

            string[] p = SplitParams(path);

            IGridRegistrationService urlModule =
                m_registry.RequestModuleInterface<IGridRegistrationService>();
            if (m_SessionID != "" && urlModule != null)
                if (!urlModule.CheckThreatLevel(m_SessionID, "Asset_Delete", ThreatLevel.High))
                    return new byte[0];
            if (p.Length > 0 && m_allowDelete)
            {
                result = m_AssetService.Delete(UUID.Parse(p[0]));
            }

            XmlSerializer xs = new XmlSerializer(typeof(bool));
            return ServerUtils.SerializeResult(xs, result);
        }
    }

    public class AssetServerGetHandler : BaseRequestHandler
    {
        private readonly IAssetService m_AssetService;
        protected string m_SessionID;
        protected IRegistryCore m_registry;

        public AssetServerGetHandler(IAssetService service, string url, string SessionID, IRegistryCore registry) :
            base("GET", url)
        {
            m_AssetService = service;
            m_SessionID = SessionID;
            m_registry = registry;
        }

        public override byte[] Handle(string path, Stream request,
                                      OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            byte[] result = new byte[0];

            string[] p = SplitParams(path);

            if (p.Length == 0)
                return result;

            IGridRegistrationService urlModule =
                m_registry.RequestModuleInterface<IGridRegistrationService>();
            if (m_SessionID != "" && urlModule != null)
                if (!urlModule.CheckThreatLevel(m_SessionID, "Asset_Get", ThreatLevel.Low))
                    return new byte[0];
            if (p.Length > 1 && p[1] == "data")
            {
                result = m_AssetService.GetData(p[0]);
                if (result == null)
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                    httpResponse.ContentType = "text/plain";
                    result = new byte[0];
                }
                else
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.OK;
                    httpResponse.ContentType = "application/octet-stream";
                }
            }
            else if (p.Length > 1 && p[1] == "exists")
            {
                try
                {
                    bool RetVal = m_AssetService.GetExists(p[0]);
                    XmlSerializer xs =
                        new XmlSerializer(typeof(AssetBase));
                    result = ServerUtils.SerializeResult(xs, RetVal);

                    if (result == null)
                    {
                        httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                        httpResponse.ContentType = "text/plain";
                        result = new byte[0];
                    }
                    else
                    {
                        httpResponse.StatusCode = (int)HttpStatusCode.OK;
                        httpResponse.ContentType = "application/octet-stream";
                    }
                }
                catch (Exception ex)
                {
                    result = new byte[0];
                    MainConsole.Instance.Warn("[AssetServerGetHandler]: Error serializing the result for /exists for asset " + p[0] +
                               ", " + ex);
                }
            }
            else
            {
                AssetBase asset = m_AssetService.Get(p[0]);

                if (asset != null)
                {
                    XmlSerializer xs = new XmlSerializer(typeof(AssetBase));
                    result = Util.CompressBytes(ServerUtils.SerializeResult(xs, asset));

                    httpResponse.StatusCode = (int)HttpStatusCode.OK;
                    httpResponse.ContentType =
                        SLUtil.SLAssetTypeToContentType(asset.Type) + "/gzip";
                }
                else
                {
                    httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                    httpResponse.ContentType = "text/plain";
                    result = new byte[0];
                }
            }
            return result;
        }
    }
}
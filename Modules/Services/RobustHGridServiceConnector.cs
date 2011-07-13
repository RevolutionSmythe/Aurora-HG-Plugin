/*
 * Copyright (c) Contributors, http://aurora-sim.org/
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

using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using Aurora.Simulation.Base;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Connectors;
using OpenSim.Services.GridService;

namespace Aurora.Addon.Hypergrid
{
    public class RobustHGridServicesConnector : GridService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        #region IGridService

        public override List<GridRegion> GetRegionsByName (UUID scopeID, string name, int maxNumber)
        {
            return AddHGRegions(scopeID, name, base.GetRegionsByName (scopeID, name, maxNumber));
        }

        private List<GridRegion> AddHGRegions (UUID scopeID, string name, List<GridRegion> list)
        {
            HypergridLinker linker = m_registryCore.RequestModuleInterface<HypergridLinker> ();
            if (list.Count == 0)
            {
                if (IsHGURL (name))
                {
                    GridRegion r = linker.LinkRegion (scopeID, name);
                    if (r != null)
                        list.Add (r);
                }
            }
            return list;
        }

        private bool IsHGURL (string name)
        {
            string[] split = name.Split (':');
            if (split.Length < 2)
                return false;
            uint port;
            if (split[0].StartsWith ("http") && uint.TryParse (split[1], out port))
                return true;
            return false;
        }

        #endregion

        #region IService Members

        public override string Name
        {
            get { return GetType().Name; }
        }

        public override void Initialize (IConfigSource config, IRegistryCore registry)
        {
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            IConfig handlerConfig = config.Configs["Handlers"];
            if (handlerConfig.GetString("GridHandler", "") != Name)
                return;

            //m_log.DebugFormat("[GRID SERVICE]: Starting...");
            Configure(config, registry);
        }

        #endregion
    }
}

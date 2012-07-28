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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Xml;

using Nini.Config;
using Aurora.Framework;
using Aurora.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using OpenMetaverse;
using Aurora.Simulation.Base;
using Aurora.DataManager;

namespace Aurora.Addon.HyperGrid
{
    public class HypergridLinker : IService, ICommunicationService
    {
        private static uint m_autoMappingX = 0;
        private static uint m_autoMappingY = 0;
        private static bool m_enableAutoMapping = false;

        protected IGridService m_GridService;
        protected IAssetService m_AssetService;
        protected IRegionData m_Database = null;
        protected GatekeeperServiceConnector m_GatekeeperConnector;

        protected UUID m_ScopeID = UUID.Zero;
        protected bool m_Check4096 = true;
        protected string m_MapTileDirectory = "hgmaptiles";
        protected string m_ThisGatekeeper = string.Empty;
        protected Uri m_ThisGatekeeperURI = null;

        // Hyperlink regions are hyperlinks on the map
        public readonly Dictionary<UUID, GridRegion> m_HyperlinkRegions = new Dictionary<UUID, GridRegion> ();
        protected Dictionary<UUID, ulong> m_HyperlinkHandles = new Dictionary<UUID, ulong> ();

        protected GridRegion m_DefaultRegion;
        protected GridRegion DefaultRegion
        {
            get
            {
                if (m_DefaultRegion == null)
                {
                    List<GridRegion> defs = m_GridService.GetDefaultRegions(null);
                    if (defs != null && defs.Count > 0)
                        m_DefaultRegion = defs[0];
                    else
                    {
                        // Get any region
                        defs = m_GridService.GetRegionsByName(null, "", null, null);
                        if (defs != null && defs.Count > 0)
                            m_DefaultRegion = defs[0];
                        else
                        {
                            // This shouldn't happen
                            m_DefaultRegion = new GridRegion ();
                            m_DefaultRegion.RegionLocX = Constants.RegionSize * 1000;
                            m_DefaultRegion.RegionLocY = Constants.RegionSize * 1000;
                            MainConsole.Instance.Error ("[HYPERGRID LINKER]: Something is wrong with this grid. It has no regions?");
                        }
                    }
                }
                return m_DefaultRegion;
            }
        }

        #region IService Members

        public void Initialize (IConfigSource config, IRegistryCore registry)
        {
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            m_Check4096 = hgConfig.GetBoolean ("Check4096", true);
            m_MapTileDirectory = hgConfig.GetString ("MapTileDirectory", "hgmaptiles");

            hgConfig = config.Configs["HyperGridLinker"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            registry.RegisterModuleInterface<HypergridLinker> (this);//Add the interface
            hgConfig = config.Configs["GatekeeperService"];

            IHttpServer server = MainServer.Instance;
            m_ThisGatekeeperURI = new Uri (server.FullHostName + ":" + server.Port);

            if (!string.IsNullOrEmpty (m_MapTileDirectory))
            {
                try
                {
                    Directory.CreateDirectory (m_MapTileDirectory);
                }
                catch (Exception e)
                {
                    MainConsole.Instance.WarnFormat ("[HYPERGRID LINKER]: Could not create map tile storage directory {0}: {1}", m_MapTileDirectory, e);
                    m_MapTileDirectory = string.Empty;
                }
            }

            if (MainConsole.Instance != null)
            {
                MainConsole.Instance.Info("[HYPERGRID LINKER]: Enabled");
                MainConsole.Instance.Commands.AddCommand("link-region",
                    "link-region <Xloc> <Yloc> <ServerURI> [<RemoteRegionName>]",
                    "Link a HyperGrid Region. Examples for <ServerURI>: http://grid.net:8002/ or http://example.org/path/foo.php", RunCommand);
                MainConsole.Instance.Commands.AddCommand ("unlink-region",
                    "unlink-region <local name>",
                    "Unlink a hypergrid region", RunCommand);
                MainConsole.Instance.Commands.AddCommand ("link-mapping", "link-mapping [<x> <y>]",
                    "Set local coordinate to map HG regions to", RunCommand);
                MainConsole.Instance.Commands.AddCommand ("show hyperlinks", "show hyperlinks",
                    "List the HG regions", HandleShow);
            }
        }

        public void Start (IConfigSource config, IRegistryCore registry)
        {
            m_AssetService = registry.RequestModuleInterface<IAssetService> ();
            m_GridService = registry.RequestModuleInterface<IGridService> ();
            m_GatekeeperConnector = new GatekeeperServiceConnector (m_AssetService);
            m_Database = Aurora.DataManager.DataManager.RequestPlugin<IRegionData> ();

            MainConsole.Instance.Debug ("[HYPERGRID LINKER]: Loaded all services...");
        }

        public void FinishedStartup ()
        {
        }

        #endregion


        #region Link Region

        // from map search
        public GridRegion LinkRegion (UUID scopeID, string regionDescriptor)
        {
            string reason = string.Empty;
            int xloc = random.Next(0, Int16.MaxValue) * (int)Constants.RegionSize;
            int yloc = random.Next(0, Int16.MaxValue) * (int)Constants.RegionSize;
            return TryLinkRegionToCoords (scopeID, regionDescriptor, xloc, yloc, out reason);
        }

        private static Random random = new Random ();

        // From the command line link-region (obsolete) and the map
        public GridRegion TryLinkRegionToCoords (UUID scopeID, string mapName, int xloc, int yloc, out string reason)
        {
            return TryLinkRegionToCoords (scopeID, mapName, xloc, yloc, UUID.Zero, out reason);
        }

        public GridRegion TryLinkRegionToCoords (UUID scopeID, string mapName, int xloc, int yloc, UUID ownerID, out string reason)
        {
            reason = string.Empty;
            GridRegion regInfo = null;

            if (!mapName.StartsWith ("http"))
            {
                string host = "127.0.0.1";
                string portstr;
                string regionName = "";
                uint port = 0;
                string[] parts = mapName.Split (new char[] { ':' });
                if (parts.Length >= 1)
                {
                    host = parts[0];
                }
                if (parts.Length >= 2)
                {
                    portstr = parts[1];
                    //MainConsole.Instance.Debug("-- port = " + portstr);
                    if (!UInt32.TryParse (portstr, out port))
                        regionName = parts[1];
                }
                // always take the last one
                if (parts.Length >= 3)
                {
                    regionName = parts[2];
                }


                bool success = TryCreateLink (scopeID, xloc, yloc, regionName, port, host, ownerID, out regInfo, out reason);
                if (success)
                {
                    regInfo.RegionName = mapName;
                    return regInfo;
                }
            }
            else
            {
                string[] parts = mapName.Split (new char[] { ' ' });
                string regionName = String.Empty;
                if (parts.Length > 1)
                {
                    regionName = mapName.Substring (parts[0].Length + 1);
                    regionName = regionName.Trim (new char[] { '"' });
                }
                if (TryCreateLink (scopeID, xloc, yloc, regionName, 0, null, parts[0], ownerID, out regInfo, out reason))
                {
                    regInfo.RegionName = mapName;
                    return regInfo;
                }
            }

            return null;
        }

        public bool TryCreateLink (UUID scopeID, int xloc, int yloc, string remoteRegionName, uint externalPort, string externalHostName, UUID ownerID, out GridRegion regInfo, out string reason)
        {
            return TryCreateLink (scopeID, xloc, yloc, remoteRegionName, externalPort, externalHostName, null, ownerID, out regInfo, out reason);
        }

        public bool TryCreateLink (UUID scopeID, int xloc, int yloc, string remoteRegionName, uint externalPort, string externalHostName, string serverURI, UUID ownerID, out GridRegion regInfo, out string reason)
        {
            MainConsole.Instance.DebugFormat ("[HYPERGRID LINKER]: Link to {0} {1}, in {2}-{3}",
                ((serverURI == null) ? (externalHostName + ":" + externalPort) : serverURI),
                remoteRegionName, xloc / Constants.RegionSize, yloc / Constants.RegionSize);

            reason = string.Empty;
            Uri uri = null;

            regInfo = new GridRegion ();
            if (externalPort > 0)
                regInfo.HttpPort = externalPort;
            else
                regInfo.HttpPort = 0;
            if (externalHostName != null)
                regInfo.ExternalHostName = externalHostName;
            else
                regInfo.ExternalHostName = "0.0.0.0";
            if (serverURI != null)
            {
                regInfo.ServerURI = serverURI;
                try
                {
                    uri = new Uri (serverURI);
                    regInfo.ExternalHostName = uri.Host;
                    regInfo.HttpPort = (uint)uri.Port;
                }
                catch
                {
                }
            }

            if (!string.IsNullOrEmpty(remoteRegionName))
                regInfo.RegionName = remoteRegionName;

            regInfo.RegionLocX = xloc;
            regInfo.RegionLocY = yloc;
            regInfo.ScopeID = scopeID;
            regInfo.EstateOwner = ownerID;

            // Make sure we're not hyperlinking to regions on this grid!
            if (m_ThisGatekeeperURI != null)
            {
                if (regInfo.ExternalHostName == m_ThisGatekeeperURI.Host && regInfo.HttpPort == m_ThisGatekeeperURI.Port)
                {
                    reason = "Cannot hyperlink to regions on the same grid";
                    return false;
                }
            }
            else
                MainConsole.Instance.WarnFormat ("[HYPERGRID LINKER]: Please set this grid's Gatekeeper's address in [GridService]!");

            // Check for free coordinates
            GridRegion region = m_GridService.GetRegionByPosition(null, regInfo.RegionLocX, regInfo.RegionLocY);
            if (region != null)
            {
                MainConsole.Instance.WarnFormat ("[HYPERGRID LINKER]: Coordinates {0}-{1} are already occupied by region {2} with uuid {3}",
                    regInfo.RegionLocX / Constants.RegionSize, regInfo.RegionLocY / Constants.RegionSize,
                    region.RegionName, region.RegionID);
                reason = "Coordinates are already in use";
                return false;
            }

            try
            {
                regInfo.InternalEndPoint = new IPEndPoint (IPAddress.Parse ("0.0.0.0"), (int)0);
            }
            catch (Exception e)
            {
                MainConsole.Instance.Warn ("[HYPERGRID LINKER]: Wrong format for link-region: " + e.Message);
                reason = "Internal error";
                return false;
            }

            // Finally, link it
            ulong handle = 0;
            UUID regionID = UUID.Zero;
            string externalName = string.Empty;
            string imageURL = string.Empty;
            if (!m_GatekeeperConnector.LinkRegion (regInfo, out regionID, out handle, out externalName, out imageURL, out reason))
                return false;
            if (regionID == UUID.Zero)
            {
                MainConsole.Instance.Warn ("[HYPERGRID LINKER]: Unable to link region");
                reason = "Remote region could not be found";
                return false;
            }

            region = m_GridService.GetRegionByUUID (null, regionID);
            if (region != null)
            {
                MainConsole.Instance.DebugFormat ("[HYPERGRID LINKER]: Region already exists in coordinates {0} {1}",
                    region.RegionLocX / Constants.RegionSize, region.RegionLocY / Constants.RegionSize);
                regInfo = region;
                return true;
            }

            uint x, y;
            if (m_Check4096 && !Check4096 (handle, out x, out y))
            {
                RemoveHyperlinkRegion (regInfo.RegionID);
                reason = "Region is too far (" + x + ", " + y + ")";
                MainConsole.Instance.Info ("[HYPERGRID LINKER]: Unable to link, region is too far (" + x + ", " + y + ")");
                return false;
            }

            regInfo.RegionID = regionID;

            if (externalName == string.Empty)
                regInfo.RegionName = regInfo.ServerURI;
            else
                regInfo.RegionName = externalName;

            MainConsole.Instance.DebugFormat ("[HYPERGRID LINKER]: naming linked region {0}, handle {1}", regInfo.RegionName, handle.ToString ());

            // Get the map image
            regInfo.TerrainImage = GetMapImage (regionID, imageURL);

            // Store the origin's coordinates somewhere

            //TODO:
            //regInfo.RegionSecret = handle.ToString ();

            AddHyperlinkRegion (regInfo, handle);
            MainConsole.Instance.InfoFormat ("[HYPERGRID LINKER]: Successfully linked to region {0} with image {1}", regInfo.RegionName, regInfo.TerrainImage);
            return true;
        }

        public bool TryUnlinkRegion (string mapName)
        {
            MainConsole.Instance.DebugFormat ("[HYPERGRID LINKER]: Request to unlink {0}", mapName);
            GridRegion regInfo = null;

            //TODO:
            List<GridRegion> regions = m_Database.Get(mapName, null, null, null);
            if (regions != null && regions.Count > 0)
            {
                Aurora.Framework.RegionFlags rflags = (Aurora.Framework.RegionFlags)regions[0].Flags;
                if ((rflags & Aurora.Framework.RegionFlags.Hyperlink) != 0)
                {
                    regInfo = new GridRegion ();
                    regInfo.RegionID = regions[0].RegionID;
                    regInfo.ScopeID = m_ScopeID;
                }
            }

            if (regInfo != null)
            {
                RemoveHyperlinkRegion (regInfo.RegionID);
                return true;
            }
            else
            {
                MainConsole.Instance.InfoFormat ("[HYPERGRID LINKER]: Region {0} not found", mapName);
                return false;
            }
        }

        /// <summary>
        /// Cope with this viewer limitation.
        /// </summary>
        /// <param name="regInfo"></param>
        /// <returns></returns>
        public bool Check4096 (ulong realHandle, out uint x, out uint y)
        {
            uint ux = 0, uy = 0;
            Utils.LongToUInts (realHandle, out ux, out uy);
            x = ux / Constants.RegionSize;
            y = uy / Constants.RegionSize;

            const uint limit = (4096 - 1) * Constants.RegionSize;
            uint xmin = ux - limit;
            uint xmax = ux + limit;
            uint ymin = uy - limit;
            uint ymax = uy + limit;
            // World map boundary checks
            if (xmin < 0 || xmin > ux)
                xmin = 0;
            if (xmax > int.MaxValue || xmax < ux)
                xmax = int.MaxValue;
            if (ymin < 0 || ymin > uy)
                ymin = 0;
            if (ymax > int.MaxValue || ymax < uy)
                ymax = int.MaxValue;

            // Check for any regions that are within the possible teleport range to the linked region
            List<GridRegion> regions = m_GridService.GetRegionRange (null, (int)xmin, (int)xmax, (int)ymin, (int)ymax);
            if (regions.Count == 0)
            {
                return false;
            }
            else
            {
                // Check for regions which are not linked regions
                List<GridRegion> hyperlinks = m_Database.Get (Aurora.Framework.RegionFlags.Hyperlink);
                regions.RemoveAll (delegate (GridRegion r)
                {
                    return hyperlinks.Contains(r);
                });
                if (regions.Count == 0)
                    return false;
            }

            return true;
        }

        private void AddHyperlinkRegion (GridRegion regionInfo, ulong regionHandle)
        {
            int flags = (int)Aurora.Framework.RegionFlags.Hyperlink + (int)Aurora.Framework.RegionFlags.NoDirectLogin + (int)Aurora.Framework.RegionFlags.RegionOnline;
            regionInfo.Flags = flags;

            m_Database.Store (regionInfo);
        }

        private void RemoveHyperlinkRegion (UUID regionID)
        {
            m_Database.Delete (regionID);
        }

        public UUID GetMapImage (UUID regionID, string imageURL)
        {
            return m_GatekeeperConnector.GetMapImage (regionID, imageURL, m_MapTileDirectory);
        }
        #endregion


        #region Console Commands

        public void HandleShow (string[] cmd)
        {
            if (cmd.Length != 2)
            {
                MainConsole.Instance.Output ("Syntax: show hyperlinks");
                return;
            }
            List<GridRegion> regions = m_Database.Get (Aurora.Framework.RegionFlags.Hyperlink);
            if (regions == null || regions.Count < 1)
            {
                MainConsole.Instance.Output ("No hyperlinks");
                return;
            }

            MainConsole.Instance.Output ("Region Name");
            MainConsole.Instance.Output ("Location                         Region UUID");
            MainConsole.Instance.Output (new string ('-', 72));
            foreach (GridRegion r in regions)
            {
                MainConsole.Instance.Output (String.Format ("{0}\n{2,-32} {1}\n",
                        r.RegionName, r.RegionID, String.Format ("{0},{1} ({2},{3})", r.RegionLocX, r.RegionLocY,
                            r.RegionLocX / Constants.RegionSize, r.RegionLocY / Constants.RegionSize)));
            }
        }

        public void RunCommand (string[] cmdparams)
        {
            List<string> args = new List<string> (cmdparams);
            if (args.Count < 1)
                return;

            string command = args[0];
            args.RemoveAt (0);

            cmdparams = args.ToArray ();

            RunHGCommand (command, cmdparams);

        }

        private void RunLinkRegionCommand (string[] cmdparams)
        {
            int xloc, yloc;
            string serverURI;
            string remoteName = null;
            xloc = Convert.ToInt32 (cmdparams[0]) * (int)Constants.RegionSize;
            yloc = Convert.ToInt32 (cmdparams[1]) * (int)Constants.RegionSize;
            serverURI = cmdparams[2];
            if (cmdparams.Length > 3)
                remoteName = string.Join (" ", cmdparams, 3, cmdparams.Length - 3);
            string reason = string.Empty;
            GridRegion regInfo;
            if (TryCreateLink (UUID.Zero, xloc, yloc, remoteName, 0, null, serverURI, UUID.Zero, out regInfo, out reason))
                MainConsole.Instance.Output ("Hyperlink established");
            else
                MainConsole.Instance.Output ("Failed to link region: " + reason);
        }

        private void RunHGCommand (string command, string[] cmdparams)
        {
            if (command.Equals ("link-mapping"))
            {
                if (cmdparams.Length == 2)
                {
                    try
                    {
                        m_autoMappingX = Convert.ToUInt32 (cmdparams[0]);
                        m_autoMappingY = Convert.ToUInt32 (cmdparams[1]);
                        m_enableAutoMapping = true;
                    }
                    catch (Exception)
                    {
                        m_autoMappingX = 0;
                        m_autoMappingY = 0;
                        m_enableAutoMapping = false;
                    }
                }
            }
            else if (command.Equals ("link-region"))
            {
                if (cmdparams.Length < 3)
                {
                    if ((cmdparams.Length == 1) || (cmdparams.Length == 2))
                    {
                        LoadXmlLinkFile (cmdparams);
                    }
                    else
                    {
                        LinkRegionCmdUsage ();
                    }
                    return;
                }

                //this should be the prefererred way of setting up hg links now
                if (cmdparams[2].StartsWith ("http"))
                {
                    RunLinkRegionCommand (cmdparams);
                }
                else if (cmdparams[2].Contains (":"))
                {
                    // New format
                    string[] parts = cmdparams[2].Split (':');
                    if (parts.Length > 2)
                    {
                        // Insert remote region name
                        ArrayList parameters = new ArrayList (cmdparams);
                        parameters.Insert (3, parts[2]);
                        cmdparams = (string[])parameters.ToArray (typeof (string));
                    }
                    cmdparams[2] = "http://" + parts[0] + ':' + parts[1];

                    RunLinkRegionCommand (cmdparams);
                }
                else
                {
                    // old format
                    GridRegion regInfo;
                    int xloc, yloc;
                    uint externalPort;
                    string externalHostName;
                    try
                    {
                        xloc = Convert.ToInt32 (cmdparams[0]);
                        yloc = Convert.ToInt32 (cmdparams[1]);
                        externalPort = Convert.ToUInt32 (cmdparams[3]);
                        externalHostName = cmdparams[2];
                        //internalPort = Convert.ToUInt32(cmdparams[4]);
                        //remotingPort = Convert.ToUInt32(cmdparams[5]);
                    }
                    catch (Exception e)
                    {
                        MainConsole.Instance.Output ("[HGrid] Wrong format for link-region command: " + e.Message);
                        LinkRegionCmdUsage ();
                        return;
                    }

                    // Convert cell coordinates given by the user to meters
                    xloc = xloc * (int)Constants.RegionSize;
                    yloc = yloc * (int)Constants.RegionSize;
                    string reason = string.Empty;
                    if (TryCreateLink (UUID.Zero, xloc, yloc, string.Empty, externalPort, externalHostName, UUID.Zero, out regInfo, out reason))
                    {
                        // What is this? The GridRegion instance will be discarded anyway,
                        // which effectively ignores any local name given with the command.
                        //if (cmdparams.Length >= 5)
                        //{
                        //    regInfo.RegionName = "";
                        //    for (int i = 4; i < cmdparams.Length; i++)
                        //        regInfo.RegionName += cmdparams[i] + " ";
                        //}
                    }
                }
                return;
            }
            else if (command.Equals ("unlink-region"))
            {
                if (cmdparams.Length < 1)
                {
                    UnlinkRegionCmdUsage ();
                    return;
                }
                string region = string.Join (" ", cmdparams);
                if (TryUnlinkRegion (region))
                    MainConsole.Instance.Output ("Successfully unlinked " + region);
                else
                    MainConsole.Instance.Output ("Unable to unlink " + region + ", region not found.");
            }
        }

        private void LoadXmlLinkFile (string[] cmdparams)
        {
            //use http://www.hgurl.com/hypergrid.xml for test
            try
            {
                XmlReader r = XmlReader.Create (cmdparams[0]);
                XmlConfigSource cs = new XmlConfigSource (r);
                string[] excludeSections = null;

                if (cmdparams.Length == 2)
                {
                    if (cmdparams[1].ToLower ().StartsWith ("excludelist:"))
                    {
                        string excludeString = cmdparams[1].ToLower ();
                        excludeString = excludeString.Remove (0, 12);
                        char[] splitter = { ';' };

                        excludeSections = excludeString.Split (splitter);
                    }
                }

                for (int i = 0; i < cs.Configs.Count; i++)
                {
                    bool skip = false;
                    if ((excludeSections != null) && (excludeSections.Length > 0))
                    {
                        for (int n = 0; n < excludeSections.Length; n++)
                        {
                            if (excludeSections[n] == cs.Configs[i].Name.ToLower ())
                            {
                                skip = true;
                                break;
                            }
                        }
                    }
                    if (!skip)
                    {
                        ReadLinkFromConfig (cs.Configs[i]);
                    }
                }
            }
            catch (Exception e)
            {
                MainConsole.Instance.Error (e.ToString ());
            }
        }


        private void ReadLinkFromConfig (IConfig config)
        {
            GridRegion regInfo;
            int xloc, yloc;
            uint externalPort;
            string externalHostName;
            uint realXLoc, realYLoc;

            xloc = Convert.ToInt32 (config.GetString ("xloc", "0"));
            yloc = Convert.ToInt32 (config.GetString ("yloc", "0"));
            externalPort = Convert.ToUInt32 (config.GetString ("externalPort", "0"));
            externalHostName = config.GetString ("externalHostName", "");
            realXLoc = Convert.ToUInt32 (config.GetString ("real-xloc", "0"));
            realYLoc = Convert.ToUInt32 (config.GetString ("real-yloc", "0"));

            if (m_enableAutoMapping)
            {
                xloc = (int)((xloc % 100) + m_autoMappingX);
                yloc = (int)((yloc % 100) + m_autoMappingY);
            }

            if (((realXLoc == 0) && (realYLoc == 0)) ||
                (((realXLoc - xloc < 3896) || (xloc - realXLoc < 3896)) &&
                 ((realYLoc - yloc < 3896) || (yloc - realYLoc < 3896))))
            {
                xloc = xloc * (int)Constants.RegionSize;
                yloc = yloc * (int)Constants.RegionSize;
                string reason = string.Empty;
                if (TryCreateLink (UUID.Zero, xloc, yloc, string.Empty, externalPort, externalHostName, UUID.Zero, out regInfo, out reason))
                {
                    regInfo.RegionName = config.GetString ("localName", "");
                }
                else
                    MainConsole.Instance.Output ("Unable to link " + externalHostName + ": " + reason);
            }
        }


        private void LinkRegionCmdUsage ()
        {
            MainConsole.Instance.Output ("Usage: link-region <Xloc> <Yloc> <ServerURI> [<RemoteRegionName>]");
            MainConsole.Instance.Output ("Usage (deprecated): link-region <Xloc> <Yloc> <HostName>:<HttpPort>[:<RemoteRegionName>]");
            MainConsole.Instance.Output ("Usage (deprecated): link-region <Xloc> <Yloc> <HostName> <HttpPort> [<LocalName>]");
            MainConsole.Instance.Output ("Usage: link-region <URI_of_xml> [<exclude>]");
        }

        private void UnlinkRegionCmdUsage ()
        {
            MainConsole.Instance.Output ("Usage: unlink-region <LocalName>");
        }

        #endregion

        public GridRegion GetRegionForGrid (string regionName, string url)
        {
            int xloc = random.Next (0, Int16.MaxValue) * (int)Constants.RegionSize;
            int yloc = random.Next (0, Int16.MaxValue) * (int)Constants.RegionSize;
            string host = "127.0.0.1";
            string portstr;
            uint port = 0;
            string[] parts = url.Split (new char[] { ':' });
            if (parts.Length >= 1)
                host = parts[0];
            if (parts.Length >= 2)
            {
                portstr = parts[1];
                UInt32.TryParse (portstr, out port);
            }
            GridRegion r;
            string reason;
            if (TryCreateLink (UUID.Zero, xloc, yloc, regionName, port, host, UUID.Zero, out r, out reason))
                return r;
            return null;
        }

        public OpenMetaverse.StructuredData.OSDMap GetUrlsForUser (GridRegion region, UUID userID)
        {
            //HG doesn't do this
            return null;
        }
    }
}
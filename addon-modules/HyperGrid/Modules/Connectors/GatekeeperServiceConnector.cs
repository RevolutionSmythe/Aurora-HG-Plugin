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
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using Aurora.Framework;
using Aurora.DataManager;
using Aurora.Simulation.Base;
using OpenMetaverse;
using OpenMetaverse.Imaging;
using OpenMetaverse.StructuredData;
using Nwc.XmlRpc;
using OpenSim.Services.Interfaces;
using OpenSim.Services.Connectors.Simulation;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

namespace Aurora.Addon.HyperGrid
{
    public class GatekeeperServiceConnector : SimulationServiceConnector
    {
        private static UUID m_HGMapImage = new UUID ("00000000-0000-1111-9999-000000000013");

        private IAssetService m_AssetService;

        public GatekeeperServiceConnector ()
            : base ()
        {
        }

        public GatekeeperServiceConnector (IAssetService assService)
        {
            m_AssetService = assService;
        }

        protected override string AgentPath ()
        {
            return "foreignagent/";
        }

        protected override string ObjectPath ()
        {
            return "foreignobject/";
        }

        public bool LinkRegion (GridRegion info, out UUID regionID, out ulong realHandle, out string externalName, out string imageURL, out string reason)
        {
            regionID = UUID.Zero;
            imageURL = string.Empty;
            realHandle = 0;
            externalName = string.Empty;
            reason = string.Empty;

            Hashtable hash = new Hashtable ();
            hash["region_name"] = info.RegionName;

            IList paramList = new ArrayList ();
            paramList.Add (hash);

            XmlRpcRequest request = new XmlRpcRequest ("link_region", paramList);
            if (info.ServerURI == null)
                info.ServerURI = "http://" + info.ServerURI + ":" + info.HttpPort;
            MainConsole.Instance.Debug ("[GATEKEEPER SERVICE CONNECTOR]: Linking to " + info.ServerURI);
            XmlRpcResponse response = null;
            try
            {
                response = request.Send (info.ServerURI, 10000);
            }
            catch (Exception e)
            {
                MainConsole.Instance.Debug ("[GATEKEEPER SERVICE CONNECTOR]: Exception " + e.Message);
                reason = "Error contacting remote server";
                return false;
            }

            if (response.IsFault)
            {
                reason = response.FaultString;
                MainConsole.Instance.ErrorFormat ("[GATEKEEPER SERVICE CONNECTOR]: remote call returned an error: {0}", response.FaultString);
                return false;
            }

            hash = (Hashtable)response.Value;
            //foreach (Object o in hash)
            //    MainConsole.Instance.Debug(">> " + ((DictionaryEntry)o).Key + ":" + ((DictionaryEntry)o).Value);
            try
            {
                bool success = false;
                Boolean.TryParse ((string)hash["result"], out success);
                if (success)
                {
                    UUID.TryParse ((string)hash["uuid"], out regionID);
                    //MainConsole.Instance.Debug(">> HERE, uuid: " + regionID);
                    if ((string)hash["handle"] != null)
                    {
                        realHandle = Convert.ToUInt64 ((string)hash["handle"]);
                        //MainConsole.Instance.Debug(">> HERE, realHandle: " + realHandle);
                    }
                    if (hash["region_image"] != null)
                    {
                        imageURL = (string)hash["region_image"];
                        //MainConsole.Instance.Debug(">> HERE, imageURL: " + imageURL);
                    }
                    if (hash["external_name"] != null)
                    {
                        externalName = (string)hash["external_name"];
                        //MainConsole.Instance.Debug(">> HERE, externalName: " + externalName);
                    }
                }

            }
            catch (Exception e)
            {
                reason = "Error parsing return arguments";
                MainConsole.Instance.Error ("[GATEKEEPER SERVICE CONNECTOR]: Got exception while parsing hyperlink response " + e.StackTrace);
                return false;
            }

            return true;
        }

        public UUID GetMapImage (UUID regionID, string imageURL, string storagePath)
        {
            if (m_AssetService == null)
            {
                MainConsole.Instance.DebugFormat ("[GATEKEEPER SERVICE CONNECTOR]: No AssetService defined. Map tile not retrieved.");
                return m_HGMapImage;
            }

            UUID mapTile = m_HGMapImage;
            string filename = string.Empty;
            Bitmap bitmap = null;
            try
            {
                WebClient c = new WebClient ();
                string name = regionID.ToString ();
                filename = Path.Combine (storagePath, name + ".jpg");
                MainConsole.Instance.DebugFormat ("[GATEKEEPER SERVICE CONNECTOR]: Map image at {0}, cached at {1}", imageURL, filename);
                if (!File.Exists (filename))
                {
                    MainConsole.Instance.DebugFormat ("[GATEKEEPER SERVICE CONNECTOR]: downloading...");
                    c.DownloadFile (imageURL, filename);
                }
                else
                    MainConsole.Instance.DebugFormat ("[GATEKEEPER SERVICE CONNECTOR]: using cached image");

                bitmap = new Bitmap (filename);
                //MainConsole.Instance.Debug("Size: " + m.PhysicalDimension.Height + "-" + m.PhysicalDimension.Width);
                byte[] imageData = OpenJPEG.EncodeFromImage (bitmap, true);
                AssetBase ass = new AssetBase (UUID.Random (), "region " + name, AssetType.Texture, regionID);

                // !!! for now
                //info.RegionSettings.TerrainImageID = ass.FullID;

                ass.Data = imageData;

                mapTile = ass.ID;

                // finally
                mapTile = m_AssetService.Store(ass);

            }
            catch // LEGIT: Catching problems caused by OpenJPEG p/invoke
            {
                MainConsole.Instance.Info ("[GATEKEEPER SERVICE CONNECTOR]: Failed getting/storing map image, because it is probably already in the cache");
            }
            return mapTile;
        }

        public GridRegion GetHyperlinkRegion (GridRegion gatekeeper, UUID regionID)
        {
            Hashtable hash = new Hashtable ();
            hash["region_uuid"] = regionID.ToString ();

            IList paramList = new ArrayList ();
            paramList.Add (hash);

            XmlRpcRequest request = new XmlRpcRequest ("get_region", paramList);
            MainConsole.Instance.Debug ("[GATEKEEPER SERVICE CONNECTOR]: contacting " + gatekeeper.ServerURI);
            XmlRpcResponse response = null;
            try
            {
                response = request.Send (gatekeeper.ServerURI, 10000);
            }
            catch (Exception e)
            {
                MainConsole.Instance.Debug ("[GATEKEEPER SERVICE CONNECTOR]: Exception " + e.Message);
                return null;
            }

            if (response.IsFault)
            {
                MainConsole.Instance.ErrorFormat ("[GATEKEEPER SERVICE CONNECTOR]: remote call returned an error: {0}", response.FaultString);
                return null;
            }

            hash = (Hashtable)response.Value;
            //foreach (Object o in hash)
            //    MainConsole.Instance.Debug(">> " + ((DictionaryEntry)o).Key + ":" + ((DictionaryEntry)o).Value);
            try
            {
                bool success = false;
                Boolean.TryParse ((string)hash["result"], out success);
                if (success)
                {
                    GridRegion region = new GridRegion ();

                    UUID.TryParse ((string)hash["uuid"], out region.RegionID);
                    //MainConsole.Instance.Debug(">> HERE, uuid: " + region.RegionID);
                    int n = 0;
                    if (hash["x"] != null)
                    {
                        Int32.TryParse ((string)hash["x"], out n);
                        region.RegionLocX = n;
                        //MainConsole.Instance.Debug(">> HERE, x: " + region.RegionLocX);
                    }
                    if (hash["y"] != null)
                    {
                        Int32.TryParse ((string)hash["y"], out n);
                        region.RegionLocY = n;
                        //MainConsole.Instance.Debug(">> HERE, y: " + region.RegionLocY);
                    }
                    if (hash["region_name"] != null)
                    {
                        region.RegionName = (string)hash["region_name"];
                        //MainConsole.Instance.Debug(">> HERE, region_name: " + region.RegionName);
                    }
                    if (hash["hostname"] != null)
                    {
                        region.ExternalHostName = (string)hash["hostname"];
                        //MainConsole.Instance.Debug(">> HERE, hostname: " + region.ExternalHostName);
                    }
                    if (hash["http_port"] != null)
                    {
                        uint p = 0;
                        UInt32.TryParse ((string)hash["http_port"], out p);
                        region.HttpPort = p;
                        //MainConsole.Instance.Debug(">> HERE, http_port: " + region.HttpPort);
                    }
                    if (hash["internal_port"] != null)
                    {
                        int p = 0;
                        Int32.TryParse ((string)hash["internal_port"], out p);
                        region.InternalEndPoint = new IPEndPoint (IPAddress.Parse ("0.0.0.0"), p);
                        //MainConsole.Instance.Debug(">> HERE, internal_port: " + region.InternalEndPoint);
                    }

                    if (hash["server_uri"] != null)
                    {
                        region.ServerURI = (string)hash["server_uri"];
                        //MainConsole.Instance.Debug(">> HERE, server_uri: " + region.ServerURI);
                    }

                    // Successful return
                    return region;
                }

            }
            catch (Exception e)
            {
                MainConsole.Instance.Error ("[GATEKEEPER SERVICE CONNECTOR]: Got exception while parsing hyperlink response " + e.StackTrace);
                return null;
            }

            return null;
        }

        public bool CreateAgent (GridRegion destination, AgentCircuitData aCircuit, uint flags, out string myipaddress, out string reason)
        {
            // MainConsole.Instance.DebugFormat("[GATEKEEPER SERVICE CONNECTOR]: CreateAgent start");

            myipaddress = String.Empty;
            reason = String.Empty;

            if (destination == null)
            {
                MainConsole.Instance.Debug ("[GATEKEEPER SERVICE CONNECTOR]: Given destination is null");
                return false;
            }

            string uri = (destination.ServerURI.EndsWith ("/") ? destination.ServerURI : (destination.ServerURI + "/"))
                + AgentPath () + aCircuit.AgentID + "/";

            try
            {
                OSDMap args = aCircuit.PackAgentCircuitData ();

                args["destination_x"] = OSD.FromString (destination.RegionLocX.ToString ());
                args["destination_y"] = OSD.FromString (destination.RegionLocY.ToString ());
                args["destination_name"] = OSD.FromString (destination.RegionName);
                args["destination_uuid"] = OSD.FromString (destination.RegionID.ToString ());
                args["teleport_flags"] = OSD.FromString (flags.ToString ());

                string r = WebUtils.PostToService (uri, args);
                OSDMap unpacked = OSDParser.DeserializeJson(r) as OSDMap;
                if (unpacked != null)
                {
                    reason = unpacked["reason"].AsString();
                    myipaddress = unpacked["your_ip"].AsString();
                    return unpacked["success"].AsBoolean();
                }

                reason = unpacked["Message"] != null ? unpacked["Message"].AsString() : "error";
                return false;
            }
            catch (Exception e)
            {
                MainConsole.Instance.Warn ("[REMOTE SIMULATION CONNECTOR]: CreateAgent failed with exception: " + e.ToString ());
                reason = e.Message;
            }

            return false;
        }
    }
}

﻿/*
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
using System.Net;
using System.Reflection;

using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

using Nwc.XmlRpc;
using OpenMetaverse;
using Aurora.Framework;

namespace Aurora.Addon.HyperGrid
{
    public class HypergridHandlers
    {
        private IGatekeeperService m_GatekeeperService;

        public HypergridHandlers (IGatekeeperService gatekeeper)
        {
            m_GatekeeperService = gatekeeper;
            MainConsole.Instance.DebugFormat ("[HYPERGRID HANDLERS]: Active");
        }

        /// <summary>
        /// Someone wants to link to us
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public XmlRpcResponse LinkRegionRequest (XmlRpcRequest request, IPEndPoint remoteClient)
        {
            Hashtable requestData = (Hashtable)request.Params[0];
            //string host = (string)requestData["host"];
            //string portstr = (string)requestData["port"];
            string name = (string)requestData["region_name"];
            if (name == null)
                name = string.Empty;

            UUID regionID = UUID.Zero;
            string externalName = string.Empty;
            string imageURL = string.Empty;
            ulong regionHandle = 0;
            string reason = string.Empty;

            bool success = m_GatekeeperService.LinkRegion (name, out regionID, out regionHandle, out externalName, out imageURL, out reason);

            Hashtable hash = new Hashtable ();
            hash["result"] = success.ToString ();
            hash["uuid"] = regionID.ToString ();
            hash["handle"] = regionHandle.ToString ();
            hash["region_image"] = imageURL;
            hash["external_name"] = externalName;

            XmlRpcResponse response = new XmlRpcResponse ();
            response.Value = hash;
            return response;
        }

        public XmlRpcResponse GetRegion (XmlRpcRequest request, IPEndPoint remoteClient)
        {
            Hashtable requestData = (Hashtable)request.Params[0];
            //string host = (string)requestData["host"];
            //string portstr = (string)requestData["port"];
            string regionID_str = (string)requestData["region_uuid"];
            UUID regionID = UUID.Zero;
            UUID.TryParse (regionID_str, out regionID);

            GridRegion regInfo = m_GatekeeperService.GetHyperlinkRegion (regionID);

            Hashtable hash = new Hashtable ();
            if (regInfo == null)
                hash["result"] = "false";
            else
            {
                hash["result"] = "true";
                hash["uuid"] = regInfo.RegionID.ToString ();
                hash["x"] = regInfo.RegionLocX.ToString ();
                hash["y"] = regInfo.RegionLocY.ToString ();
                hash["region_name"] = regInfo.RegionName;
                hash["hostname"] = regInfo.ExternalHostName;
                hash["http_port"] = regInfo.HttpPort.ToString ();
                hash["internal_port"] = regInfo.InternalEndPoint.Port.ToString ();
            }
            XmlRpcResponse response = new XmlRpcResponse ();
            response.Value = hash;
            return response;
        }
    }
}

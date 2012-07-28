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
using System.Net;
using System.Reflection;

using Nini.Config;
using Aurora.Framework;
using OpenSim.Services.Interfaces;
using Aurora.Framework.Servers.HttpServer;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using Aurora.Simulation.Base;

using Nwc.XmlRpc;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace Aurora.Addon.HyperGrid
{
    public class InstantMessageServerConnector : IService
    {
        private IRegistryCore m_registry;

        protected virtual XmlRpcResponse ProcessInstantMessage (XmlRpcRequest request, IPEndPoint remoteClient)
        {
            bool successful = false;

            try
            {
                // various rational defaults
                UUID fromAgentID = UUID.Zero;
                UUID toAgentID = UUID.Zero;
                UUID imSessionID = UUID.Zero;
                uint timestamp = 0;
                string fromAgentName = "";
                string message = "";
                byte dialog = (byte)0;
                bool fromGroup = false;
                byte offline = (byte)0;
                uint ParentEstateID = 0;
                Vector3 Position = Vector3.Zero;
                UUID RegionID = UUID.Zero;
                byte[] binaryBucket = new byte[0];

                float pos_x = 0;
                float pos_y = 0;
                float pos_z = 0;
                //MainConsole.Instance.Info("Processing IM");


                Hashtable requestData = (Hashtable)request.Params[0];
                // Check if it's got all the data
                if (requestData.ContainsKey ("from_agent_id")
                        && requestData.ContainsKey ("to_agent_id") && requestData.ContainsKey ("im_session_id")
                        && requestData.ContainsKey ("timestamp") && requestData.ContainsKey ("from_agent_name")
                        && requestData.ContainsKey ("message") && requestData.ContainsKey ("dialog")
                        && requestData.ContainsKey ("from_group")
                        && requestData.ContainsKey ("offline") && requestData.ContainsKey ("parent_estate_id")
                        && requestData.ContainsKey ("position_x") && requestData.ContainsKey ("position_y")
                        && requestData.ContainsKey ("position_z") && requestData.ContainsKey ("region_id")
                        && requestData.ContainsKey ("binary_bucket"))
                {
                    // Do the easy way of validating the UUIDs
                    UUID.TryParse ((string)requestData["from_agent_id"], out fromAgentID);
                    UUID.TryParse ((string)requestData["to_agent_id"], out toAgentID);
                    UUID.TryParse ((string)requestData["im_session_id"], out imSessionID);
                    UUID.TryParse ((string)requestData["region_id"], out RegionID);
                    try
                    {
                        timestamp = (uint)Convert.ToInt32 ((string)requestData["timestamp"]);
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (FormatException)
                    {
                    }
                    catch (OverflowException)
                    {
                    }

                    fromAgentName = (string)requestData["from_agent_name"];
                    message = (string)requestData["message"];
                    if (message == null)
                        message = string.Empty;

                    // Bytes don't transfer well over XMLRPC, so, we Base64 Encode them.
                    string requestData1 = (string)requestData["dialog"];
                    if (string.IsNullOrEmpty (requestData1))
                    {
                        dialog = 0;
                    }
                    else
                    {
                        byte[] dialogdata = Convert.FromBase64String (requestData1);
                        dialog = dialogdata[0];
                    }

                    if ((string)requestData["from_group"] == "TRUE")
                        fromGroup = true;

                    string requestData2 = (string)requestData["offline"];
                    if (String.IsNullOrEmpty (requestData2))
                    {
                        offline = 0;
                    }
                    else
                    {
                        byte[] offlinedata = Convert.FromBase64String (requestData2);
                        offline = offlinedata[0];
                    }

                    try
                    {
                        ParentEstateID = (uint)Convert.ToInt32 ((string)requestData["parent_estate_id"]);
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (FormatException)
                    {
                    }
                    catch (OverflowException)
                    {
                    }

                    float.TryParse ((string)requestData["position_x"], out pos_x);
                    float.TryParse ((string)requestData["position_y"], out pos_y);
                    float.TryParse ((string)requestData["position_z"], out pos_z);

                    Position = new Vector3 (pos_x, pos_y, pos_z);

                    string requestData3 = (string)requestData["binary_bucket"];
                    if (string.IsNullOrEmpty (requestData3))
                    {
                        binaryBucket = new byte[0];
                    }
                    else
                    {
                        binaryBucket = Convert.FromBase64String (requestData3);
                    }

                    // Create a New GridInstantMessageObject the the data
                    GridInstantMessage gim = new GridInstantMessage ();
                    gim.fromAgentID = fromAgentID;
                    gim.fromAgentName = fromAgentName;
                    gim.fromGroup = fromGroup;
                    gim.imSessionID = imSessionID;
                    gim.RegionID = RegionID;
                    gim.timestamp = timestamp;
                    gim.toAgentID = toAgentID;
                    gim.message = message;
                    gim.dialog = dialog;
                    gim.offline = offline;
                    gim.ParentEstateID = ParentEstateID;
                    gim.Position = Position;
                    gim.binaryBucket = binaryBucket;

                    successful = SendIM (gim);
                }
            }
            catch (Exception e)
            {
                MainConsole.Instance.Error ("[INSTANT MESSAGE]: Caught unexpected exception:", e);
                successful = false;
            }

            //Send response back to region calling if it was successful
            // calling region uses this to know when to look up a user's location again.
            XmlRpcResponse resp = new XmlRpcResponse ();
            Hashtable respdata = new Hashtable ();
            if (successful)
                respdata["success"] = "TRUE";
            else
                respdata["success"] = "FALSE";
            resp.Value = respdata;

            return resp;
        }

        /// <summary>
        /// Param UUID - AgentID
        /// Param string - HTTP path to the region the user is in, blank if not found
        /// </summary>
        public Dictionary<UUID, string> IMUsersCache = new Dictionary<UUID, string> ();

        private bool SendIM (GridInstantMessage gim)
        {
            string HTTPPath = "";
            List<string> AgentLocations = m_registry.RequestModuleInterface<IAgentInfoService> ().GetAgentsLocations (gim.fromAgentID.ToString(), new List<string>() { gim.toAgentID.ToString () });
            if (AgentLocations.Count > 0)
            {
                //No agents, so this user is offline
                if (AgentLocations[0] == "NotOnline")
                {
                    lock (IMUsersCache)
                    {
                        //Remove them so we keep testing against the db
                        IMUsersCache.Remove (gim.toAgentID);
                    }
                    return false;
                }
                else //Found the agent, use this location
                    HTTPPath = AgentLocations[0];
            }

            //We found the agent's location, now ask them about the user
            if (HTTPPath != "")
            {
                Hashtable msgdata = ConvertGridInstantMessageToXMLRPC (gim);
                if (!doIMSending (HTTPPath, msgdata))
                {
                    //It failed, stop now
                    lock (IMUsersCache)
                    {
                        //Remove them so we keep testing against the db
                        IMUsersCache.Remove (gim.toAgentID);
                    }
                    MainConsole.Instance.Info ("[GRID INSTANT MESSAGE]: Unable to deliver an instant message as the region could not be found");
                    return false;
                }
                else
                {
                    //Add to the cache
                    if (!IMUsersCache.ContainsKey (gim.toAgentID))
                        IMUsersCache.Add (gim.toAgentID, HTTPPath);
                    return true;
                }
            }
            else
            {
                //Couldn't find them, stop for now
                lock (IMUsersCache)
                {
                    //Remove them so we keep testing against the db
                    IMUsersCache.Remove (gim.toAgentID);
                }
                return false;
            }
        }

        /// <summary>
        /// This actually does the XMLRPC Request
        /// </summary>
        /// <param name="reginfo">RegionInfo we pull the data out of to send the request to</param>
        /// <param name="xmlrpcdata">The Instant Message data Hashtable</param>
        /// <returns>Bool if the message was successfully delivered at the other side.</returns>
        protected virtual bool doIMSending (string httpInfo, Hashtable xmlrpcdata)
        {
            ArrayList SendParams = new ArrayList ();
            SendParams.Add (xmlrpcdata);
            XmlRpcRequest GridReq = new XmlRpcRequest ("grid_instant_message", SendParams);
            try
            {
                XmlRpcResponse GridResp = GridReq.Send (httpInfo, 7000);

                Hashtable responseData = (Hashtable)GridResp.Value;

                if (responseData.ContainsKey ("success"))
                {
                    if ((string)responseData["success"] == "TRUE")
                        return true;
                    else
                        return false;
                }
                else
                    return false;
            }
            catch (WebException e)
            {
                MainConsole.Instance.ErrorFormat ("[GRID INSTANT MESSAGE]: Error sending message to " + httpInfo, e.Message);
            }

            return false;
        }

        /// <summary>
        /// Takes a GridInstantMessage and converts it into a Hashtable for XMLRPC
        /// </summary>
        /// <param name="msg">The GridInstantMessage object</param>
        /// <returns>Hashtable containing the XMLRPC request</returns>
        protected virtual Hashtable ConvertGridInstantMessageToXMLRPC (GridInstantMessage msg)
        {
            Hashtable gim = new Hashtable ();
            gim["message"] = OSDParser.SerializeJsonString (msg.ToOSD ());
            return gim;
        }

        public void Initialize (IConfigSource config, IRegistryCore registry)
        {
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            IConfig imConfig = config.Configs["HyperGridIM"];
            uint port = 8007;
            bool enabled = false;
            if (imConfig != null)
            {
                enabled = imConfig.GetBoolean ("Enabled", enabled);
                port = imConfig.GetUInt ("Port", port);
            }
            if (!enabled)
                return;

            //Add the external handler
            m_registry = registry;
            ISimulationBase simBase = m_registry.RequestModuleInterface<ISimulationBase> ();
            IHttpServer server = simBase.GetHttpServer (port);
            GetHandlers.IM_PORT = server.Port;
            server.AddXmlRPCHandler ("grid_instant_message", ProcessInstantMessage, false);
        }

        public void Start (IConfigSource config, IRegistryCore registry)
        {
        }

        public void FinishedStartup ()
        {
        }
    }
}

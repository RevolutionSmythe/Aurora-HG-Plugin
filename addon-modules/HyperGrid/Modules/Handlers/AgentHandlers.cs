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
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Net;
using System.Text;
using OpenSim.Services.Interfaces;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using Aurora.Framework;
using Aurora.Framework.Servers.HttpServer;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Nini.Config;
using Aurora.DataManager;
using Aurora.Simulation.Base;

namespace Aurora.Addon.HyperGrid
{
    public class GatekeeperAgentHandler : BaseRequestHandler
    {
        private ISimulationService m_SimulationService;
        private IGatekeeperService m_GatekeeperService;
        protected bool m_Proxy = false;

        public GatekeeperAgentHandler (IGatekeeperService gatekeeper, ISimulationService service, bool proxy) :
            base ("POST", "/foreignagent")
        {
            m_SimulationService = service;
            m_GatekeeperService = gatekeeper;
            m_Proxy = proxy;
        }

        public override byte[] Handle (string path, Stream request,
                OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            MainConsole.Instance.DebugFormat ("[SIMULATION]: Stream handler called");

            Hashtable keysvals = new Hashtable ();
            Hashtable headervals = new Hashtable ();

            string[] querystringkeys = httpRequest.QueryString.AllKeys;
            string[] rHeaders = httpRequest.Headers.AllKeys;

            keysvals.Add ("uri", httpRequest.RawUrl);
            keysvals.Add ("content-type", httpRequest.ContentType);
            keysvals.Add ("http-method", httpRequest.HttpMethod);

            foreach (string queryname in querystringkeys)
                keysvals.Add (queryname, httpRequest.QueryString[queryname]);

            foreach (string headername in rHeaders)
                headervals[headername] = httpRequest.Headers[headername];

            keysvals.Add ("headers", headervals);
            keysvals.Add ("querystringkeys", querystringkeys);

            Stream inputStream;
            if (httpRequest.ContentType == "application/x-gzip")
                inputStream = new GZipStream (request, CompressionMode.Decompress);
            else
                inputStream = request;

            Encoding encoding = Encoding.UTF8;
            StreamReader reader = new StreamReader (inputStream, encoding);

            string requestBody = reader.ReadToEnd ();
            reader.Close ();
            keysvals.Add ("body", requestBody);

            httpResponse.StatusCode = 200;
            httpResponse.ContentType = "text/html";
            httpResponse.KeepAlive = false;

            Hashtable responsedata = new Hashtable ();

            UUID agentID;
            UUID regionID;
            string action;

            if (!WebUtils.GetParams ((string)keysvals["uri"], out agentID, out regionID, out action))
            {
                MainConsole.Instance.InfoFormat ("[AGENT HANDLER]: Invalid parameters for agent message {0}", keysvals["uri"]);

                httpResponse.StatusCode = 404;

                return encoding.GetBytes ("false");
            }

            DoAgentPost (keysvals, responsedata, agentID);

            httpResponse.StatusCode = (int)responsedata["int_response_code"];
            return encoding.GetBytes ((string)responsedata["str_response_string"]);
        }

        protected void DoAgentPost (Hashtable request, Hashtable responsedata, UUID id)
        {
            OSDMap args = WebUtils.GetOSDMap ((string)request["body"]);
            if (args == null)
            {
                responsedata["int_response_code"] = HttpStatusCode.BadRequest;
                responsedata["str_response_string"] = "Bad request";
                return;
            }

            // retrieve the input arguments
            int x = 0, y = 0;
            UUID uuid = UUID.Zero;
            string regionname = string.Empty;
            uint teleportFlags = 0;
            if (args.ContainsKey ("destination_x") && args["destination_x"] != null)
                Int32.TryParse (args["destination_x"].AsString (), out x);
            else
                MainConsole.Instance.WarnFormat ("  -- request didn't have destination_x");
            if (args.ContainsKey ("destination_y") && args["destination_y"] != null)
                Int32.TryParse (args["destination_y"].AsString (), out y);
            else
                MainConsole.Instance.WarnFormat ("  -- request didn't have destination_y");
            if (args.ContainsKey ("destination_uuid") && args["destination_uuid"] != null)
                UUID.TryParse (args["destination_uuid"].AsString (), out uuid);
            if (args.ContainsKey ("destination_name") && args["destination_name"] != null)
                regionname = args["destination_name"].ToString ();
            if (args.ContainsKey ("teleport_flags") && args["teleport_flags"] != null)
                teleportFlags = args["teleport_flags"].AsUInteger ();

            GridRegion destination = new GridRegion ();
            destination.RegionID = uuid;
            destination.RegionLocX = x;
            destination.RegionLocY = y;
            destination.RegionName = regionname;

            AgentCircuitData aCircuit = new AgentCircuitData ();
            try
            {
                aCircuit.UnpackAgentCircuitData (args);
            }
            catch (Exception ex)
            {
                MainConsole.Instance.InfoFormat ("[AGENT HANDLER]: exception on unpacking ChildCreate message {0}", ex.Message);
                responsedata["int_response_code"] = HttpStatusCode.BadRequest;
                responsedata["str_response_string"] = "Bad request";
                return;
            }

            OSDMap resp = new OSDMap (2);
            string reason = String.Empty;

            // This is the meaning of POST agent
            //m_regionClient.AdjustUserInformation(aCircuit);
            //bool result = m_SimulationService.CreateAgent(destination, aCircuit, teleportFlags, out reason);
            bool result = CreateAgent (destination, aCircuit, teleportFlags, out reason);

            resp["reason"] = OSD.FromString (reason);
            resp["success"] = OSD.FromBoolean (result);
            // Let's also send out the IP address of the caller back to the caller (HG 1.5)
            resp["your_ip"] = OSD.FromString (GetCallerIP (request));

            // TODO: add reason if not String.Empty?
            responsedata["int_response_code"] = HttpStatusCode.OK;
            responsedata["str_response_string"] = OSDParser.SerializeJsonString (resp);
        }

        private string GetCallerIP (Hashtable request)
        {
            if (!m_Proxy)
                return NetworkUtils.GetCallerIP(request);

            // We're behind a proxy
            Hashtable headers = (Hashtable)request["headers"];

            //// DEBUG
            //foreach (object o in headers.Keys)
            //    MainConsole.Instance.DebugFormat("XXX {0} = {1}", o.ToString(), (headers[o] == null? "null" : headers[o].ToString()));

            string xff = "X-Forwarded-For";
            if (headers.ContainsKey (xff.ToLower ()))
                xff = xff.ToLower ();

            if (!headers.ContainsKey (xff) || headers[xff] == null)
            {
                MainConsole.Instance.WarnFormat ("[AGENT HANDLER]: No XFF header");
                return NetworkUtils.GetCallerIP(request);
            }

            MainConsole.Instance.DebugFormat ("[AGENT HANDLER]: XFF is {0}", headers[xff]);

            IPEndPoint ep = NetworkUtils.GetClientIPFromXFF((string)headers[xff]);
            if (ep != null)
                return ep.Address.ToString ();

            // Oops
            return NetworkUtils.GetCallerIP(request);
        }

        // subclasses can override this
        protected bool CreateAgent (GridRegion destination, AgentCircuitData aCircuit, uint teleportFlags, out string reason)
        {
            return m_GatekeeperService.LoginAgent (aCircuit, destination, out reason);
        }
    }
}

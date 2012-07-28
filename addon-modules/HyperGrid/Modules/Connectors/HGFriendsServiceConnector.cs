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
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Nini.Config;
using Aurora.Framework;
using Aurora.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using FriendInfo = OpenSim.Services.Interfaces.FriendInfo;
using OpenSim.Services.Robust;
using Aurora.DataManager;
using Aurora.Simulation.Base;
using OpenMetaverse;

namespace Aurora.Addon.HyperGrid
{
    public class HGFriendsServicesConnector
    {
        private string m_ServerURI = String.Empty;
        private string m_ServiceKey = String.Empty;
        private UUID m_SessionID;

        public HGFriendsServicesConnector ()
        {
        }

        public HGFriendsServicesConnector (string serverURI)
        {
            m_ServerURI = serverURI.TrimEnd ('/');
        }

        public HGFriendsServicesConnector (string serverURI, UUID sessionID, string serviceKey)
        {
            m_ServerURI = serverURI.TrimEnd ('/');
            m_ServiceKey = serviceKey;
            m_SessionID = sessionID;
        }

        #region IFriendsService

        public uint GetFriendPerms (UUID PrincipalID, UUID friendID)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object> ();

            sendData["PRINCIPALID"] = PrincipalID.ToString ();
            sendData["FRIENDID"] = friendID.ToString ();
            sendData["METHOD"] = "getfriendperms";
            sendData["KEY"] = m_ServiceKey;
            sendData["SESSIONID"] = m_SessionID.ToString ();

            string reqString = WebUtils.BuildQueryString (sendData);

            try
            {
                string reply = SynchronousRestFormsRequester.MakeRequest ("POST",
                        m_ServerURI + "/hgfriends",
                        reqString);
                if (reply != string.Empty)
                {
                    Dictionary<string, object> replyData = WebUtils.ParseXmlResponse (reply);

                    if ((replyData != null) && replyData.ContainsKey ("Value") && (replyData["Value"] != null))
                    {
                        uint perms = 0;
                        uint.TryParse (replyData["Value"].ToString (), out perms);
                        return perms;
                    }
                    else
                        MainConsole.Instance.DebugFormat ("[HGFRIENDS CONNECTOR]: GetFriendPerms {0} received null response",
                            PrincipalID);

                }
            }
            catch (Exception e)
            {
                MainConsole.Instance.DebugFormat ("[HGFRIENDS CONNECTOR]: Exception when contacting friends server: {0}", e.Message);
            }

            return 0;

        }

        public bool NewFriendship (UUID PrincipalID, string Friend)
        {
            FriendInfo finfo = new FriendInfo ();
            finfo.PrincipalID = PrincipalID;
            finfo.Friend = Friend;

            Dictionary<string, object> sendData = finfo.ToKVP ();

            sendData["METHOD"] = "newfriendship";
            sendData["KEY"] = m_ServiceKey;
            sendData["SESSIONID"] = m_SessionID.ToString ();

            string reply = string.Empty;
            try
            {
                reply = SynchronousRestFormsRequester.MakeRequest ("POST",
                        m_ServerURI + "/hgfriends",
                        WebUtils.BuildQueryString (sendData));
            }
            catch (Exception e)
            {
                MainConsole.Instance.DebugFormat ("[HGFRIENDS CONNECTOR]: Exception when contacting friends server: {0}", e.Message);
                return false;
            }

            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = WebUtils.ParseXmlResponse (reply);

                if ((replyData != null) && replyData.ContainsKey ("Result") && (replyData["Result"] != null))
                {
                    bool success = false;
                    Boolean.TryParse (replyData["Result"].ToString (), out success);
                    return success;
                }
                else
                    MainConsole.Instance.DebugFormat ("[HGFRIENDS CONNECTOR]: StoreFriend {0} {1} received null response",
                        PrincipalID, Friend);
            }
            else
                MainConsole.Instance.DebugFormat ("[HGFRIENDS CONNECTOR]: StoreFriend received null reply");

            return false;

        }

        public bool DeleteFriendship (UUID PrincipalID, UUID Friend, string secret)
        {
            FriendInfo finfo = new FriendInfo ();
            finfo.PrincipalID = PrincipalID;
            finfo.Friend = Friend.ToString ();

            Dictionary<string, object> sendData = finfo.ToKVP();

            sendData["METHOD"] = "deletefriendship";
            sendData["SECRET"] = secret;

            string reply = string.Empty;
            try
            {
                reply = SynchronousRestFormsRequester.MakeRequest ("POST",
                        m_ServerURI + "/hgfriends",
                        WebUtils.BuildQueryString (sendData));
            }
            catch (Exception e)
            {
                MainConsole.Instance.DebugFormat ("[HGFRIENDS CONNECTOR]: Exception when contacting friends server: {0}", e.Message);
                return false;
            }

            if (reply != string.Empty)
            {
                Dictionary<string, object> replyData = WebUtils.ParseXmlResponse (reply);

                if ((replyData != null) && replyData.ContainsKey ("Result") && (replyData["Result"] != null))
                {
                    bool success = false;
                    Boolean.TryParse (replyData["Result"].ToString (), out success);
                    return success;
                }
                else
                    MainConsole.Instance.DebugFormat ("[HGFRIENDS CONNECTOR]: Delete {0} {1} received null response",
                        PrincipalID, Friend);
            }
            else
                MainConsole.Instance.DebugFormat ("[HGFRIENDS CONNECTOR]: DeleteFriend received null reply");

            return false;

        }

        #endregion
    }
}
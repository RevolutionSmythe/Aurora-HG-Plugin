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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Xml;
using OpenMetaverse;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using Aurora.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;

namespace Aurora.Addon.HyperGrid
{
    public class RobustOpenProfileModule : IAuroraDataPlugin, IRemoteProfileConnector
    {
        private IRegistryCore m_registry;
        private string m_ProfileServer = "";

        public void Init(string remoteURL, IRegistryCore registry)
        {
            m_registry = registry;
            m_ProfileServer = remoteURL;
        }

        //
        // Make external XMLRPC request
        //
        private Hashtable GenericXMLRPCRequest(Hashtable ReqParams, string method)
        {
            ArrayList SendParams = new ArrayList();
            SendParams.Add(ReqParams);

            // Send Request
            XmlRpcResponse Resp;
            try
            {
                XmlRpcRequest Req = new XmlRpcRequest(method, SendParams);
                Resp = Req.Send(m_ProfileServer, 30000);
            }
            catch (WebException ex)
            {
                MainConsole.Instance.ErrorFormat("[PROFILE]: Unable to connect to Profile " +
                        "Server {0}.  Exception {1}", m_ProfileServer, ex);

                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to fetch profile data at this time. ";
                ErrorHash["errorURI"] = "";

                return ErrorHash;
            }
            catch (SocketException ex)
            {
                MainConsole.Instance.ErrorFormat(
                        "[PROFILE]: Unable to connect to Profile Server {0}. Method {1}, params {2}. " +
                        "Exception {3}", m_ProfileServer, method, ReqParams, ex);

                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to fetch profile data at this time. ";
                ErrorHash["errorURI"] = "";

                return ErrorHash;
            }
            catch (XmlException ex)
            {
                MainConsole.Instance.ErrorFormat(
                        "[PROFILE]: Unable to connect to Profile Server {0}. Method {1}, params {2}. " +
                        "Exception {3}", m_ProfileServer, method, ReqParams.ToString(), ex);
                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to fetch profile data at this time. ";
                ErrorHash["errorURI"] = "";

                return ErrorHash;
            }
            if (Resp.IsFault)
            {
                Hashtable ErrorHash = new Hashtable();
                ErrorHash["success"] = false;
                ErrorHash["errorMessage"] = "Unable to fetch profile data at this time. ";
                ErrorHash["errorURI"] = "";
                return ErrorHash;
            }
            Hashtable RespData = (Hashtable)Resp.Value;

            return RespData;
        }

        // Profile data like the WebURL
        private Hashtable GetProfileData(UUID userID)
        {
            Hashtable ReqHash = new Hashtable();

            ReqHash["avatar_id"] = userID.ToString();

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "avatar_properties_request");

            ArrayList dataArray = (ArrayList)result["data"];

            if (dataArray != null && dataArray[0] != null)
            {
                Hashtable d = (Hashtable)dataArray[0];
                return d;
            }
            return result;
        }

        public IUserProfileInfo GetUserProfile(UUID agentID)
        {
            int created = 0;
            uint flags = 0x00;
            IUserAccountService accountService = m_registry.RequestModuleInterface<IUserAccountService>();
            var account = accountService.GetUserAccount(null, agentID);
            if (null != account)
                created = account.Created;
            else
            {
                Dictionary<string, object> userInfo;
                if (GetUserProfileData(agentID, out userInfo))
                    created = (int)userInfo["user_created"];
            }

            Hashtable profileData = GetProfileData(agentID);
            string profileUrl = string.Empty;
            string aboutText = String.Empty;
            string firstLifeAboutText = String.Empty;
            UUID image = UUID.Zero;
            UUID firstLifeImage = UUID.Zero;
            UUID partner = UUID.Zero;
            uint wantMask = 0;
            string wantText = String.Empty;
            uint skillsMask = 0;
            string skillsText = String.Empty;
            string languages = String.Empty;

            if (profileData["ProfileUrl"] != null)
                profileUrl = profileData["ProfileUrl"].ToString();
            if (profileData["AboutText"] != null)
                aboutText = profileData["AboutText"].ToString();
            if (profileData["FirstLifeAboutText"] != null)
                firstLifeAboutText = profileData["FirstLifeAboutText"].ToString();
            if (profileData["Image"] != null)
                image = new UUID(profileData["Image"].ToString());
            if (profileData["FirstLifeImage"] != null)
                firstLifeImage = new UUID(profileData["FirstLifeImage"].ToString());
            if (profileData["Partner"] != null)
                partner = new UUID(profileData["Partner"].ToString());

            //Viewer expects interest data when it asks for properties.
            if (profileData["wantmask"] != null)
                wantMask = Convert.ToUInt32(profileData["wantmask"].ToString());
            if (profileData["wanttext"] != null)
                wantText = profileData["wanttext"].ToString();

            if (profileData["skillsmask"] != null)
                skillsMask = Convert.ToUInt32(profileData["skillsmask"].ToString());
            if (profileData["skillstext"] != null)
                skillsText = profileData["skillstext"].ToString();

            if (profileData["languages"] != null)
                languages = profileData["languages"].ToString();

            return new IUserProfileInfo
            {
                AboutText = aboutText,
                Created = created,
                FirstLifeAboutText = firstLifeAboutText,
                FirstLifeImage = firstLifeImage,
                Image = image,
                Interests = new ProfileInterests() { WantToMask = wantMask, WantToText = wantText, CanDoMask = skillsMask, CanDoText = skillsText, Languages = languages },
                PrincipalID = agentID,
                WebURL = profileUrl
            };
        }

        private bool GetUserProfileData(UUID userID, out Dictionary<string, object> userInfo)
        {
            IUserFinder uManage = m_registry.RequestModuleInterface<IUserFinder>();
            userInfo = new Dictionary<string, object>();

            if (!uManage.IsLocalGridUser(userID))
            {
                // Is Foreign
                string home_url = uManage.GetUserServerURL(userID, "HomeURI");

                if (String.IsNullOrEmpty(home_url))
                {
                    userInfo["user_flags"] = 0;
                    userInfo["user_created"] = 0;
                    userInfo["user_title"] = "Unavailable";

                    return true;
                }

                UserAgentServiceConnector uConn = new UserAgentServiceConnector(home_url);

                Dictionary<string, object> account = uConn.GetUserInfo(userID);

                if (account.Count > 0)
                {
                    if (account.ContainsKey("user_flags"))
                        userInfo["user_flags"] = account["user_flags"];
                    else
                        userInfo["user_flags"] = "";

                    if (account.ContainsKey("user_created"))
                        userInfo["user_created"] = account["user_created"];
                    else
                        userInfo["user_created"] = "";

                    userInfo["user_title"] = "HG Visitor";
                }
                else
                {
                    userInfo["user_flags"] = 0;
                    userInfo["user_created"] = 0;
                    userInfo["user_title"] = "HG Visitor";
                }
                return true;
            }
            return false;
        }

        public bool UpdateUserProfile(IUserProfileInfo Profile)
        {
            return false;
        }

        public void CreateNewProfile(UUID UUID)
        {
        }

        public bool AddClassified(Classified classified)
        {
            return false;
        }

        public Classified GetClassified(UUID queryClassifiedID)
        {
            return null; //not implemented in OS
        }

        public List<Classified> GetClassifieds(UUID ownerID)
        {
            Hashtable ReqHash = new Hashtable();
            ReqHash["uuid"] = ownerID;

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "avatarclassifiedsrequest");

            if (!Convert.ToBoolean(result["success"]))
                return new List<Classified>();

            ArrayList dataArray = (ArrayList)result["data"];

            List<Classified> classifieds = new List<Classified>();

            foreach (Object o in dataArray)
            {
                Hashtable d = (Hashtable)o;

                classifieds.Add(new Classified { ClassifiedUUID = new UUID(d["classifiedid"].ToString()), Name = d["name"].ToString() });
            }
            return classifieds;
        }

        public void RemoveClassified(UUID queryClassifiedID)
        {
        }

        public bool AddPick(ProfilePickInfo pick)
        {
            return false;
        }

        public ProfilePickInfo GetPick(UUID queryPickID)
        {
            Hashtable ReqHash = new Hashtable();

            ReqHash["avatar_id"] = UUID.Zero;
            ReqHash["pick_id"] = queryPickID;

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "pickinforequest");

            if (!Convert.ToBoolean(result["success"]))
                return null;

            ArrayList dataArray = (ArrayList)result["data"];

            Hashtable d = (Hashtable)dataArray[0];

            Vector3 globalPos = new Vector3();
            Vector3.TryParse(d["posglobal"].ToString(), out globalPos);

            if (d["description"] == null)
                d["description"] = String.Empty;

            return new ProfilePickInfo
            {
                PickUUID = new UUID(d["pickuuid"].ToString()),
                CreatorUUID = new UUID(d["creatoruuid"].ToString()),
                TopPick = Convert.ToBoolean(d["toppick"]) ? 1 : 0,
                ParcelUUID = new UUID(d["parceluuid"].ToString()),
                Name = d["name"].ToString(),
                Description = d["description"].ToString(),
                SnapshotUUID = new UUID(d["snapshotuuid"].ToString()),
                User = d["user"].ToString(),
                OriginalName = d["originalname"].ToString(),
                SimName = d["simname"].ToString(),
                GlobalPos = globalPos,
                SortOrder = Convert.ToInt32(d["sortorder"]),
                Enabled = Convert.ToBoolean(d["enabled"]) ? 1 : 0
            };
        }

        public List<ProfilePickInfo> GetPicks(UUID ownerID)
        {
            Hashtable ReqHash = new Hashtable();
            ReqHash["uuid"] = ownerID;

            Hashtable result = GenericXMLRPCRequest(ReqHash,
                    "avatarpicksrequest");

            if (!Convert.ToBoolean(result["success"]))
                return new List<ProfilePickInfo>();

            ArrayList dataArray = (ArrayList)result["data"];

            List<ProfilePickInfo> picks = new List<ProfilePickInfo>();

            if (dataArray != null)
            {
                foreach (Object o in dataArray)
                {
                    Hashtable d = (Hashtable)o;

                    UUID pickID = new UUID(d["pickid"].ToString());
                    string name = d["name"].ToString();
                    picks.Add(new ProfilePickInfo { Name = name, PickUUID = pickID });
                }
            }
            return picks;
        }

        public void RemovePick(UUID queryPickID)
        {
        }

        public string Name
        {
            get { return "IRemoteProfileConnector"; }
        }

        public void Initialize(IGenericData GenericData, IConfigSource source, IRegistryCore simBase, string DefaultConnectionString)
        {
            IConfig hgConfig = source.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean("Enabled", false))
                return;
            Aurora.DataManager.DataManager.RegisterPlugin(this);
        }
    }
}

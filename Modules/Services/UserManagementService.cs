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

using OpenSim.Framework;

using OpenSim.Region.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;

using OpenMetaverse;
using log4net;
using Aurora.Simulation.Base;
using Nini.Config;

namespace Aurora.Addon.Hypergrid
{
    public struct UserData
    {
        public UUID Id;
        public string FirstName;
        public string LastName;
        public string HomeURL;
        public Dictionary<string, object> ServerURLs;
    }

    public class UserManagementModule : BaseUserFinding, ISharedRegionModule, IUserManagement
    {
        private static readonly ILog m_log = LogManager.GetLogger (MethodBase.GetCurrentMethod ().DeclaringType);

        private List<Scene> m_Scenes = new List<Scene> ();

        protected override IUserAccountService UserAccountService
        {
            get
            {
                if(m_Scenes.Count > 0)
                    return m_Scenes[0].RequestModuleInterface<IUserAccountService> ();
                return null;
            }
        }

        #region ISharedRegionModule

        public void Initialise (IConfigSource config)
        {
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            MainConsole.Instance.Commands.AddCommand (
                "show names",
                "show names",
                "Show the bindings between user UUIDs and user names",
                HandleShowUsers);
        }

        public bool IsSharedModule
        {
            get
            {
                return true;
            }
        }

        public string Name
        {
            get
            {
                return "UserManagement Module";
            }
        }

        public Type ReplaceableInterface
        {
            get
            {
                return null;
            }
        }

        public void AddRegion (Scene scene)
        {
            IConfig hgConfig = scene.Config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            m_Scenes.Add (scene);

            scene.RegisterModuleInterface<IUserManagement> (this);
            scene.EventManager.OnNewClient += EventManager_OnNewClient;
            scene.EventManager.OnStartupFullyComplete += EventManager_OnStartupFullyComplete;
        }

        public void RemoveRegion (Scene scene)
        {
            scene.UnregisterModuleInterface<IUserManagement> (this);
            m_Scenes.Remove (scene);
        }

        public void RegionLoaded (Scene s)
        {
        }

        public void PostInitialise ()
        {
        }

        public void Close ()
        {
            m_Scenes.Clear ();
            m_UserCache.Clear ();
        }

        #endregion ISharedRegionModule


        #region Event Handlers

        void EventManager_OnStartupFullyComplete (IScene scene, List<string> data)
        {
            // let's sniff all the user names referenced by objects in the scene
            m_log.DebugFormat ("[USER MANAGEMENT MODULE]: Caching creators' data from {0} ({1} objects)...", scene.RegionInfo.RegionName, scene.Entities.Count);
            ((Scene)scene).ForEachSOG (delegate (SceneObjectGroup sog)
            {
                CacheCreators (sog);
            });
        }

        void EventManager_OnNewClient (IClientAPI client)
        {
            client.OnNameFromUUIDRequest += new UUIDNameRequest (HandleUUIDNameRequest);
        }

        void HandleUUIDNameRequest (UUID uuid, IClientAPI remote_client)
        {
            string[] names = GetUserNames (uuid);
            if (names.Length == 2)
            {
                //m_log.DebugFormat("[XXX] HandleUUIDNameRequest {0} is {1} {2}", uuid, names[0], names[1]);
                remote_client.SendNameReply (uuid, names[0], names[1]);
            }
        }

        #endregion Event Handlers

        private void CacheCreators (SceneObjectGroup sog)
        {
            //m_log.DebugFormat("[USER MANAGEMENT MODULE]: processing {0} {1}; {2}", sog.RootPart.Name, sog.RootPart.CreatorData, sog.RootPart.CreatorIdentification);
            /*AddUser (sog.RootPart.CreatorID, sog.RootPart.CreatorData);

            foreach (SceneObjectPart sop in sog.Parts)
            {
                AddUser (sop.CreatorID, sop.CreatorData);
                foreach (TaskInventoryItem item in sop.TaskInventory.Values)
                    AddUser (item.CreatorID, item.CreatorData);
            }*/
        }

        private void HandleShowUsers (string[] cmd)
        {
            if (m_UserCache.Count == 0)
            {
                MainConsole.Instance.Output ("No users not found");
                return;
            }

            MainConsole.Instance.Output ("UUID                                 User Name");
            MainConsole.Instance.Output ("-----------------------------------------------------------------------------");
            foreach (KeyValuePair<UUID, UserData> kvp in m_UserCache)
            {
                MainConsole.Instance.Output (String.Format ("{0} {1} {2}",
                       kvp.Key, kvp.Value.FirstName, kvp.Value.LastName));
            }
            return;
        }
    }

    public class BaseUserFinding
    {
        /// <summary>
        /// The cache
        /// </summary>
        protected Dictionary<UUID, UserData> m_UserCache = new Dictionary<UUID, UserData> ();

        protected virtual IUserAccountService UserAccountService
        {
            get { return null; }
        }

        #region IUserManagement

        protected string[] GetUserNames (UUID uuid)
        {
            string[] returnstring = new string[2];

            if (m_UserCache.ContainsKey (uuid))
            {
                returnstring[0] = m_UserCache[uuid].FirstName;
                returnstring[1] = m_UserCache[uuid].LastName;
                return returnstring;
            }

            UserAccount account = UserAccountService.GetUserAccount (UUID.Zero, uuid);

            if (account != null)
            {
                returnstring[0] = account.FirstName;
                returnstring[1] = account.LastName;

                UserData user = new UserData ();
                user.FirstName = account.FirstName;
                user.LastName = account.LastName;

                lock (m_UserCache)
                    m_UserCache[uuid] = user;
            }
            else
            {
                returnstring[0] = "Unknown";
                returnstring[1] = "User";
            }

            return returnstring;
        }

        public string GetUserName (UUID uuid)
        {
            //m_log.DebugFormat("[XXX] GetUserName {0}", uuid);
            string[] names = GetUserNames (uuid);
            if (names.Length == 2)
            {
                string firstname = names[0];
                string lastname = names[1];

                return firstname + " " + lastname;

            }
            return "(hippos)";
        }

        public string GetUserHomeURL (UUID userID)
        {
            if (m_UserCache.ContainsKey (userID))
                return m_UserCache[userID].HomeURL;

            return string.Empty;
        }

        public string GetUserServerURL (UUID userID, string serverType)
        {
            if (m_UserCache.ContainsKey (userID))
            {
                UserData userdata = m_UserCache[userID];
                if (userdata.ServerURLs != null && userdata.ServerURLs.ContainsKey (serverType) && userdata.ServerURLs[serverType] != null)
                    return userdata.ServerURLs[serverType].ToString ();

                if (userdata.HomeURL != string.Empty)
                {
                    UserAgentServiceConnector uConn = new UserAgentServiceConnector (userdata.HomeURL);
                    userdata.ServerURLs = uConn.GetServerURLs (userID);
                    if (userdata.ServerURLs != null && userdata.ServerURLs.ContainsKey (serverType) && userdata.ServerURLs[serverType] != null)
                        return userdata.ServerURLs[serverType].ToString ();
                }
            }

            return string.Empty;
        }

        public string GetUserUUI (UUID userID)
        {
            UserAccount account = UserAccountService.GetUserAccount (UUID.Zero, userID);
            if (account != null)
                return userID.ToString ();

            if (m_UserCache.ContainsKey (userID))
            {
                UserData ud = m_UserCache[userID];
                string homeURL = ud.HomeURL;
                string first = ud.FirstName, last = ud.LastName;
                if (ud.LastName.StartsWith ("@"))
                {
                    string[] parts = ud.FirstName.Split ('.');
                    if (parts.Length >= 2)
                    {
                        first = parts[0];
                        last = parts[1];
                    }
                    return userID + ";" + homeURL + ";" + first + " " + last;
                }
            }

            return userID.ToString ();
        }

        public void AddUser (UUID uuid, string userData)
        {
            if (m_UserCache.ContainsKey (uuid))
                return;

            UserData user = new UserData ();
            user.Id = uuid;
            UserAccount account = UserAccountService.GetUserAccount (UUID.Zero, uuid);
            if (account != null)
            {
                user.FirstName = account.FirstName;
                user.LastName = account.LastName;
                // user.ProfileURL = we should initialize this to the default
            }
            else
            {
                if (userData != null && userData != string.Empty)
                {
                    bool addOne = false;
                    string[] parts = userData.Split (';');
                    if (parts.Length >= 1)
                    {
                        UUID sid;
                        if (UUID.TryParse (parts[0], out sid))
                            addOne = true;
                        user.HomeURL = parts[addOne ? 1 : 0];
                        try
                        {
                            Uri uri = new Uri (parts[addOne ? 1 : 0]);
                            user.LastName = "@" + uri.Authority;
                        }
                        catch (UriFormatException)
                        {
                            user.LastName = "@unknown";
                        }
                    }
                    if (parts.Length >= 2)
                        user.FirstName = parts[addOne ? 2 : 1].Replace (' ', '.');
                }
                else
                    return;
            }

            lock (m_UserCache)
                m_UserCache[uuid] = user;
        }

        public void AddUser (UUID uuid, string first, string last, string profileURL)
        {
            AddUser (uuid, profileURL + ";" + first + " " + last);
        }

        #endregion IUserManagement
    }

    public class HGUserFinder : BaseUserFinding, IService, IUserFinder
    {
        protected IRegistryCore m_registry;

        protected override IUserAccountService UserAccountService
        {
            get { return m_registry.RequestModuleInterface<IUserAccountService>(); }
        }

        public void Initialize (IConfigSource config, IRegistryCore registry)
        {
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            HGUtil.Registry = registry;
            m_registry = registry;
            m_registry.RegisterModuleInterface<IUserFinder> (this);
        }

        public void Start (IConfigSource config, IRegistryCore registry)
        {
        }

        public void FinishedStartup ()
        {
        }
    }
}

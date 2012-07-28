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

using Aurora.Framework;

using OpenSim.Region.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;

using OpenMetaverse;
using Aurora.Simulation.Base;
using Nini.Config;

namespace Aurora.Addon.HyperGrid
{
    public class UserManagementModule : ISharedRegionModule
    {
        private List<IScene> m_Scenes = new List<IScene> ();
        private IUserFinder m_userFinder = null;

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
                return "UserManagementModule";
            }
        }

        public Type ReplaceableInterface
        {
            get
            {
                return null;
            }
        }

        public void AddRegion (IScene scene)
        {
            IConfig hgConfig = scene.Config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            m_Scenes.Add(scene);
            m_userFinder = scene.RequestModuleInterface<IUserFinder>();

            scene.EventManager.OnNewClient += EventManager_OnNewClient;
        }

        public void RemoveRegion (IScene scene)
        {
            m_Scenes.Remove (scene);
        }

        public void RegionLoaded (IScene s)
        {
        }

        public void PostInitialise ()
        {
        }

        public void Close ()
        {
            m_Scenes.Clear ();
        }

        #endregion ISharedRegionModule

        #region Event Handlers

        void EventManager_OnNewClient (IClientAPI client)
        {
            client.OnNameFromUUIDRequest += new UUIDNameRequest (HandleUUIDNameRequest);
        }

        void HandleUUIDNameRequest (UUID uuid, IClientAPI remote_client)
        {
            UserAccount account = remote_client.Scene.UserAccountService.GetUserAccount (null, uuid);
            if(account == null)
            {
                string[] names = m_userFinder.GetUserNames(uuid);
                if(names.Length == 2)
                {
                    //MainConsole.Instance.DebugFormat("[XXX] HandleUUIDNameRequest {0} is {1} {2}", uuid, names[0], names[1]);
                    remote_client.SendNameReply(uuid, names[0], names[1]);
                }
            }
        }

        #endregion Event Handlers

        private void HandleShowUsers (string[] cmd)
        {
            IGenericsConnector generics = Aurora.DataManager.DataManager.RequestPlugin<IGenericsConnector>();
            List<UserData> users = generics.GetGenerics<UserData>(UUID.Zero, "ForeignUsers");
            if (users.Count == 0)
            {
                MainConsole.Instance.Output ("No users not found");
                return;
            }

            MainConsole.Instance.Output ("UUID                                 User Name");
            MainConsole.Instance.Output ("-----------------------------------------------------------------------------");
            foreach (UserData kvp in users)
            {
                MainConsole.Instance.Output (String.Format ("{0} {1} {2}",
                       kvp.Id, kvp.FirstName, kvp.LastName));
            }
            return;
        }
    }

    public class HGUserFinder : IService, IUserFinder
    {
        protected IRegistryCore m_registry;
        protected IUserAccountService UserAccountService;
        protected IGenericsConnector m_generics;

        public void Initialize (IConfigSource config, IRegistryCore registry)
        {
            IConfig hgConfig = config.Configs["HyperGrid"];
            if (hgConfig == null || !hgConfig.GetBoolean ("Enabled", false))
                return;

            m_registry = registry;
            m_registry.RegisterModuleInterface<IUserFinder> (this);
        }

        public void Start (IConfigSource config, IRegistryCore registry)
        {
            UserAccountService = registry.RequestModuleInterface<IUserAccountService>();
            m_generics = Aurora.DataManager.DataManager.RequestPlugin<IGenericsConnector>();
        }

        public void FinishedStartup ()
        {
        }


        #region IUserManagement

        private bool GetUserData(UUID uuid, out UserData data)
        {
            return (data = m_generics.GetGeneric<UserData>(UUID.Zero, "ForeignUsers", uuid.ToString())) != null;
        }

        public string[] GetUserNames(UUID uuid)
        {
            string[] returnstring = new string[2];

            UserData data;
            if (GetUserData(uuid, out data))
            {
                returnstring[0] = data.FirstName;
                returnstring[1] = data.LastName;
                return returnstring;
            }

            return new string[0];
        }

        public bool IsLocalGridUser(UUID uuid)
        {
            UserData data;
            return !GetUserData(uuid, out data);
        }

        public string GetUserName(UUID uuid)
        {
            //MainConsole.Instance.DebugFormat("[XXX] GetUserName {0}", uuid);
            string[] names = GetUserNames(uuid);
            if (names.Length == 2)
            {
                string firstname = names[0];
                string lastname = names[1];

                return firstname + " " + lastname;

            }
            return "(hippos)";
        }

        public string GetUserHomeURL(UUID userID)
        {
            UserData data;
            if (GetUserData(userID, out data))
                return data.HomeURL;

            return string.Empty;
        }

        public bool GetUserExists(UUID userID)
        {
            UserData data;
            return GetUserData(userID, out data);
        }

        public string GetUserServerURL(UUID userID, string serverType)
        {
            UserData userdata;
            if (GetUserData(userID, out userdata))
            {
                if (userdata.ServerURLs != null && userdata.ServerURLs.ContainsKey(serverType) && userdata.ServerURLs[serverType] != null)
                    return userdata.ServerURLs[serverType].ToString();

                if (userdata.HomeURL != string.Empty)
                {
                    UserAgentServiceConnector uConn = new UserAgentServiceConnector(userdata.HomeURL);
                    userdata.ServerURLs = uConn.GetServerURLs(userID);
                    if (userdata.ServerURLs != null && userdata.ServerURLs.ContainsKey(serverType) && userdata.ServerURLs[serverType] != null)
                        return userdata.ServerURLs[serverType].ToString();
                }
            }

            return string.Empty;
        }

        public string GetUserUUI(UUID userID)
        {
            UserAccount account = UserAccountService.GetUserAccount(null, userID);
            if (account != null)
                return userID.ToString();

            UserData ud;
            if (GetUserData(userID, out ud))
            {
                string homeURL = ud.HomeURL;
                string first = ud.FirstName, last = ud.LastName;
                if (ud.LastName.StartsWith("@"))
                {
                    string[] parts = ud.FirstName.Split('.');
                    if (parts.Length >= 2)
                    {
                        first = parts[0];
                        last = parts[1];
                    }
                    return userID + ";" + homeURL + ";" + first + " " + last;
                }
            }

            return userID.ToString();
        }

        public void AddUser(UUID uuid, string firstName, string lastName, Dictionary<string, object> serviceUrls)
        {
            UserData ud = new UserData();
            ud.FirstName = firstName;
            ud.Id = uuid;
            ud.LastName = lastName;
            ud.ServerURLs = serviceUrls;
            if (ud.ServerURLs != null && ud.ServerURLs.ContainsKey(GetHandlers.Helpers_HomeURI))
                ud.HomeURL = ud.ServerURLs[GetHandlers.Helpers_HomeURI].ToString();
            else
                ud.HomeURL = "";
            if (ud.ServerURLs == null)
                ud.ServerURLs = new Dictionary<string, object>();

            m_generics.AddGeneric(UUID.Zero, "ForeignUsers", uuid.ToString(), ud.ToOSD());
        }

        public void AddUser(UUID uuid, string userData)
        {
            UserData user = new UserData();
            user.Id = uuid;
            UserAccount account = UserAccountService.GetUserAccount(null, uuid);
            if (account == null)
            {
                if (userData != null && userData != string.Empty)
                {
                    bool addOne = false;
                    string[] parts = userData.Split(';');
                    if (parts.Length >= 1)
                    {
                        UUID sid;
                        if (UUID.TryParse(parts[0], out sid))
                            addOne = true;
                        user.HomeURL = parts[addOne ? 1 : 0];
                        try
                        {
                            Uri uri = new Uri(parts[addOne ? 1 : 0]);
                            user.LastName = "@" + uri.Authority;
                        }
                        catch (UriFormatException)
                        {
                            user.LastName = "@unknown";
                        }
                    }
                    if (parts.Length >= 2)
                        user.FirstName = parts[addOne ? 2 : 1].Replace(' ', '.');
                    m_generics.AddGeneric(UUID.Zero, "ForeignUsers", uuid.ToString(), user.ToOSD());
                }
            }
        }

        #endregion IUserManagement
    }
}

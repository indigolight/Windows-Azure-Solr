﻿#region Copyright Notice
/*
Copyright © Microsoft Open Technologies, Inc.
All Rights Reserved
Apache 2.0 License

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

     http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.

See the Apache Version 2.0 License for specific language governing permissions and limitations under the License.
*/
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.WindowsAzure.ServiceRuntime;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Runtime.Serialization;
using System.Globalization;
using System.Security.Permissions;

namespace HelperLib
{
    public static class Util
    {
        public static void AddRoleInfoEntry(string roleId, string ip, int port, bool isMaster)
        {
            RoleInfoDataSource rids = new RoleInfoDataSource();
            rids.AddRoleInfoEntity(new RoleInfoEntity() { RoleId = roleId, IPString = ip, Port = port, IsSolrMaster = isMaster });
        }

        public static string GetMasterEndpoint()
        {
            int numTries = 100;

            while (--numTries > 0) // try multiple times since master may be initializing
            {
                string masterUrl = GetSolrEndpoint(true, -1);
                if (masterUrl == null)
                {
                    Thread.Sleep(10000);
                    continue;
                }

                return masterUrl;
            }

            throw new OperationFailedException("Solr master not reachable.") { OperationName = "GetMasterUrl" };
        }

        // returns null if the url could not be obtained for any reason (such as the role was not available)
        public static string GetSolrEndpoint(bool bMaster, int iInstance = -1)
        {
            string url = null;

            try
            {
                // Worker role access:
                IPEndPoint endpoint = GetSolrEndpointInfo(bMaster, iInstance);
                if (endpoint == null)
                    return null;

                url = string.Format(CultureInfo.InvariantCulture, "http://{0}/solr/", endpoint);
            }
            catch { }

            return url;
        }

        /// <summary>
        /// Get the url associated with the worker instance. If running with a warm standby instance
        /// it returns the address on which solr is actually listening.
        /// Specify bMaster = true to get master instance, false to get slave instance.
        /// Specify iInstance = -1 to get the endpoint of any instance of that type that may be actively listening.
        /// </summary>
        private static IPEndPoint GetSolrEndpointInfo(bool bMaster, int iInstance)
        {
            var roleInstances = RoleEnvironment.Roles[bMaster ? "SolrMasterHostWorkerRole" : "SolrSlaveHostWorkerRole"].Instances;
            IPEndPoint solrEndpoint = null;

            if (iInstance >= 0)
                solrEndpoint = GetEndpoint(roleInstances[iInstance], bMaster);
            else
            {
                foreach (var instance in roleInstances)
                {
                    solrEndpoint = GetEndpoint(instance, bMaster);
                    if (solrEndpoint == null)
                        continue;

                    break;
                }
            }

            if (solrEndpoint == null)
                return null;

            return solrEndpoint;
        }

        private static IPEndPoint GetEndpoint(RoleInstance instance, bool bMaster)
        {
            IPEndPoint endpoint;

            RoleInfoDataSource rids = new RoleInfoDataSource();

            if (bMaster)
                endpoint = rids.GetMasterEndpoint();
            else
                endpoint = rids.GetSlaveEndpoint(instance.Id);

            if (!CheckEndpoint(endpoint))
                return null;

            return endpoint;
        }

        public static int GetNumInstances(bool bMaster)
        {
            var roleInstances = RoleEnvironment.Roles[bMaster ? "SolrMasterHostWorkerRole" : "SolrSlaveHostWorkerRole"].Instances;
            return roleInstances.Count();
        }

        private static bool CheckEndpoint(IPEndPoint solrEndpoint)
        {
            var valid = false;
            using (var s = new Socket(solrEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp))
            {
                try
                {
                    s.Connect(solrEndpoint);
                    if (s.Connected)
                    {
                        valid = true;
                        s.Disconnect(true);
                    }
                    else
                    {
                        valid = false;
                    }
                }
                catch
                {
                    valid = false;
                }
            }

            return valid;
        }
    }

    [Serializable]
    public class OperationFailedException : Exception
    {
        public OperationFailedException()
        {
        }

        public OperationFailedException(string message)
            : base(message)
        {
        }

        public OperationFailedException(string message, Exception innerException) :
            base(message, innerException)
        {
        }

        protected OperationFailedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            if (info != null)
            {
                this.OperationName = info.GetString("OperationName");
            }
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            if (info != null)
            {
                info.AddValue("OperationName", this.OperationName);
            }
        }

        public string OperationName { get; set; }
    }
}
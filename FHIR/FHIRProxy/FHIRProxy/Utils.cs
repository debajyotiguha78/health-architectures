﻿using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using StackExchange.Redis;
namespace FHIRProxy
{
    public class Utils
    {
        public static readonly string AUTH_STATUS_HEADER = "fhirproxy-AuthorizationStatus";
        public static readonly string AUTH_STATUS_MSG_HEADER = "fhirproxy-AuthorizationStatusMessage";
        public static readonly string FHIR_PROXY_ROLES = "fhirproxy-roles";
        public static readonly string UNSUPPORTED_CMDS = "$export,_operations";
        private static Lazy<ConnectionMultiplexer> lazyConnection = new Lazy<ConnectionMultiplexer>(() =>
        {
            string cacheConnection = GetEnvironmentVariable("REDISCONNECTION");
            return ConnectionMultiplexer.Connect(cacheConnection);
        });
        public static bool UnsupportedCommands(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return false;
            if (UNSUPPORTED_CMDS.Contains(cmd.ToLower())) return true;
            return false;
        }
        public static ConnectionMultiplexer RedisConnection
        {
            get
            {
                return lazyConnection.Value;
            }
        }
        public static string genOOErrResponse(string code,string desc)
        {

            return $"{{\"resourceType\": \"OperationOutcome\",\"id\": \"{Guid.NewGuid().ToString()}\",\"issue\": [{{\"severity\": \"error\",\"code\": \"{code ?? ""}\",\"diagnostics\": \"{desc ?? ""}\"}}]}}";

        }
        //Server Roles are "A"dmin,"R"eader,"W"riter
        public static bool inServerAccessRole(HttpRequest req,string role)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUTHFREEPASS"))) return true;
            string s = req.Headers[Utils.FHIR_PROXY_ROLES];
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(role)) return false;
            return s.Contains(role);
            
        }
        public static bool isServerAccessAuthorized(HttpRequest req)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUTHFREEPASS"))) return true;
            if (req.Headers.ContainsKey(AUTH_STATUS_HEADER))
            {
                var h = req.Headers[AUTH_STATUS_HEADER];
                if (h.Count > 0)
                {
                    var s = h.First();
                    if (s == null || !s.Equals("200")) return false;
                }
                return true;
            }
            return false;
        }

        public static FHIRResponse reverseProxyResponse(FHIRResponse fhirresp, HttpRequest req, string res)
        {
            if (fhirresp != null)
            {
                if (fhirresp.Headers.ContainsKey("Location"))
                {
                    fhirresp.Headers["Location"].Value = fhirresp.Headers["Location"].Value.Replace(Environment.GetEnvironmentVariable("FS_URL"), req.Scheme + "://" + req.Host.Value + req.Path.Value.Substring(0, req.Path.Value.IndexOf(res) - 1));
                }
                if (fhirresp.Headers.ContainsKey("Content-Location"))
                {
                    fhirresp.Headers["Content-Location"].Value = fhirresp.Headers["Content-Location"].Value.Replace(Environment.GetEnvironmentVariable("FS_URL"), req.Scheme + "://" + req.Host.Value + req.Path.Value.Substring(0, req.Path.Value.IndexOf(res) - 1));
                }
                var str = fhirresp.Content == null ? "" : fhirresp.Content.ToString();
                /* Fix server locations to proxy address */
                str = str.Replace(Environment.GetEnvironmentVariable("FS_URL"), req.Scheme + "://" + req.Host.Value + (res != null ? req.Path.Value.Substring(0, req.Path.Value.IndexOf(res) - 1) : req.Path.Value));
                foreach (string key in fhirresp.Headers.Keys)
                {
                    
                    req.HttpContext.Response.Headers[key] = fhirresp.Headers[key].Value;
                }
                fhirresp.Content = str;
                return fhirresp;
            }
            return null;
        }

        public static void deleteLinkEntity(CloudTable table, LinkEntity entity)
        {
            TableOperation delete = TableOperation.Delete(entity);
            table.ExecuteAsync(delete).GetAwaiter().GetResult();
            return;
        }
        public static void setLinkEntity(CloudTable table, LinkEntity entity)
        {
            TableOperation insertorreplace = TableOperation.InsertOrReplace(entity);
            table.ExecuteAsync(insertorreplace).GetAwaiter().GetResult();
            return;
        }
        public static LinkEntity getLinkEntity(CloudTable table, string resourceType, string principalId)
        {

            TableOperation retrieveOperation = TableOperation.Retrieve<LinkEntity>(resourceType, principalId);

            TableResult query = table.ExecuteAsync(retrieveOperation).GetAwaiter().GetResult();
            return (LinkEntity)query.Result;

        }
        public static CloudTable getTable()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("STORAGEACCT"));

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

            // Retrieve a reference to the table.
            CloudTable table = tableClient.GetTableReference("identitylinks");

            // Create the table if it doesn't exist.
            table.CreateIfNotExistsAsync().GetAwaiter().GetResult();
            return table;
        }
        public static string GetEnvironmentVariable(string varname, string defval=null)
        {
            if (string.IsNullOrEmpty(varname)) return null;
            string retVal = System.Environment.GetEnvironmentVariable(varname);
            if (defval != null && retVal == null) return defval;
            return retVal;
        }
        public static int GetIntEnvironmentVariable(string varname,string defval=null)
        {

            
            string retVal = System.Environment.GetEnvironmentVariable(varname);
            if (defval != null && retVal == null) retVal = defval;
            return int.Parse(retVal);
        }

    }
}

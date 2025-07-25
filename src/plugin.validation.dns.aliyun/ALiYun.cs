﻿using AlibabaCloud.OpenApiClient.Models;
using AlibabaCloud.SDK.Alidns20150109;
using AlibabaCloud.SDK.Alidns20150109.Models;
using AlibabaCloud.TeaUtil.Models;
using PKISharp.WACS.Clients.DNS;
using PKISharp.WACS.Plugins.Base.Capabilities;
using PKISharp.WACS.Plugins.Interfaces;
using PKISharp.WACS.Services;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

//Api Key: http://ram.console.aliyun.com/manage/ak
//Api Doc: https://api.aliyun.com/api/Alidns/2015-01-09/AddDomainRecord
//Api Server: https://api.aliyun.com/product/Alidns
namespace PKISharp.WACS.Plugins.ValidationPlugins.Dns
{
    [IPlugin.Plugin1<ALiYunOptions, ALiYunOptionsFactory, DnsValidationCapability, ALiYunJson, ALiYunArguments>
        ("1d4db2ea-ce7c-46ce-b86f-40b356fcf999",
        "Aliyun", "Create verification records in ALiYun DNS",
        External = true, Provider = "Alibaba", Page = "alibaba")]
    public class ALiYun(SecretServiceManager ssm,
        LookupClientProvider dnsClient, ILogService log, ISettings settings, IProxyService proxy,
        ALiYunOptions options) : DnsValidation<ALiYun, Client>(dnsClient, log, settings, proxy)
    {
        protected override async Task<Client> CreateClient(HttpClient client)
        {
            return new Client(new Config()
            {
                AccessKeyId = await ssm.EvaluateSecret(options.ApiID),
                AccessKeySecret = await ssm.EvaluateSecret(options.ApiSecret),
                Endpoint = await ssm.EvaluateSecret(options.ApiServer),
            });
        }

        public override async Task<bool> CreateRecord(DnsValidationRecord record)
        {
            try
            {
                var client = await GetClient();
                var identifier = GetDomain(client, record) ?? throw new($"The domain name cannot be found: {record.Context.Identifier}");
                var domain = record.Authority.Domain;
                var value = record.Value;
                //Add Record
                return AddRecord(client, identifier, domain, value);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                //Out Error
                _log.Error($"Unable to add ALiYunDNS record: {ex.Message}");
            }
            return false;
        }

        public override async Task DeleteRecord(DnsValidationRecord record)
        {
            await Task.Delay(0);
            try
            {
                var client = await GetClient();
                var identifier = GetDomain(client, record) ?? throw new($"The domain name cannot be found: {record.Context.Identifier}");
                var domain = record.Authority.Domain;
                //Delete Record
                _ = DelRecord(client, identifier, domain);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                //Out Error
                _log.Error($"Unable to delete ALiYunDNS record: {ex.Message}");
            }
        }

        #region PrivateLogic

        /// <summary>
        /// Add Record
        /// </summary>
        /// <param name="domain">Domain</param>
        /// <param name="subDomain">SubDomain</param>
        /// <param name="value">Value</param>
        /// <returns></returns>
        private bool AddRecord(Client client, string domain, string subDomain, string value)
        {
            subDomain = subDomain.Replace($".{domain}", "");
            //Delete Record
            _ = DelRecord(client, domain, subDomain);
            //Add Record
            var addRecords = new AddDomainRecordRequest
            {
                DomainName = domain,
                RR = subDomain,
                Type = "TXT",
                Value = value
            };
            var runtime = new RuntimeOptions();
            client.AddDomainRecordWithOptions(addRecords, runtime);
            return true;
        }

        /// <summary>
        /// Delete Record
        /// </summary>
        /// <param name="domain">Domain</param>
        /// <param name="subDomain">SubDomain</param>
        /// <returns></returns>
        private bool DelRecord(Client client, string domain, string subDomain)
        {
            subDomain = subDomain.Replace($".{domain}", "");
            //Get RecordID
            var recordId = GetRecordID(client, domain, subDomain);
            if (recordId == default) return false;
            //Delete Record
            var delRecords = new DeleteDomainRecordRequest
            {
                RecordId = recordId.ToString(),
            };
            var runtime = new RuntimeOptions();
            client.DeleteDomainRecordWithOptions(delRecords, runtime);
            return true;
        }

        /// <summary>
        /// Get RecordID
        /// </summary>
        /// <param name="domain">Domain</param>
        /// <param name="subDomain">SubDomain</param>
        /// <returns></returns>
        private static string? GetRecordID(Client client, string domain, string subDomain)
        {
            var getRecords = new DescribeDomainRecordsRequest
            {
                DomainName = domain,
            };
            var runtime = new RuntimeOptions();
            var data = client.DescribeDomainRecordsWithOptions(getRecords, runtime);
            //Console.WriteLine(data);
            var jsonDataLinq = data.Body.DomainRecords.Record.Where(w => w.RR == subDomain && w.Type == "TXT");
            if (jsonDataLinq.Any()) return jsonDataLinq.First().RecordId;
            return default;
        }

        /// <summary>
        /// Get Domain
        /// </summary>
        /// <param name="record">DnsValidationRecord</param>
        /// <returns></returns>
        private string? GetDomain(Client client, DnsValidationRecord record)
        {
            var detDomains = new DescribeDomainsRequest();
            var runtime = new RuntimeOptions();
            var data = client.DescribeDomainsWithOptions(detDomains, runtime);
            //Console.WriteLine(data);
            var myDomains = data.Body.Domains.Domain.Select(t => t.DomainName);
            var zone = FindBestMatch(myDomains.ToDictionary(x => x), record.Authority.Domain);
            if (zone != null) return zone;
            return default;
        }

        #endregion PrivateLogic
    }
}

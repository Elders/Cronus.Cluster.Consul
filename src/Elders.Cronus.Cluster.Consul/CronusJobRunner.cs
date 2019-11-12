﻿using Elders.Cronus.Cluster.Consul.Internal;
using Elders.Cronus.Cluster.Job;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Elders.Cronus.Cluster.Consul
{
    public class CronusJobRunner : IClusterOperations, IDisposable, ICronusJobRunner
    {
        private readonly HttpClient _client;

        string _jobName = string.Empty;
        string _sessionId = string.Empty;

        public CronusJobRunner(HttpClient httpClient)
        {
            _client = httpClient;
        }

        public async Task<JobExecutionStatus> ExecuteAsync(ICronusJob<object> job, CancellationToken cancellationToken = default)
        {
            _jobName = job.Name;

            try
            {
                CronusJobState jobState = await GetJobStateAsync(cancellationToken).ConfigureAwait(false);

                if (jobState == CronusJobState.UpForGrab)
                {
                    await job.SyncInitialStateAsync(this, cancellationToken).ConfigureAwait(false);

                    bool iAmKing = await BecomeKingAsync(job.Data, cancellationToken).ConfigureAwait(false);
                    if (iAmKing)
                    {
                        jobState = await GetJobStateAsync(cancellationToken).ConfigureAwait(false);
                        if (jobState == CronusJobState.UpForGrab)
                            return await job.RunAsync(this, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (jobState == CronusJobState.Running)
                    return JobExecutionStatus.Running;
            }
            catch (Exception) { return JobExecutionStatus.Failed; }

            return JobExecutionStatus.Completed;
        }

        public Task<TData> PingAsync<TData>(TData data, CancellationToken cancellationToken = default) where TData : class, new()
        {
            return Task.Factory.ContinueWhenAll(new Task[]{
                RenewSessionAsync(cancellationToken),
                TrackProgressAsync(data,cancellationToken)
            }, _ => data);
        }

        public Task<TData> PingAsync<TData>(CancellationToken cancellationToken = default) where TData : class, new()
        {
            return Task.Factory
                .ContinueWhenAll(new Task[] { RenewSessionAsync(cancellationToken) }, _ => Task.CompletedTask)
                .ContinueWith(x => GetJobDataAsync<TData>(cancellationToken))
                .Result;
        }

        public void Dispose()
        {
            KingIsDead().GetAwaiter().GetResult();
        }

        private async Task<string> RequestSessionAsync(CancellationToken cancellationToken = default)
        {
            const string resource = "v1/session/create";

            StringContent content = GetJsonRequestBody(new SessionRequest(_jobName));
            HttpResponseMessage response = await _client.PutAsync(resource, content, cancellationToken).ConfigureAwait(false);
            var session = await ParseResponse<SessionCreateResponse>(response).ConfigureAwait(false);

            return session.ID;
        }

        private Task RenewSessionAsync(CancellationToken cancellationToken = default)
        {
            string resource = $"v1/session/renew/{_sessionId}";

            return _client.PutAsync(resource, null, cancellationToken);
        }

        private bool IAmKing => string.IsNullOrEmpty(_sessionId) == false;

        private async Task<CronusJobState> GetJobStateAsync(CancellationToken cancellationToken = default)
        {
            CronusJobState jobState = CronusJobState.UpForGrab;

            string resource = $"v1/kv/{_jobName}";
            HttpResponseMessage response = await _client.GetAsync(resource, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var asd = await ParseResponse<List<KVResponse>>(response).ConfigureAwait(false);
                var result = asd.FirstOrDefault();

                jobState = string.IsNullOrEmpty(result.Session)
                    ? CronusJobState.UpForGrab
                    : CronusJobState.Running;

                if (string.IsNullOrEmpty(_sessionId) == false && result.Session.Equals(_sessionId, StringComparison.OrdinalIgnoreCase))
                    jobState = CronusJobState.UpForGrab;
            }

            return jobState;
        }

        private async Task<TData> GetJobDataAsync<TData>(CancellationToken cancellationToken = default) where TData : class, new()
        {
            string resource = $"v1/kv/{_jobName}";
            HttpResponseMessage response = await _client.GetAsync(resource, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var asd = await ParseResponse<List<KVResponse>>(response).ConfigureAwait(false);
                var result = asd.FirstOrDefault();

                if (resource != null && result.Value != null)
                {
                    var dataFromCluster = JsonSerializer.Deserialize<TData>(Encoding.UTF8.GetString(Convert.FromBase64String(result.Value)));
                    if (dataFromCluster != null)
                        return dataFromCluster;
                }
            }

            return new TData();
        }

        private Task TrackProgressAsync(object data, CancellationToken cancellationToken = default)
        {
            string resource = $"v1/kv/{_jobName}?acquire={_sessionId}";
            StringContent content = GetJsonRequestBody(data);

            return _client.PutAsync(resource, content, cancellationToken);
        }

        private async Task<bool> BecomeKingAsync(object data, CancellationToken cancellationToken = default)
        {
            string sessionId = await RequestSessionAsync(cancellationToken).ConfigureAwait(false);

            string resource = $"v1/kv/{_jobName}?acquire={sessionId}";
            StringContent content = GetJsonRequestBody(data);

            HttpResponseMessage response = await _client.PutAsync(resource, content, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                bool isSuccess = await ParseResponse<bool>(response);
                if (isSuccess)
                    _sessionId = sessionId;
            }

            return IAmKing;
        }

        private Task KingIsDead()
        {
            if (string.IsNullOrEmpty(_sessionId) == false)
            {
                string resource = $"v1/kv/{_jobName}?release={_sessionId}";

                return _client.PutAsync(resource, null);
            }

            return Task.CompletedTask;
        }

        private async Task<T> ParseResponse<T>(HttpResponseMessage response) where T : new()
        {
            if (response.IsSuccessStatusCode)
            {
                string stringContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                T result = JsonSerializer.Deserialize<T>(stringContent);

                return result;
            }

            return new T();
        }

        private StringContent GetJsonRequestBody(object bodyContent)
        {
            var jsonBody = JsonSerializer.Serialize(bodyContent, bodyContent.GetType());

            var stringContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            return stringContent;
        }
    }
}

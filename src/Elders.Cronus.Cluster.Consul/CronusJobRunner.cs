using Elders.Cronus.Cluster.Consul.Internal;
using Elders.Cronus.Cluster.Job;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Elders.Cronus.Cluster.Consul
{
    public sealed class CronusJobRunner : AbstractCronusJobRunner, IClusterOperations, IDisposable
    {
        private static JsonSerializerSettings settings = new JsonSerializerSettings()
        {
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
        };

        private readonly HttpClient _client;
        private readonly ILogger<CronusJobRunner> logger;

        string _jobName = string.Empty;
        string _sessionId = string.Empty;

        public CronusJobRunner(JobManager jobManager, HttpClient httpClient, ILogger<CronusJobRunner> logger) : base(jobManager)
        {
            _client = httpClient;
            this.logger = logger;
        }

        public async Task<TData> PingAsync<TData>(TData data, CancellationToken cancellationToken = default) where TData : class, new()
        {
            await RenewSessionAsync(cancellationToken).ConfigureAwait(false);
            await TrackProgressAsync(data, cancellationToken).ConfigureAwait(false);

            return data;
        }

        public async Task<TData> PingAsync<TData>(CancellationToken cancellationToken = default) where TData : class, new()
        {
            await RenewSessionAsync(cancellationToken).ConfigureAwait(false);
            return await GetJobDataAsync<TData>(cancellationToken).ConfigureAwait(false);
        }

        public override void Dispose()
        {
            KingIsDead().GetAwaiter().GetResult();
        }

        protected override async Task<JobExecutionStatus> ExecuteInternalAsync(ICronusJob<object> job, CancellationToken cancellationToken = default)
        {
            CancellationTokenSource tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken ct = tokenSource.Token;

            _jobName = job.Name;

            try
            {
                CronusJobState jobState = await GetJobStateAsync(ct).ConfigureAwait(false);

                if (jobState == CronusJobState.UpForGrab)
                {
                    await job.SyncInitialStateAsync(this, ct).ConfigureAwait(false);

                    bool iAmKing = await BecomeKingAsync(job.Data, ct).ConfigureAwait(false);
                    if (iAmKing)
                    {
                        using (Timer pinger = new Timer(PingTimerMethod, tokenSource, TimeSpan.Zero, TimeSpan.FromSeconds(10)))
                        {
                            jobState = await GetJobStateAsync(ct).ConfigureAwait(false);
                            if (jobState == CronusJobState.UpForGrab)
                                return await job.RunAsync(this, ct).ConfigureAwait(false);
                        }
                    }
                    else // How to reproduce this case: 1. schedule a job twice for after 30 seconds (double click rebuild for example) and then kill and start the application
                    {
                        // A thread got here a millisecond before you, snatched your job and started running it , so you are not the king.
                        return JobExecutionStatus.Running;
                    }
                }

                if (jobState == CronusJobState.Running)
                    return JobExecutionStatus.Running;
            }
            catch (Exception ex) when (ExceptionFilterUtility.True(() => logger.LogError(ex, "Failed to execute job {cronus_job_name}", _jobName)))
            {
                return JobExecutionStatus.Failed;
            }

            return JobExecutionStatus.Completed;
        }

        private void PingTimerMethod(object state)
        {
            CancellationTokenSource ts = state as CancellationTokenSource;

            try
            {
                if (ts.Token.IsCancellationRequested) return;

                if (RenewSessionAsync(ts.Token).GetAwaiter().GetResult() == false)
                    ts.Cancel();
            }
            catch (Exception)
            {
                ts.Cancel();
            }
        }

        private async Task<string> RequestSessionAsync(CancellationToken cancellationToken = default)
        {
            const string resource = "v1/session/create";

            StringContent content = GetJsonRequestBody(new SessionRequest(_jobName));
            HttpResponseMessage response = await _client.PutAsync(resource, content, cancellationToken).ConfigureAwait(false);
            var session = await ParseResponse<SessionCreateResponse>(response).ConfigureAwait(false);

            return session.ID;
        }

        private async Task<bool> RenewSessionAsync(CancellationToken cancellationToken = default)
        {
            string resource = $"v1/session/renew/{_sessionId}";

            var response = await _client.PutAsync(resource, null, cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }

        private bool IAmKing => string.IsNullOrEmpty(_sessionId) == false;

        private async Task<CronusJobState> GetJobStateAsync(CancellationToken cancellationToken = default)
        {
            CronusJobState jobState = CronusJobState.UpForGrab;

            string resource = $"v1/kv/cronus/{_jobName}";
            HttpResponseMessage response = await _client.GetAsync(resource, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                List<KVResponse> resultAsList = await ParseResponse<List<KVResponse>>(response).ConfigureAwait(false);
                KVResponse result = resultAsList.FirstOrDefault();

                jobState = string.IsNullOrEmpty(result?.Session)
                    ? CronusJobState.UpForGrab
                    : CronusJobState.Running;

                if (string.IsNullOrEmpty(_sessionId) == false && result.Session.Equals(_sessionId, StringComparison.OrdinalIgnoreCase))
                    jobState = CronusJobState.UpForGrab;
            }

            return jobState;
        }

        private async Task<TData> GetJobDataAsync<TData>(CancellationToken cancellationToken = default) where TData : class, new()
        {
            string resource = $"v1/kv/cronus/{_jobName}";
            HttpResponseMessage response = await _client.GetAsync(resource, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var asd = await ParseResponse<List<KVResponse>>(response).ConfigureAwait(false);
                var result = asd.FirstOrDefault();

                if (resource != null && result.Value != null)
                {
                    string json = Encoding.UTF8.GetString(Convert.FromBase64String(result.Value));
                    var dataFromCluster = JsonConvert.DeserializeObject<TData>(json, settings);
                    if (dataFromCluster != null)
                        return dataFromCluster;
                }
            }

            return default;
        }

        private Task TrackProgressAsync(object data, CancellationToken cancellationToken = default)
        {
            string resource = $"v1/kv/cronus/{_jobName}?acquire={_sessionId}";
            StringContent content = GetJsonRequestBody(data);

            return _client.PutAsync(resource, content, cancellationToken);
        }

        private async Task<bool> BecomeKingAsync(object data, CancellationToken cancellationToken = default)
        {
            string sessionId = await RequestSessionAsync(cancellationToken).ConfigureAwait(false);

            string resource = $"v1/kv/cronus/{_jobName}?acquire={sessionId}";
            StringContent content = GetJsonRequestBody(data);

            HttpResponseMessage response = await _client.PutAsync(resource, content, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                bool isSuccess = await ParseResponse<bool>(response).ConfigureAwait(false);
                if (isSuccess)
                    _sessionId = sessionId;
            }

            return IAmKing;
        }

        private async Task KingIsDead()
        {
            if (string.IsNullOrEmpty(_sessionId) == false)
            {
                string resource = $"v1/kv/cronus/{_jobName}?release={_sessionId}";
                var data = await GetJobDataAsync<object>().ConfigureAwait(false);
                StringContent content = GetJsonRequestBody(data);
                await _client.PutAsync(resource, content).ConfigureAwait(false);
            }
        }

        private async Task<T> ParseResponse<T>(HttpResponseMessage response) where T : new()
        {
            if (response.IsSuccessStatusCode)
            {
                string stringContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                T result = JsonConvert.DeserializeObject<T>(stringContent, settings);

                return result;
            }

            return new T();
        }

        private StringContent GetJsonRequestBody(object bodyContent)
        {
            string jsonBody = JsonConvert.SerializeObject(bodyContent, bodyContent.GetType(), Formatting.None, settings);

            StringContent stringContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            return stringContent;
        }
    }
}

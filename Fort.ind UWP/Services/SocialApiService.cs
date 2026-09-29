using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Web.Http;

namespace Fort.ind_UWP
{
    public enum SocialApiStatus
    {
        Ok,
        PermissionDenied,
        TokenRejected,
        Failed
    }

    public sealed class SocialApiResult<T>
    {
        private SocialApiResult()
        {
        }

        public SocialApiStatus Status { get; private set; }

        public T Value { get; private set; }

        public static SocialApiResult<T> Succeeded(T value)
        {
            return new SocialApiResult<T> { Status = SocialApiStatus.Ok, Value = value };
        }

        public static SocialApiResult<T> Failed(SocialApiStatus status)
        {
            return new SocialApiResult<T> { Status = status };
        }
    }

    public static class SocialApiService
    {
        private const string PermissionDeniedCode = "PERMISSION_DENIED";

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

        private static readonly Lazy<HttpClient> s_client = new Lazy<HttpClient>(CreateClient);

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();

            var version = AppConstants.AppVersionDisplay;
            int space = version.IndexOf(' ');
            if (space > 0)
            {
                version = version.Substring(0, space);
            }

            if (!client.DefaultRequestHeaders.UserAgent.TryParseAdd("Fort.ind/" + version))
            {
                client.DefaultRequestHeaders.UserAgent.TryParseAdd("Fort.ind");
            }

            return client;
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNotification>>> GetNotificationsAsync(
            string token, string untilId, int limit, bool markAsRead, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("limit", JsonValue.CreateNumberValue(limit));
            body.Add("markAsRead", JsonValue.CreateBooleanValue(markAsRead));
            if (!string.IsNullOrEmpty(untilId))
            {
                body.Add("untilId", JsonValue.CreateStringValue(untilId));
            }

            var response = await PostAsync("i/notifications", token, body, cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<IReadOnlyList<SocialNotification>>.Failed(response.Status);
            }

            if (response.Value.ValueType != JsonValueType.Array)
            {
                return SocialApiResult<IReadOnlyList<SocialNotification>>.Failed(SocialApiStatus.Failed);
            }

            return SocialApiResult<IReadOnlyList<SocialNotification>>.Succeeded(
                SocialNotification.ListFromJson(response.Value.GetArray()));
        }

        public static async Task<SocialApiResult<IReadOnlyList<SocialNote>>> GetMentionsAsync(
            string token, string untilId, int limit, CancellationToken cancellationToken)
        {
            JsonObject body = new JsonObject();
            body.Add("limit", JsonValue.CreateNumberValue(limit));
            if (!string.IsNullOrEmpty(untilId))
            {
                body.Add("untilId", JsonValue.CreateStringValue(untilId));
            }

            var response = await PostAsync("notes/mentions", token, body, cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<IReadOnlyList<SocialNote>>.Failed(response.Status);
            }

            if (response.Value.ValueType != JsonValueType.Array)
            {
                return SocialApiResult<IReadOnlyList<SocialNote>>.Failed(SocialApiStatus.Failed);
            }

            return SocialApiResult<IReadOnlyList<SocialNote>>.Succeeded(
                SocialNote.ListFromJson(response.Value.GetArray()));
        }

        public static async Task<SocialApiResult<int>> GetUnreadCountAsync(string token, CancellationToken cancellationToken)
        {
            var response = await PostAsync("i", token, new JsonObject(), cancellationToken);
            if (response.Status != SocialApiStatus.Ok)
            {
                return SocialApiResult<int>.Failed(response.Status);
            }

            if (response.Value.ValueType != JsonValueType.Object)
            {
                return SocialApiResult<int>.Failed(SocialApiStatus.Failed);
            }

            var me = response.Value.GetObject();
            if (!me.ContainsKey("unreadNotificationsCount")) return SocialApiResult<int>.Failed(SocialApiStatus.Failed);

            var value = me.GetNamedValue("unreadNotificationsCount");
            if (value.ValueType != JsonValueType.Number) return SocialApiResult<int>.Failed(SocialApiStatus.Failed);

            var number = value.GetNumber();
            if (double.IsNaN(number) || number < 0) return SocialApiResult<int>.Failed(SocialApiStatus.Failed);

            return SocialApiResult<int>.Succeeded(number > int.MaxValue ? int.MaxValue : (int)number);
        }

        private static async Task<SocialApiResult<IJsonValue>> PostAsync(string endpoint, string token, JsonObject body,
                                                                        CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(token)) return SocialApiResult<IJsonValue>.Failed(SocialApiStatus.TokenRejected);

            try
            {
                body.SetNamedValue("i", JsonValue.CreateStringValue(token));
                Uri uri = new Uri($"https://{MisskeyAuthService.InstanceHost}/api/{endpoint}");

                using (var timeout = new CancellationTokenSource(RequestTimeout))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken))
                using (HttpStringContent content = new HttpStringContent(body.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json"))
                using (var response = await s_client.Value.PostAsync(uri, content).AsTask(linked.Token))
                {
                    var text = await response.Content.ReadAsStringAsync().AsTask(linked.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        var status = ClassifyFailure((int)response.StatusCode, text);
                        Debug.WriteLine($"SocialApiService: {endpoint} answered {(int)response.StatusCode} ({status})");
                        return SocialApiResult<IJsonValue>.Failed(status);
                    }

                    JsonValue parsed;
                    if (!JsonValue.TryParse(text, out parsed))
                    {
                        Debug.WriteLine($"SocialApiService: {endpoint} returned something that is not JSON");
                        return SocialApiResult<IJsonValue>.Failed(SocialApiStatus.Failed);
                    }

                    return SocialApiResult<IJsonValue>.Succeeded(parsed);
                }
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested) throw;

                Debug.WriteLine($"SocialApiService: {endpoint} failed - {ex.GetType().Name}: {ex.Message}");
                return SocialApiResult<IJsonValue>.Failed(SocialApiStatus.Failed);
            }
        }

        private static SocialApiStatus ClassifyFailure(int statusCode, string body)
        {
            if (statusCode == 401) return SocialApiStatus.TokenRejected;
            if (statusCode != 403) return SocialApiStatus.Failed;

            JsonObject parsed;
            if (!string.IsNullOrEmpty(body) && JsonObject.TryParse(body, out parsed))
            {
                var error = SocialJson.Object(parsed, "error");
                if (string.Equals(SocialJson.String(error, "code"), PermissionDeniedCode, StringComparison.Ordinal))
                {
                    return SocialApiStatus.PermissionDenied;
                }
            }

            return SocialApiStatus.Failed;
        }
    }
}

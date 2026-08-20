using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace CodexQuotaOverlay
{
    internal static class QuotaParser
    {
        private const int WeeklyWindowMinutes = 7 * 24 * 60;
        private const int MinimumWeeklyWindowMinutes = 6 * 24 * 60;
        private const int MaximumWeeklyWindowMinutes = 8 * 24 * 60;

        private sealed class Candidate
        {
            public string LimitId;
            public string LimitName;
            public string WindowName;
            public double UsedPercent;
            public int WindowDurationMins;
            public long ResetsAt;
        }

        public static QuotaSnapshot ParseResult(object resultObject)
        {
            IDictionary<string, object> result = AsDictionary(resultObject);
            if (result == null)
            {
                return null;
            }

            List<Candidate> candidates = new List<Candidate>();
            object multiObject;
            if (result.TryGetValue("rateLimitsByLimitId", out multiObject))
            {
                IDictionary<string, object> multi = AsDictionary(multiObject);
                if (multi != null)
                {
                    foreach (KeyValuePair<string, object> pair in multi)
                    {
                        AddBucketCandidates(candidates, pair.Value, pair.Key);
                    }
                }
            }

            object singleObject;
            if (result.TryGetValue("rateLimits", out singleObject))
            {
                AddBucketCandidates(candidates, singleObject, string.Empty);
            }

            Candidate selected = null;
            int selectedDistance = int.MaxValue;
            foreach (Candidate candidate in candidates)
            {
                if (candidate.WindowDurationMins < MinimumWeeklyWindowMinutes ||
                    candidate.WindowDurationMins > MaximumWeeklyWindowMinutes)
                {
                    continue;
                }

                int distance = Math.Abs(candidate.WindowDurationMins - WeeklyWindowMinutes);
                if (selected == null || IsBetterCandidate(candidate, distance, selected, selectedDistance))
                {
                    selected = candidate;
                    selectedDistance = distance;
                }
            }

            if (selected == null)
            {
                return null;
            }

            double remaining = Math.Max(0.0, Math.Min(100.0, 100.0 - selected.UsedPercent));
            int remainingPercent = (int)Math.Round(remaining, MidpointRounding.AwayFromZero);
            DateTimeOffset resetTime = DateTimeOffset.MinValue;
            if (selected.ResetsAt > 0)
            {
                resetTime = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(selected.ResetsAt);
            }

            return new QuotaSnapshot(
                remainingPercent,
                selected.UsedPercent,
                selected.WindowDurationMins,
                resetTime,
                selected.LimitId,
                selected.LimitName);
        }

        private static bool IsBetterCandidate(Candidate candidate, int distance, Candidate selected, int selectedDistance)
        {
            if (distance != selectedDistance)
            {
                return distance < selectedDistance;
            }

            bool candidateIsCodex = string.Equals(candidate.LimitId, "codex", StringComparison.OrdinalIgnoreCase);
            bool selectedIsCodex = string.Equals(selected.LimitId, "codex", StringComparison.OrdinalIgnoreCase);
            if (candidateIsCodex != selectedIsCodex)
            {
                return candidateIsCodex;
            }

            bool candidateIsSecondary = string.Equals(candidate.WindowName, "secondary", StringComparison.OrdinalIgnoreCase);
            bool selectedIsSecondary = string.Equals(selected.WindowName, "secondary", StringComparison.OrdinalIgnoreCase);
            if (candidateIsSecondary != selectedIsSecondary)
            {
                return candidateIsSecondary;
            }

            return false;
        }

        private static void AddBucketCandidates(List<Candidate> candidates, object bucketObject, string fallbackLimitId)
        {
            IDictionary<string, object> bucket = AsDictionary(bucketObject);
            if (bucket == null)
            {
                return;
            }

            string limitId = GetString(bucket, "limitId");
            if (string.IsNullOrEmpty(limitId))
            {
                limitId = fallbackLimitId ?? string.Empty;
            }

            string limitName = GetString(bucket, "limitName");
            AddWindowCandidate(candidates, bucket, "primary", limitId, limitName);
            AddWindowCandidate(candidates, bucket, "secondary", limitId, limitName);
        }

        private static void AddWindowCandidate(
            List<Candidate> candidates,
            IDictionary<string, object> bucket,
            string windowName,
            string limitId,
            string limitName)
        {
            object windowObject;
            if (!bucket.TryGetValue(windowName, out windowObject))
            {
                return;
            }

            IDictionary<string, object> window = AsDictionary(windowObject);
            if (window == null)
            {
                return;
            }

            double usedPercent;
            int duration;
            if (!TryGetDouble(window, "usedPercent", out usedPercent) ||
                !TryGetInt(window, "windowDurationMins", out duration))
            {
                return;
            }

            long resetsAt;
            TryGetLong(window, "resetsAt", out resetsAt);
            candidates.Add(new Candidate
            {
                LimitId = limitId,
                LimitName = limitName,
                WindowName = windowName,
                UsedPercent = usedPercent,
                WindowDurationMins = duration,
                ResetsAt = resetsAt
            });
        }

        private static IDictionary<string, object> AsDictionary(object value)
        {
            return value as IDictionary<string, object>;
        }

        private static string GetString(IDictionary<string, object> dictionary, string key)
        {
            object value;
            if (!dictionary.TryGetValue(key, out value) || value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static bool TryGetDouble(IDictionary<string, object> dictionary, string key, out double result)
        {
            result = 0;
            object value;
            if (!dictionary.TryGetValue(key, out value) || value == null)
            {
                return false;
            }

            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryGetInt(IDictionary<string, object> dictionary, string key, out int result)
        {
            result = 0;
            object value;
            if (!dictionary.TryGetValue(key, out value) || value == null)
            {
                return false;
            }

            try
            {
                result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryGetLong(IDictionary<string, object> dictionary, string key, out long result)
        {
            result = 0;
            object value;
            if (!dictionary.TryGetValue(key, out value) || value == null)
            {
                return false;
            }

            try
            {
                result = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool RunSelfTest(out string result)
        {
            const string fixture = "{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":12,\"windowDurationMins\":300,\"resetsAt\":1730947200},\"secondary\":{\"usedPercent\":31,\"windowDurationMins\":10080,\"resetsAt\":1730950800}}}";
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            QuotaSnapshot snapshot = ParseResult(serializer.DeserializeObject(fixture));
            bool success = snapshot != null &&
                           snapshot.RemainingPercent == 69 &&
                           snapshot.WindowDurationMins == WeeklyWindowMinutes &&
                           Math.Abs(snapshot.UsedPercent - 31.0) < 0.001;
            result = success
                ? "{\"ok\":true,\"test\":\"weekly-window-selection\",\"remainingPercent\":69}"
                : "{\"ok\":false,\"test\":\"weekly-window-selection\"}";
            return success;
        }
    }
}

using System;

namespace CodexQuotaOverlay
{
    internal sealed class QuotaSnapshot
    {
        public int RemainingPercent { get; private set; }
        public double UsedPercent { get; private set; }
        public int WindowDurationMins { get; private set; }
        public DateTimeOffset ResetsAtUtc { get; private set; }
        public string LimitId { get; private set; }
        public string LimitName { get; private set; }

        public QuotaSnapshot(
            int remainingPercent,
            double usedPercent,
            int windowDurationMins,
            DateTimeOffset resetsAtUtc,
            string limitId,
            string limitName)
        {
            RemainingPercent = remainingPercent;
            UsedPercent = usedPercent;
            WindowDurationMins = windowDurationMins;
            ResetsAtUtc = resetsAtUtc;
            LimitId = limitId ?? string.Empty;
            LimitName = limitName ?? string.Empty;
        }
    }

    internal sealed class QuotaEventArgs : EventArgs
    {
        public QuotaSnapshot Snapshot { get; private set; }

        public QuotaEventArgs(QuotaSnapshot snapshot)
        {
            Snapshot = snapshot;
        }
    }

    internal sealed class StatusEventArgs : EventArgs
    {
        public string Status { get; private set; }

        public StatusEventArgs(string status)
        {
            Status = status ?? string.Empty;
        }
    }
}

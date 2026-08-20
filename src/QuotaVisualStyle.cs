using System.Drawing;

namespace CodexQuotaOverlay
{
    internal static class QuotaVisualStyle
    {
        public static Color Surface
        {
            get { return Color.FromArgb(255, 254, 253); }
        }

        public static Color SurfaceHover
        {
            get { return Color.FromArgb(248, 249, 246); }
        }

        public static Color SurfacePressed
        {
            get { return Color.FromArgb(244, 245, 242); }
        }

        public static Color Border
        {
            get { return Color.FromArgb(222, 224, 218); }
        }

        public static Color BorderHover
        {
            get { return Color.FromArgb(207, 211, 203); }
        }

        public static Color TextPrimary
        {
            get { return Color.FromArgb(47, 50, 45); }
        }

        public static Color TextSecondary
        {
            get { return Color.FromArgb(112, 116, 108); }
        }

        public static Color Separator
        {
            get { return Color.FromArgb(235, 237, 232); }
        }

        public static Color GetPillSurface(QuotaSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return Surface;
            }

            if (snapshot.RemainingPercent <= 20)
            {
                return Color.FromArgb(255, 244, 242);
            }

            if (snapshot.RemainingPercent <= 40)
            {
                return Color.FromArgb(255, 248, 235);
            }

            return Color.FromArgb(244, 249, 242);
        }

        public static Color GetPillHoverSurface(QuotaSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return SurfaceHover;
            }

            if (snapshot.RemainingPercent <= 20)
            {
                return Color.FromArgb(253, 238, 235);
            }

            if (snapshot.RemainingPercent <= 40)
            {
                return Color.FromArgb(253, 244, 226);
            }

            return Color.FromArgb(239, 246, 237);
        }

        public static Color GetPillPressedSurface(QuotaSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return SurfacePressed;
            }

            if (snapshot.RemainingPercent <= 20)
            {
                return Color.FromArgb(249, 231, 227);
            }

            if (snapshot.RemainingPercent <= 40)
            {
                return Color.FromArgb(249, 237, 216);
            }

            return Color.FromArgb(233, 241, 230);
        }

        public static Color GetPillBorder(QuotaSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return Border;
            }

            if (snapshot.RemainingPercent <= 20)
            {
                return Color.FromArgb(238, 205, 200);
            }

            if (snapshot.RemainingPercent <= 40)
            {
                return Color.FromArgb(237, 216, 177);
            }

            return Color.FromArgb(211, 226, 207);
        }

        public static Color GetPillText(QuotaSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return TextPrimary;
            }

            if (snapshot.RemainingPercent <= 20)
            {
                return Color.FromArgb(133, 76, 72);
            }

            if (snapshot.RemainingPercent <= 40)
            {
                return Color.FromArgb(122, 98, 56);
            }

            return Color.FromArgb(77, 102, 80);
        }
    }
}

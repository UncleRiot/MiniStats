using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MiniStats
{
    public class SplashForm : Form
    {
        private readonly System.Windows.Forms.Timer timer;
        private readonly Bitmap splashBitmap;

        private int phase;
        private int tick;

        private const int FadeInTicks = 60;
        private const int HoldTicks = 140;
        private const int FadeOutTicks = 60;

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = false;

            using Stream? stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("MiniStats.Ressources.ministats_splash.png");

            splashBitmap = stream != null
                ? new Bitmap(stream)
                : new Bitmap(360, 100, PixelFormat.Format32bppArgb);

            // Splash-Screen size and multiplicator
            double scale = 0.3;

            int width = (int)(splashBitmap.Width * scale);
            int height = (int)(splashBitmap.Height * scale);

            ClientSize = new Size(width, height);

            BackColor = Color.Black;
            TransparencyKey = Color.Empty;
            Opacity = 1.0;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 15;
            timer.Tick += OnTick;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams createParams = base.CreateParams;
                createParams.ExStyle |= NativeMethods.WS_EX_LAYERED;
                return createParams;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            CenterToScreen();
            ApplySplashBitmap(0);
            timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                splashBitmap.Dispose();
            }

            base.Dispose(disposing);
        }

        private void OnTick(object? sender, EventArgs e)
        {
            tick++;

            if (phase == 0)
            {
                byte alpha = (byte)Math.Min(255, (255 * tick) / FadeInTicks);
                ApplySplashBitmap(alpha);

                if (tick >= FadeInTicks)
                {
                    phase = 1;
                    tick = 0;
                }

                return;
            }

            if (phase == 1)
            {
                if (tick >= HoldTicks)
                {
                    phase = 2;
                    tick = 0;
                }

                return;
            }

            byte fadeOutAlpha = (byte)Math.Max(0, 255 - ((255 * tick) / FadeOutTicks));
            ApplySplashBitmap(fadeOutAlpha);

            if (tick >= FadeOutTicks)
            {
                timer.Stop();
                Close();
            }
        }

        private void ApplySplashBitmap(byte alpha)
        {
            if (!IsHandleCreated)
            {
                return;
            }

            using Bitmap scaledBitmap = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(scaledBitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(splashBitmap, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
            }

            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                hBitmap = scaledBitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

                NativeMethods.SIZE size = new NativeMethods.SIZE(ClientSize.Width, ClientSize.Height);
                NativeMethods.POINT sourcePoint = new NativeMethods.POINT(0, 0);
                NativeMethods.POINT topPos = new NativeMethods.POINT(Left, Top);

                NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION
                {
                    BlendOp = NativeMethods.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = alpha,
                    AlphaFormat = NativeMethods.AC_SRC_ALPHA
                };

                NativeMethods.UpdateLayeredWindow(
                    Handle,
                    screenDc,
                    ref topPos,
                    ref size,
                    memDc,
                    ref sourcePoint,
                    0,
                    ref blend,
                    NativeMethods.ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memDc, oldBitmap);
                }

                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(hBitmap);
                }

                NativeMethods.DeleteDC(memDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static class NativeMethods
        {
            public const int WS_EX_LAYERED = 0x00080000;
            public const int ULW_ALPHA = 0x00000002;
            public const byte AC_SRC_OVER = 0x00;
            public const byte AC_SRC_ALPHA = 0x01;

            [StructLayout(LayoutKind.Sequential)]
            public struct POINT
            {
                public int X;
                public int Y;

                public POINT(int x, int y)
                {
                    X = x;
                    Y = y;
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct SIZE
            {
                public int cx;
                public int cy;

                public SIZE(int cx, int cy)
                {
                    this.cx = cx;
                    this.cy = cy;
                }
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct BLENDFUNCTION
            {
                public byte BlendOp;
                public byte BlendFlags;
                public byte SourceConstantAlpha;
                public byte AlphaFormat;
            }

            [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
            public static extern IntPtr GetDC(IntPtr hWnd);

            [DllImport("user32.dll", ExactSpelling = true)]
            public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

            [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
            public static extern IntPtr CreateCompatibleDC(IntPtr hDc);

            [DllImport("gdi32.dll", ExactSpelling = true)]
            public static extern bool DeleteDC(IntPtr hDc);

            [DllImport("gdi32.dll", ExactSpelling = true)]
            public static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

            [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
            public static extern bool DeleteObject(IntPtr hObject);

            [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
            public static extern bool UpdateLayeredWindow(
                IntPtr hwnd,
                IntPtr hdcDst,
                ref POINT pptDst,
                ref SIZE psize,
                IntPtr hdcSrc,
                ref POINT pprSrc,
                int crKey,
                ref BLENDFUNCTION pblend,
                int dwFlags);
        }
    }
}
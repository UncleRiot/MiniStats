using System;
using System.Windows.Forms;

namespace MiniStats
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ApplicationConfiguration.Initialize();

            using (SplashForm splash = new SplashForm())
            {
                splash.Show();

                while (splash.Visible)
                {
                    Application.DoEvents();
                }
            }

            Application.Run(new Overlay());
        }
    }
}
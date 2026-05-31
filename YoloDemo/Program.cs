using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YoloDemo
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleOutputCP(uint codePage);

        [STAThread]
        private static void Main()
        {
            AllocConsole();
            SetConsoleOutputCP(65001);
            Console.Title = "YOLO Pose Data";
            Console.WriteLine("[YOLO Pose Demo] Console data output started.");
            Console.WriteLine("[YOLO Pose Demo] Press Esc in the video window to exit.");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new FrmMain());
        }
    }
}

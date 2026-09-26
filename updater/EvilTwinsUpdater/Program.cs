namespace EvilTwinsUpdater;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--preview")
        {
            ApplicationConfiguration.Initialize();
            using var form = new MainForm(preview: true);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            bitmap.Save(Path.GetFullPath(args[1]));
            form.Hide();
            return;
        }
        using var instance = new Mutex(true, "Local\\EvilTwinsUpdater", out var first);
        if (!first) { MessageBox.Show("Evil Twins Updater is already running. Look for its icon in the system tray."); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

// Claudiometer - affiche le quota Claude Code (session 5h + semaine) dans la taskbar, a cote de l'horloge.
// Compile avec build.ps1 (csc.exe de .NET Framework, rien a installer).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Claudiometer")]
[assembly: AssemblyDescription("Quota Claude Code (session 5 h + semaine) dans la taskbar Windows")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc p, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint x, out uint y);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);
}

class Config
{
    public string Monitor = "secondary", Layout = "stack", LabelSession = "5h", LabelWeek = "7j",
                  FontName = "Segoe UI", TextColor = "auto";
    public int RefreshMinutes = 5, RightOffset = 150, WarnPercent = 30, CritPercent = 10, LabelOpacity = 65;
    public float FontSize = 9;
    public bool ShowRemaining = true, RightAuto = true, TrayIcon = false;

    const string Defaults =
"; Claudiometer - ce fichier est relu automatiquement (pas besoin de relancer).\r\n" +
"; Monitor : primary | secondary | 1, 2, 3... (numero d'ecran)\r\n" +
"Monitor=secondary\r\n" +
"; Intervalle de rafraichissement en minutes (min 1)\r\n" +
"RefreshMinutes=5\r\n" +
"; true = quota restant, false = quota consomme\r\n" +
"ShowRemaining=true\r\n" +
"; stack = 2 lignes, line = 1 ligne\r\n" +
"Layout=stack\r\n" +
"LabelSession=5h\r\n" +
"LabelWeek=7j\r\n" +
"; Opacite des libelles (5h / 7j) en % : 100 = pleine, plus bas = plus discret\r\n" +
"LabelOpacity=65\r\n" +
"FontName=Segoe UI\r\n" +
"FontSize=9\r\n" +
"; auto = colle le widget a gauche de l'horloge (detection automatique).\r\n" +
"; Ou un nombre : distance (px a 100%) entre le bord droit de la taskbar et le widget.\r\n" +
"RightOffset=auto\r\n" +
"; Seuils sur le quota RESTANT : orange <= Warn, rouge <= Crit\r\n" +
"WarnPercent=30\r\n" +
"CritPercent=10\r\n" +
"; auto (suit le theme Windows) ou #RRGGBB\r\n" +
"TextColor=auto\r\n" +
"; Icone dans la zone de notification : % de la session + fine barre pour la semaine. Clic = panneau detaille.\r\n" +
"TrayIcon=false\r\n";

    public static string Path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
    public DateTime Stamp;

    // Modifie (ou ajoute) une cle dans config.ini ; le widget relit le fichier tout seul.
    public static void Set(string key, string value)
    {
        try
        {
            var lines = new List<string>(File.Exists(Path) ? File.ReadAllLines(Path) : new string[0]);
            bool done = false;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) { lines[i] = key + "=" + value; done = true; }
            if (!done) lines.Add(key + "=" + value);
            File.WriteAllLines(Path, lines.ToArray(), Encoding.UTF8);
        }
        catch { }
    }

    public static Config Load()
    {
        var c = new Config();
        try
        {
            if (!File.Exists(Path)) File.WriteAllText(Path, Defaults, Encoding.UTF8);
            c.Stamp = File.GetLastWriteTimeUtc(Path);
            foreach (var raw in File.ReadAllLines(Path))
            {
                var l = raw.Trim();
                if (l.Length == 0 || l[0] == ';' || l[0] == '#') continue;
                int i = l.IndexOf('=');
                if (i < 1) continue;
                string k = l.Substring(0, i).Trim().ToLowerInvariant(), v = l.Substring(i + 1).Trim();
                switch (k)
                {
                    case "monitor": c.Monitor = v.ToLowerInvariant(); break;
                    case "layout": c.Layout = v.ToLowerInvariant(); break;
                    case "labelsession": c.LabelSession = v; break;
                    case "labelweek": c.LabelWeek = v; break;
                    case "labelopacity": int.TryParse(v, out c.LabelOpacity); break;
                    case "fontname": c.FontName = v; break;
                    case "textcolor": c.TextColor = v; break;
                    case "trayicon": c.TrayIcon = !v.Equals("false", StringComparison.OrdinalIgnoreCase) && v != "0"; break;
                    case "refreshminutes": int.TryParse(v, out c.RefreshMinutes); break;
                    case "rightoffset":
                        c.RightAuto = v.Equals("auto", StringComparison.OrdinalIgnoreCase);
                        if (!c.RightAuto) int.TryParse(v, out c.RightOffset);
                        break;
                    case "warnpercent": int.TryParse(v, out c.WarnPercent); break;
                    case "critpercent": int.TryParse(v, out c.CritPercent); break;
                    case "fontsize": float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out c.FontSize); break;
                    case "showremaining": c.ShowRemaining = !v.Equals("false", StringComparison.OrdinalIgnoreCase) && v != "0"; break;
                }
            }
        }
        catch { }
        if (c.RefreshMinutes < 1) c.RefreshMinutes = 1;
        if (c.FontSize < 5) c.FontSize = 9;
        return c;
    }
}

// Echec d'actualisation : Auth = identifiants a renouveler via Claude Code (inutile de reessayer vite),
// RetryAfterSec = delai demande par le serveur (en-tete Retry-After, 0 si absent).
class FetchException : Exception
{
    public bool Auth; public int RetryAfterSec;
    public FetchException(string msg, bool auth, int retryAfter) : base(msg) { Auth = auth; RetryAfterSec = retryAfter; }
}

class Limit
{
    public bool Has; public double Used; public DateTimeOffset Reset;
    public double UsedNow() { return (Has && Reset < DateTimeOffset.Now) ? 0 : Used; } // fenetre deja reinitialisee
}

// Rendu du menu contextuel : suit le theme Windows, survol arrondi, pictos Segoe Fluent, coche couleur Claude.
class MenuRenderer : ToolStripProfessionalRenderer
{
    readonly Color bg, fg, sub, hover, border, accent = Color.FromArgb(217, 119, 87);
    readonly Font iconFont;
    public readonly Dictionary<ToolStripItem, string> Glyphs = new Dictionary<ToolStripItem, string>();

    public MenuRenderer(bool light)
    {
        RoundedEdges = false;
        bg = light ? Color.FromArgb(249, 249, 249) : Color.FromArgb(43, 43, 43);
        fg = light ? Color.FromArgb(25, 25, 25) : Color.White;
        sub = light ? Color.FromArgb(105, 105, 105) : Color.FromArgb(170, 170, 170);
        hover = light ? Color.FromArgb(232, 232, 232) : Color.FromArgb(62, 62, 62);
        border = light ? Color.FromArgb(215, 215, 215) : Color.FromArgb(72, 72, 72);
        string fam = "Segoe MDL2 Assets";
        foreach (var f in FontFamily.Families) if (f.Name == "Segoe Fluent Icons") { fam = f.Name; break; }
        iconFont = new Font(fam, 10f);
    }
    public Color Fg { get { return fg; } }
    public Color Bg { get { return bg; } }

    static GraphicsPath Round(Rectangle r, int d)
    {
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.AffectedBounds); }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using (var p = new Pen(border)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using (var path = Round(r, 8)) using (var b = new SolidBrush(hover)) g.FillPath(b, path);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using (var p = new Pen(border)) e.Graphics.DrawLine(p, 12, e.Item.Height / 2, e.Item.Width - 12, e.Item.Height / 2);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = fg;
        // centrage vertical sur toute la hauteur de l'element (meme ligne que le picto)
        e.TextRectangle = new Rectangle(e.TextRectangle.X, 0, e.TextRectangle.Width, e.Item.Height);
        e.TextFormat = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        base.OnRenderItemText(e);
    }
    // le picto est dessine dans la marge image (reservee par une image transparente sur chaque element)
    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        string gl;
        if (!Glyphs.TryGetValue(e.Item, out gl)) return;
        var on = e.Item is ToolStripMenuItem && ((ToolStripMenuItem)e.Item).Checked;
        var r = e.ImageRectangle;
        // les glyphes Segoe Fluent ont un point de base plus bas que le texte : remonte de 1 px
        TextRenderer.DrawText(e.Graphics, gl, iconFont, new Rectangle(r.X - 2, -1, r.Width + 4, e.Item.Height), on ? accent : sub,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = sub; base.OnRenderArrow(e); }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        if (Glyphs.ContainsKey(e.Item)) return; // picto deja colore selon l'etat
        var r = e.ImageRectangle;
        TextRenderer.DrawText(e.Graphics, "", iconFont, new Rectangle(r.X - 2, 0, r.Width + 4, e.Item.Height), accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

class Widget : Form
{
    const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    Config cfg = Config.Load();
    Limit session = new Limit(), week = new Limit();
    bool haveData, stale; string error; DateTime lastOk;
    int w = 10, h = 10;
    readonly ToolTip tip = new ToolTip();
    readonly System.Windows.Forms.Timer fast = new System.Windows.Forms.Timer(), slow = new System.Windows.Forms.Timer();
    bool fetching;
    ToolStripMenuItem startupItem;

    public Widget()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        tip.AutoPopDelay = 30000; tip.InitialDelay = 250; tip.ShowAlways = true;
        MouseUp += delegate(object snd, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { tip.Hide(this); TogglePopup(new Point(Left + w / 2, Top + h / 2)); }
        };

        var menu = new ContextMenuStrip();
        menu.Font = new Font("Segoe UI", 9.5f);
        menu.ShowImageMargin = true; menu.ShowCheckMargin = false;
        menu.Padding = new Padding(0, 4, 0, 4);
        var refreshItem = new ToolStripMenuItem("Actualiser maintenant", null, delegate { Refresh_(); });
        menu.Items.Add(refreshItem);
        var intervalMenu = new ToolStripMenuItem("Intervalle de mise à jour");
        intervalMenu.DropDown.Font = menu.Font;
        ((ToolStripDropDownMenu)intervalMenu.DropDown).ShowImageMargin = false;
        ((ToolStripDropDownMenu)intervalMenu.DropDown).ShowCheckMargin = true;
        intervalMenu.DropDown.Padding = new Padding(0, 4, 0, 4);
        foreach (int m in new[] { 1, 2, 5, 10, 15, 30 })
        {
            int mm = m;
            var it = new ToolStripMenuItem(mm + " min", null, delegate { Config.Set("RefreshMinutes", mm.ToString()); });
            it.Tag = mm; intervalMenu.DropDownItems.Add(it);
        }
        menu.Items.Add(intervalMenu);
        menu.Items.Add("-");

        // sous-menus a choix unique (coche sur la valeur courante) : disposition et ecran
        Func<string, string, string[][], ToolStripMenuItem> choiceMenu = delegate(string text, string key, string[][] choices)
        {
            var parent = new ToolStripMenuItem(text);
            parent.DropDown.Font = menu.Font;
            var dd = (ToolStripDropDownMenu)parent.DropDown;
            dd.ShowImageMargin = false; dd.ShowCheckMargin = true; dd.Padding = new Padding(0, 4, 0, 4);
            foreach (var c in choices)
            {
                string val = c[0];
                var it = new ToolStripMenuItem(c[1], null, delegate { Config.Set(key, val); });
                it.Tag = val; it.Padding = new Padding(0, 6, 10, 6); parent.DropDownItems.Add(it);
            }
            RoundCorners(parent.DropDown);
            return parent;
        };
        var showItem = new ToolStripMenuItem("Afficher le quota restant", null, delegate { Config.Set("ShowRemaining", cfg.ShowRemaining ? "false" : "true"); });
        menu.Items.Add(showItem);
        var layoutMenu = choiceMenu("Disposition", "Layout", new[] { new[] { "stack", "Deux lignes" }, new[] { "line", "Une ligne" } });
        menu.Items.Add(layoutMenu);
        var monitorMenu = choiceMenu("Écran", "Monitor", new[] { new[] { "primary", "Principal" }, new[] { "secondary", "Secondaire" } });
        menu.Items.Add(monitorMenu);
        var trayItem = new ToolStripMenuItem("Icône de notification", null, delegate { Config.Set("TrayIcon", cfg.TrayIcon ? "false" : "true"); });
        menu.Items.Add(trayItem);
        menu.Items.Add("-");
        startupItem =new ToolStripMenuItem("Lancer avec Windows", null, delegate { ToggleStartup(); });
        menu.Items.Add(startupItem);
        var cfgItem = new ToolStripMenuItem("Modifier la config...", null, delegate { try { Process.Start("notepad.exe", "\"" + Config.Path + "\""); } catch { } });
        menu.Items.Add(cfgItem);
        menu.Items.Add("-");
        var quitItem = new ToolStripMenuItem("Quitter", null, delegate { Close(); });
        menu.Items.Add(quitItem);
        // gouttiere gauche pour les pictos ; les elements de la liste deroulante gardent la coche
        var blank = new Bitmap(20, 20);
        foreach (ToolStripItem it in menu.Items)
            if (it is ToolStripMenuItem) { it.Padding = new Padding(4, 6, 12, 6); it.Image = blank; }
        foreach (ToolStripMenuItem it in intervalMenu.DropDownItems) it.Padding = new Padding(0, 6, 10, 6);
        RoundCorners(menu); RoundCorners(intervalMenu.DropDown);
        // Le widget est WS_EX_NOACTIVATE : le menu ne perd jamais le focus tout seul.
        // On surveille donc les clics souris et on ferme si le clic tombe hors du menu / sous-menu.
        var clickWatch = new System.Windows.Forms.Timer(); clickWatch.Interval = 40;
        bool armed = false;
        clickWatch.Tick += delegate
        {
            bool down = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x02) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x04) & 0x8000) != 0;
            if (!down) { armed = true; return; }   // le clic droit d'ouverture doit d'abord etre relache
            if (!armed) return;
            var p = Cursor.Position;
            if (menu.Bounds.Contains(p)) return;
            foreach (ToolStripMenuItem mi in menu.Items.OfType<ToolStripMenuItem>())
                if (mi.HasDropDownItems && mi.DropDown.Visible && mi.DropDown.Bounds.Contains(p)) return;
            menu.Close();
        };
        menu.Opened += delegate { armed = false; clickWatch.Start(); };
        menu.Closed += delegate { clickWatch.Stop(); };
        menu.Opening += delegate
        {
            var rd = new MenuRenderer(LightTheme());
            rd.Glyphs[refreshItem] = ""; rd.Glyphs[intervalMenu] = ""; rd.Glyphs[startupItem] = "";
            rd.Glyphs[cfgItem] = ""; rd.Glyphs[quitItem] = "";
            rd.Glyphs[showItem] = ""; rd.Glyphs[layoutMenu] = ""; rd.Glyphs[monitorMenu] = ""; rd.Glyphs[trayItem] = "";
            menu.Renderer = rd; intervalMenu.DropDown.Renderer = rd; layoutMenu.DropDown.Renderer = rd; monitorMenu.DropDown.Renderer = rd;
            startupItem.Checked = IsStartup();
            showItem.Checked = cfg.ShowRemaining; trayItem.Checked = cfg.TrayIcon;
            foreach (ToolStripMenuItem it in layoutMenu.DropDownItems) it.Checked = (string)it.Tag == cfg.Layout;
            foreach (ToolStripMenuItem it in monitorMenu.DropDownItems) it.Checked = (string)it.Tag == cfg.Monitor;
            foreach (ToolStripMenuItem it in intervalMenu.DropDownItems) it.Checked = (int)it.Tag == cfg.RefreshMinutes;
        };
        ContextMenuStrip = menu;

        fast.Interval = 2000; fast.Tick += delegate { Tick(); };
        slow.Tick += delegate { Refresh_(); };
    }

    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST; return p; }
    }
    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Render(); Reposition();
        fast.Start();
        ApplyInterval();
        Refresh_();
    }

    // coins arrondis Windows 11 (DWMWA_WINDOW_CORNER_PREFERENCE = round) ; ignore sur Windows 10
    static void RoundCorners(ToolStripDropDown d)
    {
        d.HandleCreated += delegate { try { int v = 2; Native.DwmSetWindowAttribute(d.Handle, 33, ref v, 4); } catch { } };
    }

    void ApplyInterval() { slow.Stop(); slow.Interval = cfg.RefreshMinutes * 60 * 1000; slow.Start(); }

    // ---------- demarrage Windows ----------
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    bool IsStartup() { try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("Claudiometer") != null; } catch { return false; } }
    void ToggleStartup()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (IsStartup()) k.DeleteValue("Claudiometer", false);
                else k.SetValue("Claudiometer", "\"" + Application.ExecutablePath + "\"");
            }
        }
        catch { }
    }

    // ---------- donnees ----------
    void Refresh_()
    {
        if (fetching) return;
        fetching = true;
        ThreadPool.QueueUserWorkItem(delegate
        {
            string json = null, err = null; FetchException fe = null;
            try { json = Fetch(); }
            catch (Exception ex) { err = ex.Message; fe = ex as FetchException; }
            try { BeginInvoke(new Action(delegate { Apply(json, err, fe); })); } catch { }
        });
    }

    static string CredentialsPath()
    {
        string dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return Path.Combine(dir, ".credentials.json");
    }

    const string ReloginMsg = "session expirée : relancez Claude Code pour vous reconnecter";

    static string Fetch()
    {
        string file = CredentialsPath();
        if (!File.Exists(file)) throw new FetchException("credentials Claude Code introuvables : connectez-vous dans Claude Code", true, 0);
        string txt = File.ReadAllText(file);
        int sec = txt.IndexOf("\"claudeAiOauth\"", StringComparison.Ordinal);
        if (sec < 0) throw new FetchException("pas de session claude.ai dans les credentials : connectez-vous dans Claude Code", true, 0);
        txt = txt.Substring(sec); // evite le "accessToken" de la section mcpOAuth
        var tok = Regex.Match(txt, "\"accessToken\"\\s*:\\s*\"([^\"]+)\"");
        if (!tok.Success) throw new FetchException("token OAuth absent : connectez-vous dans Claude Code", true, 0);
        var exp = Regex.Match(txt, "\"expiresAt\"\\s*:\\s*(\\d+)");
        if (exp.Success && long.Parse(exp.Groups[1].Value) < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            throw new FetchException(ReloginMsg, true, 0);

        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
        var req = (HttpWebRequest)WebRequest.Create("https://api.anthropic.com/api/oauth/usage");
        req.Headers["Authorization"] = "Bearer " + tok.Groups[1].Value;
        req.Headers["anthropic-beta"] = "oauth-2025-04-20";
        req.UserAgent = "Claudiometer/" + Program.Version;
        req.Timeout = 15000;
        try
        {
            using (var resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream())) return sr.ReadToEnd();
        }
        catch (WebException we)
        {
            var hr = we.Response as HttpWebResponse;
            if (hr == null) throw new FetchException(we.Message, false, 0);
            int code = (int)hr.StatusCode, retry = 0;
            int.TryParse(hr.Headers["Retry-After"], out retry);
            if (code == 401 || code == 403) throw new FetchException(ReloginMsg, true, 0);
            if (code == 429) throw new FetchException("HTTP 429 : limite de requêtes", false, retry);
            throw new FetchException("HTTP " + code, false, retry);
        }
    }

    static Limit Parse(string json, string key)
    {
        var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\\{\\s*\"utilization\"\\s*:\\s*([0-9.]+)\\s*,\\s*\"resets_at\"\\s*:\\s*\"([^\"]+)\"");
        var l = new Limit();
        if (m.Success)
        {
            l.Has = true;
            l.Used = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            l.Reset = DateTimeOffset.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        return l;
    }

    // Echecs consecutifs : on espace les essais (x2 a chaque echec, plafond 30 min, ou Retry-After s'il est plus long)
    // pour ne pas aggraver un 429. Une erreur d'identifiants ne declenche pas de backoff : on repart quand
    // .credentials.json change (voir Tick), sinon a l'intervalle normal.
    int failures;

    void Apply(string json, string err, FetchException fe)
    {
        fetching = false;
        if (json != null)
        {
            session = Parse(json, "five_hour"); week = Parse(json, "seven_day");
            if (!session.Has && !week.Has)
            {
                err = "réponse inattendue de l'API (format modifié ?) : voir last-error.txt";
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-error.txt"), DateTime.Now + " " + json); } catch { }
                json = null;
            }
        }
        if (json != null)
        {
            haveData = true; stale = false; error = null; lastOk = DateTime.Now;
            if (failures > 0) { failures = 0; ApplyInterval(); }
        }
        else
        {
            failures++;
            double baseMs = cfg.RefreshMinutes * 60 * 1000.0, ms = baseMs;
            if (fe == null || !fe.Auth)
            {
                ms = Math.Min(baseMs * Math.Pow(2, Math.Min(failures, 8)), 30 * 60 * 1000.0);
                if (fe != null && fe.RetryAfterSec > 0) ms = Math.Max(ms, fe.RetryAfterSec * 1000.0);
            }
            slow.Stop(); slow.Interval = (int)Math.Max(ms, baseMs); slow.Start();
            DateTime next = DateTime.Now.AddMilliseconds(slow.Interval);
            stale = true;
            error = err + (fe != null && fe.Auth ? "" : " — nouvel essai à " + next.ToString("HH:mm"));
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-error.txt"), DateTime.Now + " " + err); } catch { }
        }
        Render();
        Trim();
    }

    // DateTime est un champ d'une classe MarshalByRefObject (Form) : on passe par une copie locale (evite CS1690).
    string LastOkText() { DateTime t = lastOk; return t.ToString("HH:mm"); }

    // Rend au systeme les pages memoire inutilisees (le working set retombe a quelques Mo).
    static void Trim()
    {
        try { GC.Collect(); Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1)); } catch { }
    }

    // ---------- rendu ----------
    static bool LightTheme()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return k != null && Convert.ToInt32(k.GetValue("SystemUsesLightTheme", 0)) == 1;
        }
        catch { return false; }
    }

    Color Normal()
    {
        if (cfg.TextColor.StartsWith("#") && cfg.TextColor.Length == 7)
            try { return ColorTranslator.FromHtml(cfg.TextColor); } catch { }
        return LightTheme() ? Color.FromArgb(25, 25, 25) : Color.White;
    }

    // Couleur des barres (panneau + icone) : suit les seuils sur le quota restant.
    Color BarColor(Limit l)
    {
        if (stale || !l.Has) return Color.FromArgb(150, 150, 150);
        double rem = 100 - l.UsedNow();
        if (rem <= cfg.CritPercent) return Color.FromArgb(255, 90, 90);
        if (rem <= cfg.WarnPercent) return Color.FromArgb(255, 185, 60);
        return Color.FromArgb(217, 119, 87);
    }

    Color ColorFor(Limit l)
    {
        if (stale || !l.Has) return Color.FromArgb(150, 150, 150);
        double rem = 100 - l.UsedNow();
        if (rem <= cfg.CritPercent) return Color.FromArgb(255, 90, 90);
        if (rem <= cfg.WarnPercent) return Color.FromArgb(255, 185, 60);
        return Normal();
    }

    // Valeur affichee (0-100) : restant ou utilise selon la config.
    double Disp(Limit l)
    {
        double v = cfg.ShowRemaining ? 100 - l.UsedNow() : l.UsedNow();
        return Math.Max(0, Math.Min(100, v));
    }

    string Val(Limit l)
    {
        if (!l.Has) return "--";
        return Math.Round(Disp(l)).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    float Scale() { return ScaleFor(PickScreen()); }

    static float ScaleFor(Screen sc)
    {
        try
        {
            uint dx, dy;
            var mon = Native.MonitorFromPoint(new Native.POINT(sc.Bounds.Left + sc.Bounds.Width / 2, sc.Bounds.Top + sc.Bounds.Height / 2), 2);
            if (Native.GetDpiForMonitor(mon, 0, out dx, out dy) == 0) return dx / 96f;
        }
        catch { }
        return 1f;
    }

    void Render()
    {
        if (!IsHandleCreated) return;
        float s = Scale();
        var pad = (int)Math.Round(4 * s);
        var fmt = StringFormat.GenericTypographic; fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        var rows = new[] { new { L = cfg.LabelSession, V = Val(session), C = ColorFor(session) },
                           new { L = cfg.LabelWeek, V = Val(week), C = ColorFor(week) } };
        using (var font = new Font(cfg.FontName, cfg.FontSize * 96f / 72f * s, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var probe = new Bitmap(1, 1))
        using (var pg = Graphics.FromImage(probe))
        {
            pg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            float lh = font.GetHeight(pg), gap = 4 * s;
            float lw = Math.Max(pg.MeasureString(rows[0].L, font, 999, fmt).Width, pg.MeasureString(rows[1].L, font, 999, fmt).Width);
            float vw = Math.Max(Math.Max(pg.MeasureString(rows[0].V, font, 999, fmt).Width, pg.MeasureString(rows[1].V, font, 999, fmt).Width), 1);
            bool stack = cfg.Layout != "line";
            float tw, th;
            if (stack) { tw = lw + gap + vw; th = lh * 2; }
            else
            {
                float w0 = pg.MeasureString(rows[0].L, font, 999, fmt).Width, w1 = pg.MeasureString(rows[1].L, font, 999, fmt).Width;
                tw = w0 + gap + vw + gap * 2 + w1 + gap + vw; th = lh;
            }
            w = (int)Math.Ceiling(tw) + pad * 2; h = (int)Math.Ceiling(th) + pad * 2;

            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(1, 0, 0, 0)); // quasi transparent mais "cliquable" pour le survol
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                var right = new StringFormat(fmt) { Alignment = StringAlignment.Far };
                float x = pad, y = pad;
                for (int i = 0; i < 2; i++)
                {
                    using (var b = new SolidBrush(rows[i].C))
                    {
                        float lwi = stack ? lw : g.MeasureString(rows[i].L, font, 999, fmt).Width;
                        using (var lb = new SolidBrush(Color.FromArgb(Math.Max(0, Math.Min(100, cfg.LabelOpacity)) * 255 / 100, rows[i].C)))
                            g.DrawString(rows[i].L, font, lb, x, y, fmt);
                        g.DrawString(rows[i].V, font, b, new RectangleF(x + lwi + gap, y, vw, lh), right);
                        if (stack) y += lh; else x += lwi + gap + vw + gap * 2;
                    }
                }
                Blit(bmp);
            }
        }
        tip.SetToolTip(this, TipText());
        UpdateTray();
        if (popup != null) popup.Invalidate();
    }

    void Blit(Bitmap bmp)
    {
        IntPtr sdc = Native.GetDC(IntPtr.Zero), mdc = Native.CreateCompatibleDC(sdc);
        IntPtr hb = bmp.GetHbitmap(Color.FromArgb(0)), old = Native.SelectObject(mdc, hb);
        try
        {
            var size = new Native.SIZE(w, h); var src = new Native.POINT(0, 0); var dst = new Native.POINT(Left, Top);
            var bf = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            Native.UpdateLayeredWindow(Handle, sdc, ref dst, ref size, mdc, ref src, 0, ref bf, 2);
        }
        finally { Native.SelectObject(mdc, old); Native.DeleteObject(hb); Native.DeleteDC(mdc); Native.ReleaseDC(IntPtr.Zero, sdc); }
    }

    static string Dans(Limit l)
    {
        var left = l.Reset - DateTimeOffset.Now;
        return left.TotalSeconds <= 0 ? "déjà réinitialisé"
            : "dans " + (left.Days > 0 ? left.Days + " j " : "") + left.Hours + " h " + left.Minutes.ToString("00") + " min";
    }

    string Block(string title, Limit l)
    {
        if (!l.Has) return title + " : pas de donnees";
        double used = l.UsedNow();
        var r = l.Reset.ToLocalTime();
        string dans = Dans(l);
        return title + " : " + Math.Round(100 - used) + "% restant (" + Math.Round(used) + "% utilisé)\r\nReset : " +
               r.ToString("dddd d MMMM HH:mm", CultureInfo.CurrentCulture) + " (" + dans + ")";
    }

    string TipText()
    {
        var sb = new StringBuilder();
        sb.Append(Block("Session 5h", session)).Append("\r\n\r\n").Append(Block("Semaine", week));
        if (haveData) sb.Append("\r\n\r\nMis à jour : ").Append(LastOkText());
        if (stale) sb.Append("\r\n! Échec d'actualisation : ").Append(error);
        return sb.ToString();
    }

    // ---------- icone de notification ----------
    NotifyIcon ni; Icon trayIcon;

    void UpdateTray()
    {
        if (!cfg.TrayIcon) { if (ni != null) ni.Visible = false; return; }
        if (ni == null)
        {
            ni = new NotifyIcon();
            ni.ContextMenuStrip = ContextMenuStrip;
            ni.MouseClick += delegate(object snd, MouseEventArgs e) { if (e.Button == MouseButtons.Left) TogglePopup(Cursor.Position); };
        }
        ni.Text = "Claude  " + cfg.LabelSession + " " + Val(session) + "  " + cfg.LabelWeek + " " + Val(week);
        var old = trayIcon; trayIcon = MakeIcon(); ni.Icon = trayIcon; if (old != null) old.Dispose();
        ni.Visible = true;
    }

    // Icone : pastille arrondie (orange Claude, ambre ou rouge selon les seuils) avec le % de la session en blanc
    // et une fine jauge blanche en bas pour la semaine.
    Icon MakeIcon()
    {
        int sz = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using (var bmp = new Bitmap(sz, sz, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Color badge = BarColor(session);
            bool amber = !stale && session.Has && 100 - session.UsedNow() <= cfg.WarnPercent && 100 - session.UsedNow() > cfg.CritPercent;
            Color ink = amber ? Color.FromArgb(40, 30, 10) : Color.White; // texte sombre sur l'ambre pour le contraste

            float r = sz * 0.22f;
            using (var path = new GraphicsPath())
            {
                path.AddArc(0, 0, r * 2, r * 2, 180, 90); path.AddArc(sz - r * 2, 0, r * 2, r * 2, 270, 90);
                path.AddArc(sz - r * 2, sz - r * 2, r * 2, r * 2, 0, 90); path.AddArc(0, sz - r * 2, r * 2, r * 2, 90, 90);
                path.CloseFigure();
                using (var bb = new SolidBrush(badge)) g.FillPath(bb, path);
            }

            float bh = Math.Max(2f, sz * 0.11f), bx = sz * 0.14f, bw = sz * 0.72f, by = sz - bh - sz * 0.10f;
            string t = session.Has ? Math.Round(Disp(session)).ToString("0", CultureInfo.InvariantCulture) : "--";
            using (var f = new Font("Segoe UI", sz * (t.Length >= 3 ? 0.46f : 0.62f), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(ink))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(t, f, b, new RectangleF(0, sz * 0.02f, sz, by), sf);

            using (var tb = new SolidBrush(Color.FromArgb(90, ink))) g.FillRectangle(tb, bx, by, bw, bh);
            if (week.Has)
                using (var fb = new SolidBrush(ink))
                    g.FillRectangle(fb, bx, by, Math.Max(1f, (float)(bw * Disp(week) / 100)), bh);

            IntPtr hi = bmp.GetHicon();
            try { return (Icon)Icon.FromHandle(hi).Clone(); } finally { Native.DestroyIcon(hi); }
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (ni != null) { ni.Visible = false; ni.Dispose(); }
        base.OnFormClosed(e);
    }

    // ---------- panneau detaille (clic gauche) ----------
    Popup popup; DateTime popupClosed = DateTime.MinValue;

    void TogglePopup(Point anchor)
    {
        if (popup != null) { popup.Close(); return; }
        if ((DateTime.Now - popupClosed).TotalMilliseconds < 300) return; // le clic qui vient de le fermer
        var sc = Screen.FromPoint(anchor); float s = ScaleFor(sc);
        var p = new Popup(this, s);
        var wa = sc.WorkingArea; int m = (int)(8 * s);
        int x = Math.Max(wa.Left + m, Math.Min(anchor.X - p.Width / 2, wa.Right - p.Width - m));
        p.Location = new Point(x, wa.Bottom - p.Height - m);
        p.FormClosed += delegate { popup = null; popupClosed = DateTime.Now; tip.Active = true; };
        tip.Active = false; // pas d'infobulle par-dessus le panneau
        popup = p;
        p.Show(); p.Activate();
    }

    class Popup : Form
    {
        readonly Widget o; readonly float s;
        readonly Color bg, fg, sub, track, accent = Color.FromArgb(217, 119, 87);
        Rectangle refreshRect;

        public Popup(Widget owner, float scale)
        {
            o = owner; s = scale;
            bool light = LightTheme();
            bg = light ? Color.FromArgb(249, 249, 249) : Color.FromArgb(36, 36, 36);
            fg = light ? Color.FromArgb(25, 25, 25) : Color.White;
            sub = light ? Color.FromArgb(105, 105, 105) : Color.FromArgb(160, 160, 160);
            track = light ? Color.FromArgb(225, 225, 225) : Color.FromArgb(66, 66, 66);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            TopMost = true; DoubleBuffered = true; KeyPreview = true; BackColor = bg;
            int wpx = (int)(300 * s), hpx;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) hpx = Draw(g, wpx, false);
            ClientSize = new Size(wpx, hpx);
        }

        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ClassStyle |= 0x20000; p.ExStyle |= 0x80; return p; } // ombre + hors Alt-Tab
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int v = 2; // coins arrondis Windows 11
            try { Native.DwmSetWindowAttribute(Handle, 33, ref v, 4); } catch { }
        }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(bg); Draw(e.Graphics, ClientSize.Width, true); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = refreshRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default; base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && refreshRect.Contains(e.Location)) o.Refresh_();
            base.OnMouseUp(e);
        }

        static void FillRound(Graphics g, Color c, RectangleF r)
        {
            using (var path = new GraphicsPath())
            {
                float d = r.Height;
                path.AddArc(r.X, r.Y, d, d, 90, 180); path.AddArc(r.Right - d, r.Y, d, d, 270, 180); path.CloseFigure();
                using (var b = new SolidBrush(c)) g.FillPath(b, path);
            }
        }

        // Dessine le panneau (ou le mesure si paint=false) et renvoie sa hauteur.
        int Draw(Graphics g, int W, bool paint)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float p = 16 * s, y = p, iw = W - 2 * p;
            string fn = o.cfg.FontName;
            Func<float, FontStyle, Font> mk = delegate(float pt, FontStyle st) { return new Font(fn, pt * 96f / 72f * s, st, GraphicsUnit.Pixel); };
            var sfL = new StringFormat(StringFormat.GenericTypographic); sfL.FormatFlags |= StringFormatFlags.NoWrap; sfL.Trimming = StringTrimming.EllipsisCharacter;
            var sfR = new StringFormat(sfL); sfR.Alignment = StringAlignment.Far;
            using (Font fT = mk(11, FontStyle.Bold), fL = mk(10, FontStyle.Regular), fV = mk(10, FontStyle.Bold), fS = mk(8.5f, FontStyle.Regular))
            {
                Action<string, Font, Color, float, bool> T = delegate(string str, Font f, Color c, float yy, bool right)
                {
                    if (!paint || string.IsNullOrEmpty(str)) return;
                    using (var b = new SolidBrush(c)) g.DrawString(str, f, b, new RectangleF(p, yy, iw, f.GetHeight(g) + 2), right ? sfR : sfL);
                };

                T("Claude Code", fT, fg, y, false);
                if (o.haveData) T("Mis à jour " + o.LastOkText(), fS, sub, y + 2 * s, true);
                y += fT.GetHeight(g) + 14 * s;

                for (int i = 0; i < 2; i++)
                {
                    Limit l = i == 0 ? o.session : o.week;
                    double used = l.Has ? l.UsedNow() : 0;
                    T(i == 0 ? "Session (5 h)" : "Semaine (7 j)", fL, fg, y, false);
                    T(l.Has ? Math.Round(used).ToString("0", CultureInfo.InvariantCulture) + "% utilisé" : "--", fV, fg, y, true);
                    y += fL.GetHeight(g) + 6 * s;

                    float bh = 8 * s;
                    if (paint)
                    {
                        FillRound(g, track, new RectangleF(p, y, iw, bh));
                        if (l.Has && used > 0)
                            FillRound(g, o.BarColor(l), new RectangleF(p, y, Math.Max(bh, (float)(iw * Math.Min(100, used) / 100)), bh));
                    }
                    y += bh + 6 * s;

                    T(l.Has ? Math.Round(100 - used).ToString("0", CultureInfo.InvariantCulture) + "% restant" : "", fS, sub, y, false);
                    T(l.Has ? Dans(l) : "", fS, sub, y, true);
                    y += fS.GetHeight(g) + 1 * s;
                    T(l.Has ? "Reset : " + l.Reset.ToLocalTime().ToString("dddd d MMMM HH:mm", CultureInfo.CurrentCulture) : "", fS, sub, y, false);
                    y += fS.GetHeight(g) + 14 * s;
                }

                if (o.stale && o.error != null)
                {
                    // message d'erreur : retour a la ligne (la hauteur du panneau s'adapte, Draw sert aussi a mesurer)
                    using (var sfW = new StringFormat(StringFormat.GenericTypographic))
                    {
                        string msg = "! " + o.error;
                        float mh = g.MeasureString(msg, fS, (int)iw, sfW).Height + 2;
                        if (paint)
                            using (var b = new SolidBrush(Color.FromArgb(255, 185, 60)))
                                g.DrawString(msg, fS, b, new RectangleF(p, y, iw, mh), sfW);
                        y += mh + 6 * s;
                    }
                }
                T("Actualisation auto : toutes les " + o.cfg.RefreshMinutes + " min", fS, sub, y, false);
                T("Actualiser", fS, accent, y, true);
                float rw = g.MeasureString("Actualiser", fS, 999, sfL).Width;
                refreshRect = Rectangle.Round(new RectangleF(W - p - rw - 4 * s, y - 2 * s, rw + 8 * s, fS.GetHeight(g) + 4 * s));
                y += fS.GetHeight(g);
            }
            return (int)Math.Ceiling(y + p);
        }
    }

    // ---------- placement ----------
    Screen PickScreen()
    {
        var all = Screen.AllScreens; var m = cfg.Monitor;
        int n;
        if (int.TryParse(m, out n) && n >= 1 && n <= all.Length) return all[n - 1];
        if (m == "primary") return Screen.PrimaryScreen;
        foreach (var s in all) if (!s.Primary) return s;
        return Screen.PrimaryScreen;
    }

    IntPtr tbCache = IntPtr.Zero; // taskbar secondaire deja trouvee (evite un EnumWindows a chaque tick)

    bool TaskbarRect(Screen sc, out Native.RECT r, out IntPtr tbWnd)
    {
        r = new Native.RECT(); tbWnd = IntPtr.Zero;
        if (sc.Primary)
        {
            tbWnd = Native.FindWindow("Shell_TrayWnd", null);
            return tbWnd != IntPtr.Zero && Native.GetWindowRect(tbWnd, out r);
        }
        var b = sc.Bounds;
        if (tbCache != IntPtr.Zero && Native.IsWindow(tbCache) && Native.GetWindowRect(tbCache, out r))
        {
            int ccx = (r.Left + r.Right) / 2, ccy = (r.Top + r.Bottom) / 2;
            if (ccx >= b.Left && ccx < b.Right && ccy >= b.Top && ccy < b.Bottom) { tbWnd = tbCache; return true; }
        }
        Native.RECT found = new Native.RECT(); bool ok = false; IntPtr foundWnd = IntPtr.Zero;
        Native.EnumWindows(delegate(IntPtr hw, IntPtr l)
        {
            var sb = new StringBuilder(64); Native.GetClassName(hw, sb, 64);
            Native.RECT rr;
            if (sb.ToString() == "Shell_SecondaryTrayWnd" && Native.GetWindowRect(hw, out rr))
            {
                int cx = (rr.Left + rr.Right) / 2, cy = (rr.Top + rr.Bottom) / 2;
                if (cx >= b.Left && cx < b.Right && cy >= b.Top && cy < b.Bottom) { found = rr; foundWnd = hw; ok = true; return false; }
            }
            return true;
        }, IntPtr.Zero);
        r = found; tbWnd = foundWnd; tbCache = foundWnd; return ok;
    }

    // Position (px physiques) du bord gauche du premier bouton de la zone horloge/systeme, via UI Automation.
    // Execute en tache de fond, rarement ; le resultat est mis en cache.
    int clockLeft = int.MinValue; IntPtr clockHwnd = IntPtr.Zero; bool detecting;
    static readonly string[] TrayClasses = { "TrayNotifyWnd", "SystemTray.OmniButton", "SystemTray.OmniButtonCenter",
                                             "SystemTray.AccentButton", "SystemTray.NormalButton" };
    void DetectClock(IntPtr tb)
    {
        if (detecting || tb == IntPtr.Zero) return;
        detecting = true;
        ThreadPool.QueueUserWorkItem(delegate
        {
            int best = int.MinValue;
            try
            {
                var el = System.Windows.Automation.AutomationElement.FromHandle(tb);
                var conds = new List<System.Windows.Automation.Condition>();
                foreach (var cn in TrayClasses)
                    conds.Add(new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ClassNameProperty, cn));
                var any = new System.Windows.Automation.OrCondition(conds.ToArray());
                int min = int.MaxValue;
                // bord gauche de toute la zone systeme (icones, reseau/volume/batterie, horloge)
                foreach (System.Windows.Automation.AutomationElement e in el.FindAll(System.Windows.Automation.TreeScope.Descendants, any))
                {
                    var rc = e.Current.BoundingRectangle;
                    if (rc.Width > 0 && rc.Left < min) min = (int)rc.Left;
                }
                if (min != int.MaxValue) best = min;
            }
            catch { }
            clockLeft = best; clockHwnd = tb; detecting = false;
            Trim();
        });
    }

    bool ForegroundIsFullscreen(Screen sc)
    {
        // Windows sait si une vraie appli plein ecran tourne (2 = plein ecran, 3 = D3D/jeu, 4 = presentation).
        // Les volets systeme (centre de notifications, Demarrer, recherche...) couvrent l'ecran mais ne comptent pas.
        int state;
        if (Native.SHQueryUserNotificationState(out state) != 0 || (state != 2 && state != 3 && state != 4)) return false;
        var fg = Native.GetForegroundWindow(); Native.RECT r;
        if (fg == IntPtr.Zero || fg == Handle || !Native.GetWindowRect(fg, out r)) return false;
        var sb = new StringBuilder(64); Native.GetClassName(fg, sb, 64); var c = sb.ToString();
        if (c == "Progman" || c == "WorkerW" || c.StartsWith("Shell_")) return false;
        var b = sc.Bounds;
        return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }

    Native.RECT lastTb;

    void Reposition()
    {
        if (!IsHandleCreated) return;
        var sc = PickScreen(); float s = ScaleFor(sc);
        if (ForegroundIsFullscreen(sc)) { Native.ShowWindow(Handle, 0); return; }
        Native.RECT t; IntPtr tbWnd;
        int x, y;
        if (TaskbarRect(sc, out t, out tbWnd))
        {
            int tw = t.Right - t.Left, th = t.Bottom - t.Top;
            bool fresh = clockHwnd == tbWnd && clockLeft != int.MinValue; // resultat valable pour CETTE taskbar
            // tout de suite si la taskbar a change (autre ecran, taille, DPI), sinon toutes les 5 min (10 s apres un echec)
            bool moved = t.Left != lastTb.Left || t.Top != lastTb.Top || t.Right != lastTb.Right || t.Bottom != lastTb.Bottom;
            lastTb = t;
            if (cfg.RightAuto && (clockHwnd != tbWnd || moved || ticks % (fresh ? 150 : 5) == 0)) DetectClock(tbWnd);
            bool auto = cfg.RightAuto && fresh && clockLeft > t.Left && clockLeft < t.Right;
            if (tw >= th)
            {
                x = (auto ? clockLeft - (int)(8 * s) : t.Right - (int)(cfg.RightOffset * s)) - w;
                y = t.Top + (th - h) / 2;
            }
            else { x = t.Left + (tw - w) / 2; y = t.Bottom - (int)(cfg.RightOffset * s) - h; }
        }
        else { var b = sc.Bounds; x = b.Right - (int)(cfg.RightOffset * s) - w; y = b.Bottom - (int)(48 * s) + ((int)(48 * s) - h) / 2; }
        Native.ShowWindow(Handle, 4); // SW_SHOWNOACTIVATE
        Native.SetWindowPos(Handle, HWND_TOPMOST, x, y, 0, 0, 0x0001 | 0x0010 | 0x0040); // NOSIZE|NOACTIVATE|SHOWWINDOW
    }

    int ticks; DateTime credStamp;
    void Tick()
    {
        if (++ticks % 10 == 0) Trim(); // toutes les 20 s
        // Claude Code renouvelle le token dans .credentials.json : si on est en erreur, on reessaie aussitot.
        try
        {
            DateTime st = File.GetLastWriteTimeUtc(CredentialsPath());
            if (st != credStamp) { bool first = credStamp == default(DateTime); credStamp = st; if (!first && stale) Refresh_(); }
        }
        catch { }
        try
        {
            if (File.GetLastWriteTimeUtc(Config.Path) != cfg.Stamp)
            {
                int old = cfg.RefreshMinutes; cfg = Config.Load();
                if (old != cfg.RefreshMinutes) ApplyInterval();
                Render();
            }
        }
        catch { }
        Reposition();
    }
}

static class Program
{
    public const string Version = "1.1.0";

    [STAThread]
    static void Main()
    {
        bool created;
        using (new Mutex(true, "Claudiometer_single", out created))
        {
            if (!created) return;
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object snd, ThreadExceptionEventArgs e)
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-error.txt"), DateTime.Now + " " + e.Exception); } catch { }
            };
            Application.EnableVisualStyles();
            Application.Run(new Widget());
        }
    }
}

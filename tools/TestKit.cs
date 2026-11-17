using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class TestKit
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    delegate bool EnumProc(IntPtr h, IntPtr p);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int L, T, R, B; }

    static string LogPath { get { return Path.Combine(Path.GetTempPath(), "testkit.log"); } }
    static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine); } catch { }
    }

    [STAThread]
    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "help";
        if (mode == "target") { Target(args); return; }
        if (mode == "hotkey") { Hotkey(args); return; }
        if (mode == "keys") { Keys(args); return; }
        if (mode == "act") { Act(args); return; }
        if (mode == "web") { Web(args); return; }
        if (mode == "sw") { Sw(args); return; }
        if (mode == "elev") { Elev(args); return; }
        if (mode == "ping") { Ping(args); return; }
        if (mode == "hk2") { HotkeySendInput(args); return; }
        if (mode == "pid") { PidProbe(args); return; }
        if (mode == "mods") { ModState(); return; }
        if (mode == "hk3") { HotkeyCtrlAltT(); return; }
        if (mode == "shot") { Shot(args); return; }
        if (mode == "windows") { Windows(args); return; }
        if (mode == "crop") { Crop(args); return; }
        Console.WriteLine("modes: target <text> <ms> | hotkey [n] | shot <png> | windows [filter]");
    }

    static void Target(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string text = args.Length > 1 ? args[1] : "serendipity";
        int life = 20000;
        if (args.Length > 2) int.TryParse(args[2], out life);

        Form f = new Form();
        f.Text = "TranslatePop Test Target";
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(240, 220);
        f.Size = new Size(700, 320);
        f.TopMost = true;
        TextBox tb = new TextBox();
        tb.Multiline = true;
        tb.Dock = DockStyle.Fill;
        tb.Font = new Font("Consolas", 18f);
        tb.Text = text + "\r\n\r\ncontext line";
        f.Controls.Add(tb);
        f.Shown += delegate
        {
            f.Activate();
            SetForegroundWindow(f.Handle);
            tb.Focus();
            tb.Select(0, text.Length);
            SetCursorPos(f.Left + 300, f.Top + 60);
            Log("target shown sel=" + tb.SelectionLength);
            System.Windows.Forms.Timer keep = new System.Windows.Forms.Timer();
            keep.Interval = 900;
            keep.Tick += delegate (object s2, EventArgs e2)
            {
                keep.Stop();
                keep.Dispose();
                f.Activate();
                SetForegroundWindow(f.Handle);
                tb.Focus();
                tb.Select(0, text.Length);
                SetCursorPos(f.Left + 300, f.Top + 60);
                Log("reassert sel=" + tb.SelectionLength);
            };
            keep.Start();
        };
        System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
        t.Interval = Math.Max(2000, life);
        t.Tick += delegate { t.Stop(); f.Close(); };
        t.Start();
        Application.Run(f);
        Log("target closed");
    }

    static void Hotkey(string[] args)
    {
        int n = 1;
        if (args.Length > 1) int.TryParse(args[1], out n);
        byte win = 0x5B;
        byte grave = 0xC0;
        uint up = 0x0002;
        // flush any stuck modifiers first
        byte[] mods = new byte[] { 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x10, 0x11, 0x12 };
        for (int m = 0; m < mods.Length; m++) keybd_event(mods[m], 0, up, UIntPtr.Zero);
        Thread.Sleep(120);
        for (int i = 0; i < n; i++)
        {
            keybd_event(win, 0, 0, UIntPtr.Zero);
            Thread.Sleep(30);
            keybd_event(grave, 0, 0, UIntPtr.Zero);
            Thread.Sleep(40);
            keybd_event(grave, 0, up, UIntPtr.Zero);
            Thread.Sleep(30);
            keybd_event(win, 0, up, UIntPtr.Zero);
            Log("hotkey sent #" + (i + 1));
            if (i < n - 1) Thread.Sleep(500);
        }
    }

    static void Shot(string[] args)
    {
        string png = args.Length > 1 ? args[1] : "shot.png";
        Rectangle r = SystemInformation.VirtualScreen;
        using (Bitmap bmp = new Bitmap(r.Width, r.Height))
        {
            using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size);
            bmp.Save(png, ImageFormat.Png);
        }
        Log("shot " + png);
    }

    static void Windows(string[] args)
    {
        string filter = args.Length > 1 ? args[1].ToLowerInvariant() : "";
        IntPtr fg = GetForegroundWindow();
        List<string> lines = new List<string>();
        EnumWindows(delegate (IntPtr h, IntPtr p)
        {
            if (!IsWindowVisible(h)) return true;
            StringBuilder t = new StringBuilder(512);
            GetWindowTextW(h, t, 512);
            StringBuilder c = new StringBuilder(256);
            GetClassNameW(h, c, 256);
            RECT r;
            GetWindowRect(h, out r);
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            string title = t.ToString();
            string cls = c.ToString();
            string line = "0x" + h.ToInt64().ToString("X") + " pid=" + pid + " cls=" + cls + " rect=" + r.L + "," + r.T + " " + (r.R - r.L) + "x" + (r.B - r.T) + " title='" + title + "'";
            if (filter.Length > 0 && line.ToLowerInvariant().IndexOf(filter) < 0) return true;
            if (h == fg) line = "[FG] " + line;
            lines.Add(line);
            return true;
        }, IntPtr.Zero);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("foreground=0x" + fg.ToInt64().ToString("X") + " count=" + lines.Count);
        foreach (string l in lines) sb.AppendLine(l);
        string outp = args.Length > 2 ? args[2] : null;
        if (outp != null) File.WriteAllText(outp, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine(sb.ToString());
        Log("windows listed " + lines.Count);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct INPUT { public uint type; public int a; public int b; public int c; public int d; public int e; public int f; }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint n, INPUT[] p, int cb);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint mapType);

    static void HotkeySendInput(string[] args)
    {
        int n = 1;
        if (args.Length > 1) int.TryParse(args[1], out n);
        const uint KEYUP = 0x0002, SCAN = 0x0008;
        int cb = 7 * 4;
        // flush modifiers first
        ushort[] mods = new ushort[] { 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 };
        for (int i = 0; i < n; i++)
        {
            for (int m = 0; m < mods.Length; m++)
            {
                INPUT km = new INPUT(); km.type = 1; km.a = mods[m]; km.b = (int)MapVirtualKey(mods[m], 0); km.c = (int)(KEYUP | SCAN);
                SendInput(1, new INPUT[] { km }, cb);
            }
            Thread.Sleep(80);
            ushort win = 0x5B, grave = 0xC0;
            INPUT[] seq = new INPUT[4];
            int ku = (int)(KEYUP | SCAN), kd = (int)SCAN;
            seq[0].type = 1; seq[0].a = win;   seq[0].b = (int)MapVirtualKey(win, 0);   seq[0].c = kd;
            seq[1].type = 1; seq[1].a = grave; seq[1].b = (int)MapVirtualKey(grave, 0); seq[1].c = kd;
            seq[2].type = 1; seq[2].a = grave; seq[2].b = (int)MapVirtualKey(grave, 0); seq[2].c = ku;
            seq[3].type = 1; seq[3].a = win;   seq[3].b = (int)MapVirtualKey(win, 0);   seq[3].c = ku;
            uint sent = SendInput(4, seq, cb);
            Log("hk2 sent #" + (i + 1) + " n=" + sent + " err=" + Marshal.GetLastWin32Error());
            if (i < n - 1) Thread.Sleep(500);
        }
    }

    static void Ping(string[] args)
    {
        IntPtr h = new IntPtr(Convert.ToInt64(args[1], 16));
        UIntPtr res;
        bool ok = SendMessageTimeout(h, 0x0000, IntPtr.Zero, IntPtr.Zero, 0x0002, 1500, out res);
        string line = "ping " + args[1] + " ok=" + ok + (ok ? "" : " err=" + Marshal.GetLastWin32Error());
        Log(line);
        Console.WriteLine(line);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out UIntPtr result);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int GetWindowThreadProcessId(IntPtr h, out int pid);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool ProcessIdToSessionId(int pid, out uint sid);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    static void HotkeyCtrlAltT()
    {
        uint up = 0x0002;
        byte[] down = new byte[] { 0xA2, 0xA5, 0x54 };
        for (int i = 0; i < down.Length; i++)
        {
            int st = GetAsyncKeyState(down[i]);
            if ((st & 0x8000) != 0) keybd_event(down[i], 0, up, UIntPtr.Zero);
        }
        Thread.Sleep(50);
        for (int i = 0; i < down.Length; i++) { keybd_event(down[i], 0, 0, UIntPtr.Zero); Thread.Sleep(35); }
        Thread.Sleep(40);
        for (int i = down.Length - 1; i >= 0; i--) { keybd_event(down[i], 0, up, UIntPtr.Zero); Thread.Sleep(25); }
        Log("hk3 ctrl+alt+t sent");
    }

    static void ModState()
    {
        int[] vks = new int[] { 0x5B, 0x5C, 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x14, 0x90 };
        string[] names = new string[] { "LWIN", "RWIN", "SHIFT", "CTRL", "ALT", "LSHIFT", "RSHIFT", "LCTRL", "RCTRL", "LALT", "RALT", "CAPS", "NUMLOCK" };
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < vks.Length; i++)
        {
            short st = GetAsyncKeyState(vks[i]);
            bool down = (st & 0x8000) != 0;
            bool pressed = (st & 1) != 0;
            if (down || pressed) sb.Append(names[i] + "=" + (down ? "DOWN" : "") + (pressed ? "(pressed)" : "") + " ");
        }
        string line = "mods: " + (sb.Length == 0 ? "none down" : sb.ToString());
        Log(line);
        Console.WriteLine(line);
    }

    static void PidProbe(string[] args)
    {
        IntPtr h = new IntPtr(Convert.ToInt64(args[1], 16));
        int pid;
        int tid = GetWindowThreadProcessId(h, out pid);
        IntPtr ph = OpenProcess(0x1000, false, pid);
        int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        StringBuilder img = new StringBuilder(600);
        int cap = 600;
        uint sid = 999;
        string imgpath = "?";
        if (ph != IntPtr.Zero)
        {
            if (QueryFullProcessImageName(ph, 0, img, ref cap)) imgpath = img.ToString();
            ProcessIdToSessionId(pid, out sid);
        }
        string elev = "?";
        if (ph != IntPtr.Zero)
        {
            IntPtr tok;
            if (OpenProcessToken(ph, 0x0008, out tok))
            {
                int len; byte[] buf = new byte[64];
                if (GetTokenInformation(tok, 20, buf, buf.Length, out len)) elev = buf[0] != 0 ? "ELEVATED" : "normal";
                CloseHandle(tok);
            }
            CloseHandle(ph);
        }
        bool vmRead = false;
        if (ph == IntPtr.Zero) { IntPtr p2 = OpenProcess(0x0410, false, pid); vmRead = p2 != IntPtr.Zero; if (vmRead) CloseHandle(p2); }
        string line = "pidinfo hwnd=" + args[1] + " pid=" + pid + " session=" + sid + " openLimited=" + (ph != IntPtr.Zero) + " vmRead=" + vmRead + " elev=" + elev + " img=" + imgpath;
        Log(line);
        Console.WriteLine(line);
    }

    static void Elev(string[] args)
    {
        string name = args.Length > 1 ? args[1] : "idea64";
        foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName(name))
        {
            string r = "?";
            IntPtr h = OpenProcess(0x1000, false, p.Id);
            if (h != IntPtr.Zero)
            {
                IntPtr tok;
                if (OpenProcessToken(h, 0x0008, out tok))
                {
                    int len;
                    byte[] buf = new byte[64];
                    int tn = 20; // TokenElevation
                    if (GetTokenInformation(tok, tn, buf, buf.Length, out len)) r = buf[0] != 0 ? "elevated" : "normal";
                    CloseHandle(tok);
                }
                CloseHandle(h);
            }
            Log("elev " + name + " pid=" + p.Id + " -> " + r);
            Console.WriteLine("elev " + name + " pid=" + p.Id + " -> " + r);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint acc, bool inh, int pid);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [System.Runtime.InteropServices.DllImport("advapi32.dll")] static extern bool OpenProcessToken(IntPtr p, uint acc, out IntPtr tok);
    [System.Runtime.InteropServices.DllImport("advapi32.dll")] static extern bool GetTokenInformation(IntPtr tok, int cls, byte[] buf, int len, out int ret);

    static void Sw(string[] args)
    {
        IntPtr h = new IntPtr(Convert.ToInt64(args[1], 16));
        int code = args.Length > 2 ? int.Parse(args[2]) : 9;
        ShowWindow(h, code);
        Thread.Sleep(400);
        Log("sw " + args[1] + " code=" + code);
    }

    static void Web(string[] args)
    {
        Application.EnableVisualStyles();
        string url = args.Length > 1 ? args[1] : "D:/imagination/Translate/tools/sample.html";
        Form f = new Form();
        f.Text = "TranslatePop Browser Target";
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(200, 180);
        f.Size = new Size(760, 420);
        f.TopMost = true;
        WebBrowser wb = new WebBrowser();
        wb.Dock = DockStyle.Fill;
        wb.ScriptErrorsSuppressed = true;
        wb.Navigate(url);
        f.Controls.Add(wb);
        f.Shown += delegate { Log("web shown hwnd=0x" + f.Handle.ToInt64().ToString("X")); };
        Application.Run(f);
    }

    static void Act(string[] args)
    {
        long h = Convert.ToInt64(args[1], 16);
        IntPtr hwnd = new IntPtr(h);
        ShowWindow(hwnd, 9);
        bool ok = SetForegroundWindow(hwnd);
        Thread.Sleep(500);
        Log("act " + args[1] + " fg=" + ok);
        if (args.Length > 2 && args[2] == "drag")
        {
            int x1 = int.Parse(args[3]); int y1 = int.Parse(args[4]);
            int x2 = int.Parse(args[5]); int y2 = int.Parse(args[6]);
            Cursor.Position = new Point(x1, y1);
            Thread.Sleep(150);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(120);
            for (int i = 1; i <= 12; i++)
            {
                Cursor.Position = new Point(x1 + (x2 - x1) * i / 12, y1 + (y2 - y1) * i / 12);
                Thread.Sleep(25);
            }
            Thread.Sleep(120);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(250);
            Log("drag " + x1 + "," + y1 + " -> " + x2 + "," + y2);
        }
        if (args.Length > 2 && args[2] == "dbl")
        {
            int x = int.Parse(args[3]);
            int y = int.Parse(args[4]);
            Cursor.Position = new Point(x, y);
            Thread.Sleep(120);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(60);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            Log("dbl " + x + "," + y);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);

    static void Keys(string[] args)
    {
        string seq = args.Length > 1 ? args[1] : "^a";
        System.Windows.Forms.SendKeys.SendWait(seq);
        Log("keys " + seq);
    }

    static void Crop(string[] args)
    {
        string src = args.Length > 1 ? args[1] : "shot.png";
        string dst = args.Length > 2 ? args[2] : "crop.png";
        int x = args.Length > 3 ? int.Parse(args[3]) : 0;
        int y = args.Length > 4 ? int.Parse(args[4]) : 0;
        int w = args.Length > 5 ? int.Parse(args[5]) : 400;
        int h = args.Length > 6 ? int.Parse(args[6]) : 200;
        using (Bitmap b = new Bitmap(src))
        using (Bitmap o = new Bitmap(w, h))
        {
            using (Graphics g = Graphics.FromImage(o)) g.DrawImage(b, new Rectangle(0, 0, w, h), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
            o.Save(dst, ImageFormat.Png);
        }
        Log("crop " + dst);
    }
}
